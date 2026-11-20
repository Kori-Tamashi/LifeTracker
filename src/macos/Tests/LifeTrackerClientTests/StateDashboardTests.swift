import Foundation
import Testing

@testable import LifeTrackerClient

private actor SequenceLoader: CurrentStateLoading {
    var calls = 0
    let state: CurrentState
    let cancel: Bool

    init(state: CurrentState, cancel: Bool = false) {
        self.state = state
        self.cancel = cancel
    }

    func currentState() async throws -> CurrentState {
        calls += 1
        if cancel { throw CancellationError() }
        if calls > 1 { throw LocalAPIError.httpStatus(503) }
        return state
    }
}

private actor PausedDashboardLoader: CurrentStateLoading {
    let initial: CurrentState
    let updated: CurrentState
    var calls = 0
    private var waiting: CheckedContinuation<Void, Never>?
    private var pending: CheckedContinuation<Void, Never>?
    init(initial: CurrentState, updated: CurrentState) {
        self.initial = initial
        self.updated = updated
    }
    func currentState() async throws -> CurrentState {
        calls += 1
        if calls == 1 {
            waiting?.resume()
            waiting = nil
            await withCheckedContinuation { pending = $0 }
            return initial
        }
        return updated
    }
    func waitForFirst() async { if calls == 0 { await withCheckedContinuation { waiting = $0 } } }
    func release() {
        pending?.resume()
        pending = nil
    }
}

struct StateDashboardTests {
    private func snapshot() throws -> CurrentState {
        try CurrentState.decode(
            Data(
                """
                {"metrics":[],"balance":7.5,"level":"good","computed_at":"2026-01-15T12:00:00Z","valid_until":"2026-01-15T13:00:00Z","rule_version":"state-v1"}
                """.utf8))
    }

    @Test @MainActor func wakeOrSaveRefreshDuringLoadingFetchesANewSnapshotImmediately() async throws {
        let initial = try snapshot()
        let updated = try CurrentState.decode(
            Data(
                """
                {"metrics":[],"balance":6,"level":"satisfactory","computed_at":"2026-01-15T12:01:00Z","valid_until":"2026-01-15T13:00:00Z","rule_version":"state-v1"}
                """.utf8))
        let loader = PausedDashboardLoader(initial: initial, updated: updated)
        let dashboard = StateDashboard(loader: loader)
        let refresh = Task { await dashboard.refresh() }
        await loader.waitForFirst()
        await dashboard.refresh()
        await dashboard.refresh()
        await loader.release()
        await refresh.value
        #expect(await loader.calls == 2)
        #expect(dashboard.state?.balance == 6 && !dashboard.isLoading)
    }

    @Test @MainActor func expiryHidesBalanceWithoutRecomputingOrDiscardingSnapshot() async throws {
        let state = try snapshot()
        let dashboard = StateDashboard(loader: SequenceLoader(state: state))
        #expect(!dashboard.snapshotIsCurrent(at: state.computedAt))
        await dashboard.refresh()
        #expect(dashboard.snapshotIsCurrent(at: state.validUntil!.addingTimeInterval(-0.001)))
        #expect(!dashboard.snapshotIsCurrent(at: state.validUntil!))
        #expect(dashboard.state?.balance == 7.5)
        #expect(!dashboard.isLoading)
    }

    @Test @MainActor func failedRefreshKeepsLastSnapshotButDoesNotClaimItIsCurrent() async throws {
        let state = try snapshot()
        let dashboard = StateDashboard(loader: SequenceLoader(state: state))
        await dashboard.refresh()
        await dashboard.refresh()
        #expect(dashboard.state?.balance == 7.5)
        #expect(dashboard.errorMessage != nil)
        #expect(!dashboard.snapshotIsCurrent(at: state.computedAt))
        #expect(!dashboard.isLoading)
    }

    @Test @MainActor func cancellationDoesNotBecomeConnectionError() async throws {
        let dashboard = StateDashboard(loader: SequenceLoader(state: try snapshot(), cancel: true))
        await dashboard.refresh()
        #expect(dashboard.errorMessage == nil)
        #expect(dashboard.state == nil)
        #expect(!dashboard.isLoading)
    }
}
