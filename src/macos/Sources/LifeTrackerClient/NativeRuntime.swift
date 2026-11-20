import Darwin
import Foundation
import Observation

public enum RuntimePhase: String, Sendable {
    case stopped, starting, ready, recovering, degraded, stopping, failed
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
    @ObservationIgnored private var activeConfiguration: NativeHostConfiguration?
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
        let healthSession = URLSession(configuration: configuration)
        defer { healthSession.invalidateAndCancel() }
        let client = try LocalAPIClient(
            baseURL: url, ownerToken: token.trimmingCharacters(in: .whitespacesAndNewlines),
            session: healthSession)
        let state = try await client.currentState()
        guard state.ruleVersion == "state-v1" else { throw LocalAPIError.invalidResponse }
        // Readiness has a short deadline; interactive requests retain the normal session timeout.
        let interactiveConfiguration = URLSessionConfiguration.ephemeral
        interactiveConfiguration.connectionProxyDictionary = [:]
        return try LocalAPIClient(
            baseURL: url, ownerToken: token.trimmingCharacters(in: .whitespacesAndNewlines),
            session: URLSession(configuration: interactiveConfiguration))
    }

    public func start(_ configuration: NativeHostConfiguration, timeout: Duration = .seconds(15)) async {
        guard phase == .stopped || phase == .failed, child == nil else { return }
        let startup = UUID()
        generation = startup
        activeConfiguration = configuration
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
            let clock = SuspendingClock()
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

    /// Revalidate after wake without replacing the Process, workspace or pending mutation.
    @discardableResult public func reconcile() async -> Bool {
        guard phase == .ready || phase == .degraded,
            let configuration = activeConfiguration, let token = generation
        else { return false }
        let previous = phase
        phase = .recovering
        do {
            _ = try await probe(configuration.root, configuration.port)
            try Task.checkCancellation()
            guard generation == token, phase == .recovering else { return false }
            if ownsHost, child?.isRunning != true { return false }
            phase = .ready
            errorMessage = nil
            return true
        } catch {
            guard generation == token, phase == .recovering else { return false }
            if Task.isCancelled {
                phase = previous
            } else {
                phase = .degraded
                errorMessage =
                    "Подключение к сервису временно недоступно. Последние данные сохранены; повторите проверку подключения."
            }
            return false
        }
    }

    @discardableResult public func stop(grace: Duration = .seconds(10)) async -> Bool {
        guard phase != .stopping else { return false }
        phase = .stopping
        generation = nil
        client = nil
        if let process = child {
            guard await stopProcess(process, grace: grace) else {
                phase = .failed
                errorMessage = "Не удалось остановить собственный процесс сервиса."
                return false
            }
        }
        child = nil
        activeConfiguration = nil
        ownsHost = false
        phase = .stopped
        return true
    }

    /// Preserve the client/view identity while pausing only our own Core for an offline snapshot.
    public func withOfflinePause(_ operation: @MainActor () async throws -> Void) async throws {
        guard phase == .ready, ownsHost, let process = child, let configuration = activeConfiguration else {
            throw LocalAPIError.invalidResponse
        }
        let token = UUID()
        generation = token
        phase = .recovering
        guard await stopProcess(process, grace: .seconds(10)) else {
            phase = .ready
            Task { await watch(process, token: token) }
            throw LocalAPIError.invalidResponse
        }
        guard generation == token else { throw LocalAPIError.invalidResponse }
        child = nil
        ownsHost = false
        var operationError: (any Error)?
        do { try await operation() } catch { operationError = error }
        guard generation == token else { throw LocalAPIError.invalidResponse }
        phase = .stopped
        await Task {
            guard self.generation == token else { return }
            await self.start(configuration)
        }.value
        guard phase == .ready else { throw LocalAPIError.invalidResponse }
        if let operationError { throw operationError }
    }

    private func stopProcess(_ process: Process, grace: Duration) async -> Bool {
        if process.isRunning { process.terminate() }
        let clock = SuspendingClock()
        let deadline = clock.now.advanced(by: grace)
        while process.isRunning, clock.now < deadline { await pause() }
        if process.isRunning { Darwin.kill(process.processIdentifier, SIGKILL) }
        let killDeadline = clock.now.advanced(by: .seconds(2))
        while process.isRunning, clock.now < killDeadline { await pause() }
        return !process.isRunning
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
