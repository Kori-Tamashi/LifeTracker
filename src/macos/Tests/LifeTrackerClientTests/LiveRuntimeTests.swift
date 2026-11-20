import Darwin
import Foundation
import Testing

@testable import LifeTrackerClient

@Suite(.serialized)
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
            #expect(await runtime.reconcile())
            #expect(runtime.phase == .ready && runtime.ownsHost)
            let beforeStop = try await client.history()
            #expect(beforeStop.items.count == 1)
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

    @Test(.enabled(if: ProcessInfo.processInfo.environment["LIFETRACKER_INTEGRATION_URL"] != nil))
    @MainActor func killedCorePreservesPendingReceiptOnRestart() async throws {
        let environment = ProcessInfo.processInfo.environment
        let executable = try #require(environment["LIFETRACKER_INTEGRATION_DOTNET"])
        let dll = try #require(environment["LIFETRACKER_INTEGRATION_DLL"])
        let port = try #require(environment["LIFETRACKER_MANAGED_TEST_PORT"].flatMap(Int.init))
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let pidFile = root.appendingPathComponent("synthetic-child.pid")
        // exec preserves the PID recorded by our wrapper; never discover or signal by name/port.
        let configuration = NativeHostConfiguration(
            executable: URL(fileURLWithPath: "/bin/sh"),
            arguments: [
                "-c", "echo $$ > \"$1\"; exec \"$2\" \"$3\"", "synthetic-host",
                pidFile.path, executable, dll,
            ], root: root, port: port)
        let runtime = NativeRuntime()
        do {
            await runtime.start(configuration)
            #expect(runtime.phase == .ready && runtime.ownsHost)
            let client = try #require(runtime.client)
            let command = try AssessmentCommand.record(
                observedAt: .now, values: [.init(metric: .energy, score: 8)], comment: "")
            let journal = try FilePendingAssessmentJournal(runtimeRoot: root)
            try journal.store(command)
            let receipt = try await client.submit(command)
            // Model an acknowledged commit whose response the desktop did not persist/clear.
            let pid = try #require(
                Int32(
                    String(contentsOf: pidFile, encoding: .utf8)
                        .trimmingCharacters(in: .whitespacesAndNewlines)))
            #expect(Darwin.kill(pid, SIGKILL) == 0)
            let clock = ContinuousClock()
            let deadline = clock.now.advanced(by: .seconds(5))
            while runtime.phase != .failed, clock.now < deadline {
                try await Task.sleep(for: .milliseconds(50))
            }
            #expect(runtime.phase == .failed && !runtime.ownsHost && runtime.client == nil)
            #expect(try journal.load()?.id == command.id)
            await runtime.start(configuration)
            #expect(runtime.phase == .ready && runtime.ownsHost)
            let restarted = try #require(runtime.client)
            let replay = try await restarted.submit(command)
            #expect(replay.commandId == receipt.commandId)
            let workspace = try AssessmentWorkspace(service: restarted, journal: journal)
            #expect(workspace.pending?.id == command.id)
            await workspace.retrySave()
            #expect(workspace.pending == nil && workspace.saveError == nil && workspace.savedCount == 1)
            #expect(try journal.load() == nil)
            let history = try await restarted.history()
            #expect(history.items.count == 1 && history.items.first?.score == 8)
            #expect(await runtime.stop())
        } catch {
            await runtime.stop()
            throw error
        }
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["LIFETRACKER_INTEGRATION_URL"] != nil))
    @MainActor func stoppingAttachedRuntimeLeavesExternalCoreRunning() async throws {
        let environment = ProcessInfo.processInfo.environment
        let root = URL(fileURLWithPath: try #require(environment["LIFETRACKER_INTEGRATION_ROOT"]))
        let endpoint = try #require(environment["LIFETRACKER_INTEGRATION_URL"])
        let address = try #require(URL(string: endpoint))
        let port = try #require(address.port)
        let runtime = NativeRuntime()
        await runtime.start(
            NativeHostConfiguration(
                executable: URL(fileURLWithPath: "/usr/bin/false"), arguments: [], root: root, port: port))
        #expect(runtime.phase == .ready && !runtime.ownsHost)
        #expect(await runtime.stop())
        #expect(runtime.phase == .stopped)
        let stillRunning = try await NativeRuntime.connect(root: root, port: port)
        _ = try await stillRunning.currentState()
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["LIFETRACKER_INTEGRATION_URL"] != nil))
    @MainActor func offlineMaintenanceRunnerRestoresProfileAndRefusesLiveWriter() async throws {
        let environment = ProcessInfo.processInfo.environment
        let executable = URL(fileURLWithPath: try #require(environment["LIFETRACKER_INTEGRATION_DOTNET"]))
        let dll = try #require(environment["LIFETRACKER_INTEGRATION_DLL"])
        let port = try #require(environment["LIFETRACKER_MANAGED_TEST_PORT"].flatMap(Int.init))
        let parent = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let source = parent.appendingPathComponent("source")
        let backup = parent.appendingPathComponent("backup with spaces")
        let restored = parent.appendingPathComponent("restored")
        defer { try? FileManager.default.removeItem(at: parent) }
        let runtime = NativeRuntime()
        let tool = OfflineProfileMaintenance(executable: executable, hostArguments: [dll])
        do {
            await runtime.start(
                NativeHostConfiguration(executable: executable, arguments: [dll], root: source, port: port))
            let client = try #require(runtime.client)
            let command = try AssessmentCommand.record(
                observedAt: .now, values: [.init(metric: .energy, score: 8)], comment: "")
            let receipt = try await client.submit(command)
            await #expect(throws: ProfileMaintenanceError.self) {
                try await tool.run(.backup(source: source, destination: backup))
            }
            #expect(!FileManager.default.fileExists(atPath: backup.path))
            #expect(await runtime.stop())
            try await tool.run(.daily(source: source, catalog: parent.appendingPathComponent("daily catalog")))
            try await tool.run(.backup(source: source, destination: backup))
            try await tool.run(.verify(backup: backup))
            try await tool.run(.restore(backup: backup, destination: restored))
            await #expect(throws: ProfileMaintenanceError.self) {
                try await tool.run(.restore(backup: backup, destination: restored))
            }
            await runtime.start(
                NativeHostConfiguration(executable: executable, arguments: [dll], root: restored, port: port))
            let recovered = try #require(runtime.client)
            let replay = try await recovered.submit(command)
            #expect(replay.commandId == receipt.commandId)
            let history = try await recovered.history()
            #expect(history.items.count == 1 && history.items.first?.score == 8)
            #expect(await runtime.stop())
        } catch {
            await runtime.stop()
            throw error
        }
    }

}
