import Foundation
import Testing

@testable import LifeTrackerClient

struct LiveRuntimeTests {
    @Test(.enabled(if: ProcessInfo.processInfo.environment["LIFETRACKER_INTEGRATION_URL"] != nil))
    @MainActor func managedHostStopAndRestartPreserveState() async throws {
        let environment = ProcessInfo.processInfo.environment
        let executable = URL(fileURLWithPath: try #require(environment["LIFETRACKER_INTEGRATION_DOTNET"]))
        let dll = try #require(environment["LIFETRACKER_INTEGRATION_DLL"])
        let port = try #require(environment["LIFETRACKER_MANAGED_TEST_PORT"].flatMap(Int.init))
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let configuration = NativeHostConfiguration(executable: executable, arguments: [dll], root: root, port: port)
        let runtime = NativeRuntime()
        do {
            await runtime.start(configuration)
            #expect(runtime.phase == .ready && runtime.ownsHost)
            let client = try #require(runtime.client)
            let command = try AssessmentCommand.record(
                observedAt: .now, values: [.init(metric: .energy, score: 8)], comment: "")
            _ = try await client.submit(command)
            #expect(await runtime.stop())
            await #expect(throws: (any Error).self) { try await client.currentState() }
            await runtime.start(configuration)
            let restarted = try #require(runtime.client)
            let history = try await restarted.history()
            #expect(history.items.count == 1 && history.items.first?.score == 8)
            _ = try await restarted.submit(command)
            #expect(await runtime.stop())
        } catch {
            await runtime.stop()
            throw error
        }
    }
}
