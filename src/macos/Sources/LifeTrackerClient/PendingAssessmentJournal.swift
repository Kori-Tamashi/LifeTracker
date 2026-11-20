import Darwin
import Foundation

@MainActor
public protocol PendingAssessmentJournal: AnyObject {
    func load() throws -> AssessmentCommand?
    func store(_ command: AssessmentCommand) throws
    func clear() throws
}

/// One desktop writer per runtime root. Pending mutations survive a lost response or restart.
@MainActor
public final class FilePendingAssessmentJournal: PendingAssessmentJournal {
    private let file: URL
    private let descriptor: Int32

    public init(runtimeRoot: URL) throws {
        file = runtimeRoot.appendingPathComponent("desktop-pending-assessment.json")
        let lock = runtimeRoot.appendingPathComponent("desktop-assessment.lock")
        let descriptor = Darwin.open(lock.path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard descriptor >= 0 else { throw LocalAPIError.invalidResponse }
        guard flock(descriptor, LOCK_EX | LOCK_NB) == 0 else {
            Darwin.close(descriptor)
            throw LocalAPIError.invalidResponse
        }
        self.descriptor = descriptor
    }

    deinit { Darwin.close(descriptor) }

    public func load() throws -> AssessmentCommand? {
        guard FileManager.default.fileExists(atPath: file.path) else { return nil }
        return try JSONDecoder().decode(AssessmentCommand.self, from: Data(contentsOf: file))
    }

    public func store(_ command: AssessmentCommand) throws {
        try JSONEncoder().encode(command).write(to: file, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
    }

    public func clear() throws {
        if FileManager.default.fileExists(atPath: file.path) { try FileManager.default.removeItem(at: file) }
    }
}
