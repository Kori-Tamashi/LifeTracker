import Foundation
import Testing

@testable import LifeTrackerClient

private func wakeClient() throws -> LocalAPIClient {
    try LocalAPIClient(baseURL: URL(string: "http://127.0.0.1:17841/api/v1")!, ownerToken: "synthetic-wake")
}

private actor WakeProbe {
    var calls = 0
    let owned: Bool
    init(owned: Bool) { self.owned = owned }
    func connect() throws -> LocalAPIClient {
        calls += 1
        if owned && calls == 1 || calls == (owned ? 3 : 2) { throw URLError(.cannotConnectToHost) }
        return try wakeClient()
    }
}

private actor WakeGate {
    var calls = 0
    private var waiting: CheckedContinuation<Void, Never>?
    private var pending: CheckedContinuation<Void, Never>?
    func connect() async throws -> LocalAPIClient {
        calls += 1
        if calls == 2 {
            waiting?.resume()
            waiting = nil
            await withCheckedContinuation { pending = $0 }
        }
        return try wakeClient()
    }
    func waitForRecovery() async {
        if calls < 2 { await withCheckedContinuation { waiting = $0 } }
    }
    func release() {
        pending?.resume()
        pending = nil
    }
}

struct WakeReconciliationTests {
    private func configuration() -> NativeHostConfiguration {
        NativeHostConfiguration(
            executable: URL(fileURLWithPath: "/bin/sleep"), arguments: ["30"],
            root: FileManager.default.temporaryDirectory, port: 17841)
    }

    @Test @MainActor func ownedHostSurvivesTemporaryWakeFailureAndRecoversWithoutRestart() async {
        let probe = WakeProbe(owned: true)
        let runtime = NativeRuntime { _, _ in try await probe.connect() }
        await runtime.start(configuration())
        #expect(runtime.ownsHost)
        #expect(!(await runtime.reconcile()))
        #expect(runtime.phase == .degraded && runtime.ownsHost && runtime.client != nil)
        #expect(await runtime.reconcile())
        #expect(runtime.phase == .ready && runtime.ownsHost && runtime.errorMessage == nil)
        #expect(await probe.calls == 4)
        #expect(await runtime.stop())
    }

    @Test @MainActor func externalConnectionRecoveryNeverAcquiresProcessOwnership() async {
        let probe = WakeProbe(owned: false)
        let runtime = NativeRuntime { _, _ in try await probe.connect() }
        await runtime.start(configuration())
        #expect(!(await runtime.reconcile()))
        #expect(runtime.phase == .degraded && !runtime.ownsHost)
        #expect(await runtime.reconcile())
        #expect(runtime.phase == .ready && !runtime.ownsHost)
        #expect(await runtime.stop())
    }

    @Test @MainActor func repeatedWakeIsCoalescedAndStopWinsOverInFlightProbe() async {
        let gate = WakeGate()
        let runtime = NativeRuntime { _, _ in try await gate.connect() }
        await runtime.start(configuration())
        let recovery = Task { await runtime.reconcile() }
        await gate.waitForRecovery()
        #expect(runtime.phase == .recovering)
        #expect(!(await runtime.reconcile()))
        #expect(await gate.calls == 2)
        #expect(await runtime.stop())
        await gate.release()
        #expect(!(await recovery.value))
        #expect(runtime.phase == .stopped && runtime.client == nil)
    }

    @Test @MainActor func cancellationRetainsPreviousReadyConnection() async {
        let gate = WakeGate()
        let runtime = NativeRuntime { _, _ in try await gate.connect() }
        await runtime.start(configuration())
        let recovery = Task { await runtime.reconcile() }
        await gate.waitForRecovery()
        recovery.cancel()
        await gate.release()
        #expect(!(await recovery.value))
        #expect(runtime.phase == .ready && runtime.client != nil && runtime.errorMessage == nil)
        #expect(await runtime.stop())
    }

    @Test @MainActor func wakeCannotStartAStoppedRuntime() async {
        let gate = WakeGate()
        let runtime = NativeRuntime { _, _ in try await gate.connect() }
        #expect(!(await runtime.reconcile()))
        #expect(await gate.calls == 0)
        #expect(runtime.phase == .stopped)
    }
}
