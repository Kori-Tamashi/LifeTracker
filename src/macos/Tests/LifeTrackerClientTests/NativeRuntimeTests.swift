import Foundation
import Testing

@testable import LifeTrackerClient

private func syntheticClient() throws -> LocalAPIClient {
    try LocalAPIClient(baseURL: URL(string: "http://127.0.0.1:17841/api/v1")!, ownerToken: "synthetic")
}

private actor RuntimeProbeSequence {
    var calls = 0
    func probe() throws -> LocalAPIClient {
        calls += 1
        if calls == 1 { throw URLError(.cannotConnectToHost) }
        return try syntheticClient()
    }
}

private actor RuntimeProbeGate {
    private var entered = false
    private var waiting: CheckedContinuation<Void, Never>?
    private var pending: CheckedContinuation<Void, Never>?
    func probe() async throws -> LocalAPIClient {
        entered = true
        waiting?.resume()
        waiting = nil
        await withCheckedContinuation { pending = $0 }
        return try syntheticClient()
    }
    func waitForEntry() async {
        if !entered { await withCheckedContinuation { waiting = $0 } }
    }
    func release() {
        pending?.resume()
        pending = nil
    }
}

struct NativeRuntimeTests {
    private func configuration(executable: String = "/bin/sleep") -> NativeHostConfiguration {
        NativeHostConfiguration(
            executable: URL(fileURLWithPath: executable), arguments: ["30"],
            root: FileManager.default.temporaryDirectory, port: 17841)
    }

    @Test @MainActor func stoppingAttachedRuntimeDoesNotSignalExternalProcess() async throws {
        let external = Process()
        external.executableURL = URL(fileURLWithPath: "/bin/sleep")
        external.arguments = ["30"]
        try external.run()
        defer {
            if external.isRunning { external.terminate() }
            external.waitUntilExit()
        }
        let runtime = NativeRuntime { _, _ in try syntheticClient() }
        await runtime.start(configuration(executable: "/missing/synthetic"))
        #expect(runtime.phase == .ready)
        #expect(!runtime.ownsHost)
        var operated = false
        await #expect(throws: LocalAPIError.self) {
            try await runtime.withOfflinePause { operated = true }
        }
        #expect(!operated && external.isRunning)
        #expect(await runtime.stop())
        #expect(external.isRunning)
    }

    @Test @MainActor func ownedProcessStartsOnceAndStops() async {
        let probe = RuntimeProbeSequence()
        let runtime = NativeRuntime { _, _ in try await probe.probe() }
        await runtime.start(configuration())
        #expect(runtime.phase == .ready && runtime.ownsHost)
        await runtime.start(configuration())
        #expect(await probe.calls == 2)
        #expect(await runtime.stop())
        #expect(runtime.phase == .stopped && !runtime.ownsHost && runtime.client == nil)
    }

    @Test @MainActor func timeoutAndFailedLaunchLeaveNoOwnedProcess() async {
        let runtime = NativeRuntime { _, _ in throw URLError(.cannotConnectToHost) }
        await runtime.start(configuration(), timeout: .milliseconds(100))
        #expect(runtime.phase == .failed && !runtime.ownsHost)
        #expect(runtime.errorMessage != nil)
        await runtime.start(configuration(executable: "/missing/synthetic"))
        #expect(runtime.phase == .failed && !runtime.ownsHost)
        #expect(await runtime.stop())
    }

    @Test @MainActor func unexpectedExitClearsReadyState() async {
        let probe = RuntimeProbeSequence()
        let runtime = NativeRuntime { _, _ in try await probe.probe() }
        let configuration = NativeHostConfiguration(
            executable: URL(fileURLWithPath: "/bin/sleep"), arguments: ["0.2"],
            root: FileManager.default.temporaryDirectory, port: 17841)
        await runtime.start(configuration)
        let clock = ContinuousClock()
        let deadline = clock.now.advanced(by: .seconds(2))
        while runtime.phase == .ready, clock.now < deadline { try? await Task.sleep(for: .milliseconds(50)) }
        #expect(runtime.phase == .failed && runtime.client == nil && !runtime.ownsHost)
        #expect(await runtime.stop())
    }

    @Test @MainActor func stopDuringProbeCannotLaunchOrReviveAService() async {
        let gate = RuntimeProbeGate()
        let runtime = NativeRuntime { _, _ in try await gate.probe() }
        let startup = Task { await runtime.start(configuration()) }
        await gate.waitForEntry()
        #expect(await runtime.stop())
        await gate.release()
        await startup.value
        #expect(runtime.phase == .stopped && !runtime.ownsHost && runtime.client == nil)
    }
    @Test @MainActor func explicitStopDuringOfflineOperationCannotRestartCore() async {
        let probe = RuntimeProbeSequence()
        let runtime = NativeRuntime { _, _ in try await probe.probe() }
        await runtime.start(configuration())
        let gate = RuntimeProbeGate()
        let operation = Task {
            do {
                try await runtime.withOfflinePause { _ = try await gate.probe() }
                return false
            } catch { return true }
        }
        await gate.waitForEntry()
        #expect(runtime.client != nil && runtime.phase == .recovering)
        #expect(await runtime.stop())
        await gate.release()
        #expect(await operation.value)
        #expect(runtime.phase == .stopped && runtime.client == nil && !runtime.ownsHost)
    }

}
