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

struct StateDashboardTests {
    private func snapshot() throws -> CurrentState {
        try CurrentState.decode(
            Data(
                """
                {"metrics":[],"balance":7.5,"level":"good","computed_at":"2026-01-15T12:00:00Z","valid_until":"2026-01-15T13:00:00Z","rule_version":"state-v1"}
                """.utf8))
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
