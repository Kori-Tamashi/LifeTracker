import Darwin
import Foundation

@MainActor
public protocol PendingAssessmentJournal: AnyObject {
    func load() throws -> AssessmentCommand?
    func store(_ command: AssessmentCommand) throws
    func clear() throws
}

@MainActor
public protocol BackupPausableJournal: PendingAssessmentJournal {
    func suspendLease() throws
    func resumeLease() throws
}

/// One desktop writer per runtime root. Pending mutations survive a lost response or restart.
@MainActor
public final class FilePendingAssessmentJournal: BackupPausableJournal {
    private let file: URL
    private var descriptor: Int32
    private let lock: URL
    private var suspendedBytes: Data?
    private var suspended = false

    public init(runtimeRoot: URL) throws {
        file = runtimeRoot.appendingPathComponent("desktop-pending-assessment.json")
        lock = runtimeRoot.appendingPathComponent("desktop-assessment.lock")
        let descriptor = Darwin.open(lock.path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard descriptor >= 0 else { throw LocalAPIError.invalidResponse }
        guard flock(descriptor, LOCK_EX | LOCK_NB) == 0 else {
            Darwin.close(descriptor)
            throw LocalAPIError.invalidResponse
        }
        self.descriptor = descriptor
    }

    deinit { if descriptor >= 0 { Darwin.close(descriptor) } }

    public func load() throws -> AssessmentCommand? {
        guard !suspended else { throw LocalAPIError.invalidResponse }
        guard FileManager.default.fileExists(atPath: file.path) else { return nil }
        return try JSONDecoder().decode(AssessmentCommand.self, from: Data(contentsOf: file))
    }

    public func store(_ command: AssessmentCommand) throws {
        guard !suspended else { throw LocalAPIError.invalidResponse }
        try JSONEncoder().encode(command).write(to: file, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
    }

    public func clear() throws {
        guard !suspended else { throw LocalAPIError.invalidResponse }
        if FileManager.default.fileExists(atPath: file.path) { try FileManager.default.removeItem(at: file) }
    }
    public func suspendLease() throws {
        guard !suspended, descriptor >= 0 else { throw LocalAPIError.invalidResponse }
        suspendedBytes = try bytes()
        suspended = true
        Darwin.close(descriptor)
        descriptor = -1
    }

    public func resumeLease() throws {
        guard suspended else { throw LocalAPIError.invalidResponse }
        let next = Darwin.open(lock.path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard next >= 0 else { throw LocalAPIError.invalidResponse }
        guard flock(next, LOCK_EX | LOCK_NB) == 0 else {
            Darwin.close(next)
            throw LocalAPIError.invalidResponse
        }
        do {
            guard try bytes() == suspendedBytes else { throw LocalAPIError.invalidResponse }
        } catch {
            Darwin.close(next)
            throw error
        }
        descriptor = next
        suspended = false
        suspendedBytes = nil
    }

    private func bytes() throws -> Data? {
        FileManager.default.fileExists(atPath: file.path) ? try Data(contentsOf: file) : nil
    }

}
