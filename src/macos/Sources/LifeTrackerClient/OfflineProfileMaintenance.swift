import Foundation

public enum ProfileMaintenanceOperation: Sendable {
    case backup(source: URL, destination: URL)
    case verify(backup: URL)
    case restore(backup: URL, destination: URL)

    var arguments: [String] {
        switch self {
        case .backup(_, let destination): ["backup", destination.path]
        case .verify(let backup): ["verify-backup", backup.path]
        case .restore(let backup, let destination): ["restore", backup.path, destination.path]
        }
    }

    var source: URL? {
        if case .backup(let source, _) = self { source } else { nil }
    }
}

public enum ProfileMaintenanceError: Error { case operationFailed }

/// Runs the same offline Core CLI as the operator tools. Never invokes a shell or copies files itself.
public struct OfflineProfileMaintenance: Sendable {
    public let executable: URL
    public let hostArguments: [String]

    public init(executable: URL, hostArguments: [String]) {
        self.executable = executable
        self.hostArguments = hostArguments
    }

    public func run(_ operation: ProfileMaintenanceOperation) async throws {
        // SQLite maintenance is not cancellable mid-snapshot. The caller keeps the UI/quit gate until exit.
        try await Task.detached {
            let process = Process()
            process.executableURL = executable
            process.arguments = hostArguments + operation.arguments
            var environment = ProcessInfo.processInfo.environment
            if let source = operation.source { environment["LIFETRACKER_RUNTIME_ROOT"] = source.path }
            process.environment = environment
            process.standardInput = FileHandle.nullDevice
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try process.run()
            process.waitUntilExit()
            guard process.terminationReason == .exit, process.terminationStatus == 0 else {
                throw ProfileMaintenanceError.operationFailed
            }
        }.value
    }
}
