import Darwin
import Foundation
import Observation

public enum RuntimePhase: String, Sendable {
    case stopped, starting, ready, stopping, failed
}

public struct NativeHostConfiguration: Sendable {
    public let executable: URL
    public let arguments: [String]
    public let root: URL
    public let port: Int

    public init(executable: URL, arguments: [String], root: URL, port: Int) {
        self.executable = executable
        self.arguments = arguments
        self.root = root
        self.port = port
    }
}

/// Owns only the child Process it created. No persisted PID or name-based signalling.
@MainActor @Observable
public final class NativeRuntime {
    public private(set) var phase: RuntimePhase = .stopped
    public private(set) var client: LocalAPIClient?
    public private(set) var ownsHost = false
    public private(set) var errorMessage: String?
    @ObservationIgnored private var child: Process?
    @ObservationIgnored private var generation: UUID?
    private let probe: @Sendable (URL, Int) async throws -> LocalAPIClient

    public init(probe: @escaping @Sendable (URL, Int) async throws -> LocalAPIClient = NativeRuntime.connect) {
        self.probe = probe
    }

    nonisolated public static func connect(root: URL, port: Int) async throws -> LocalAPIClient {
        guard (1...65535).contains(port), let url = URL(string: "http://127.0.0.1:\(port)/api/v1") else {
            throw LocalAPIError.invalidLocalEndpoint
        }
        let token = try String(contentsOf: root.appendingPathComponent("owner.credential"), encoding: .utf8)
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 1
        configuration.timeoutIntervalForResource = 2
        configuration.connectionProxyDictionary = [:]
        let client = try LocalAPIClient(
            baseURL: url, ownerToken: token.trimmingCharacters(in: .whitespacesAndNewlines),
            session: URLSession(configuration: configuration))
        let state = try await client.currentState()
        guard state.ruleVersion == "state-v1" else { throw LocalAPIError.invalidResponse }
        return client
    }

    public func start(_ configuration: NativeHostConfiguration, timeout: Duration = .seconds(15)) async {
        guard phase == .stopped || phase == .failed, child == nil else { return }
        let startup = UUID()
        generation = startup
        phase = .starting
        errorMessage = nil
        do {
            guard configuration.root.isFileURL, configuration.executable.isFileURL,
                (1...65535).contains(configuration.port)
            else { throw LocalAPIError.invalidLocalEndpoint }
            if let existing = try? await probe(configuration.root, configuration.port) {
                guard generation == startup, phase == .starting else { return }
                try Task.checkCancellation()
                client = existing
                ownsHost = false
                phase = .ready
                return
            }
            guard generation == startup, phase == .starting else { return }
            try Task.checkCancellation()
            let process = Process()
            process.executableURL = configuration.executable
            process.arguments = configuration.arguments
            process.environment = ProcessInfo.processInfo.environment.merging([
                "LIFETRACKER_RUNTIME_ROOT": configuration.root.path,
                "LIFETRACKER_PORT": String(configuration.port),
            ]) { _, value in value }
            process.standardInput = FileHandle.nullDevice
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try process.run()
            child = process
            ownsHost = true
            let clock = ContinuousClock()
            let deadline = clock.now.advanced(by: timeout)
            while process.isRunning, clock.now < deadline {
                try Task.checkCancellation()
                if let ready = try? await probe(configuration.root, configuration.port) {
                    guard generation == startup, phase == .starting else { return }
                    try Task.checkCancellation()
                    guard process.isRunning else { throw LocalAPIError.invalidResponse }
                    client = ready
                    phase = .ready
                    Task { await watch(process, token: startup) }
                    return
                }
                try await Task.sleep(for: .milliseconds(100))
            }
            throw LocalAPIError.invalidResponse
        } catch {
            guard generation == startup else { return }
            _ = await stop()
            guard generation == nil, phase == .stopped else { return }
            phase = .failed
            errorMessage = "Сервис не запустился. Проверьте .NET 10, доступ к папке данных, порт и состояние профиля."
        }
    }

    @discardableResult public func stop(grace: Duration = .seconds(10)) async -> Bool {
        guard phase != .stopping else { return false }
        phase = .stopping
        generation = nil
        client = nil
        if let process = child {
            if process.isRunning { process.terminate() }
            let clock = ContinuousClock()
            let deadline = clock.now.advanced(by: grace)
            while process.isRunning, clock.now < deadline { await pause() }
            if process.isRunning { Darwin.kill(process.processIdentifier, SIGKILL) }
            let killDeadline = clock.now.advanced(by: .seconds(2))
            while process.isRunning, clock.now < killDeadline { await pause() }
            guard !process.isRunning else {
                phase = .failed
                errorMessage = "Не удалось остановить собственный процесс сервиса."
                return false
            }
        }
        child = nil
        ownsHost = false
        phase = .stopped
        return true
    }

    private func watch(_ process: Process, token: UUID) async {
        while generation == token, process.isRunning { try? await Task.sleep(for: .milliseconds(250)) }
        guard generation == token else { return }
        child = nil
        generation = nil
        client = nil
        ownsHost = false
        phase = .failed
        errorMessage = "Сервис завершился. Сохранённые данные остаются в профиле; можно повторить запуск."
    }

    private func pause() async {
        // Cleanup must still yield when the caller was cancelled during startup.
        await Task.detached { try? await Task.sleep(for: .milliseconds(50)) }.value
    }
}
