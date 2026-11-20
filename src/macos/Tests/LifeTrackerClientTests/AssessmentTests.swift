import Foundation
import Testing

@testable import LifeTrackerClient

private actor AssessmentServiceStub: AssessmentServicing {
    var commands: [AssessmentCommand] = []
    var cursors: [String?] = []
    let rejection: Bool
    let failFirst: Bool

    init(failFirst: Bool = false, rejection: Bool = false) {
        self.failFirst = failFirst
        self.rejection = rejection
    }

    func submit(_ command: AssessmentCommand) async throws -> AssessmentReceipt {
        commands.append(command)
        if rejection { throw LocalAPIError.rejected(409, "REVISION_CONFLICT") }
        if failFirst, commands.count == 1 { throw URLError(.networkConnectionLost) }
        return AssessmentReceipt(commandId: command.id, status: "completed")
    }

    func history(cursor: String?) async throws -> AssessmentPage {
        cursors.append(cursor)
        return AssessmentPage(items: [], nextCursor: cursor == nil ? "snapshot+/=" : nil)
    }
}

@MainActor
private final class FailingJournal: PendingAssessmentJournal {
    func load() throws -> AssessmentCommand? { nil }
    func store(_ command: AssessmentCommand) throws { throw CocoaError(.fileWriteOutOfSpace) }
    func clear() throws {}
}

private actor BackupBlockedService: AssessmentServicing {
    private var entered = false
    private var entryWaiter: CheckedContinuation<Void, Never>?
    private var submitWaiter: CheckedContinuation<Void, Never>?
    func submit(_ command: AssessmentCommand) async throws -> AssessmentReceipt {
        entered = true
        entryWaiter?.resume()
        entryWaiter = nil
        await withCheckedContinuation { submitWaiter = $0 }
        return AssessmentReceipt(commandId: command.id, status: "completed")
    }
    func history(cursor: String?) async throws -> AssessmentPage { AssessmentPage(items: [], nextCursor: nil) }
    func waitForSubmit() async {
        if !entered { await withCheckedContinuation { entryWaiter = $0 } }
    }
    func release() {
        submitWaiter?.resume()
        submitWaiter = nil
    }
}

struct AssessmentTests {
    private func command() throws -> AssessmentCommand {
        try .record(
            observedAt: Date(timeIntervalSince1970: 1_800_000_000), values: [.init(metric: .mood, score: 7.5)],
            comment: "")
    }

    private func client() throws -> LocalAPIClient {
        try LocalAPIClient(baseURL: URL(string: "http://127.0.0.1:17841/api/v1")!, ownerToken: "synthetic")
    }

    private func temporaryRoot() throws -> URL {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        return root
    }

    @Test func partialRecordUsesExactBodyAndStableDeliveryIdentity() throws {
        let command = try command()
        let client = try client()
        let first = client.mutationRequest(command)
        let retry = client.mutationRequest(command)
        #expect(first == retry)
        #expect(first.httpMethod == "POST")
        #expect(first.url?.path == "/api/v1/assessments")
        #expect(first.value(forHTTPHeaderField: "Idempotency-Key") == command.id.uuidString)
        #expect(first.value(forHTTPHeaderField: "X-Command-ID") == command.id.uuidString)
        let bytes = try #require(first.httpBody)
        let body = try #require(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
        #expect(Set(body.keys) == ["observed_at", "values", "comment"])
        let values = try #require(body["values"] as? [[String: Any]])
        #expect(values.count == 1)
        #expect(values[0]["metric"] as? String == "mood")
        #expect(values[0]["score"] as? Double == 7.5)
    }

    @Test func validationRejectsEmptyDuplicateAndOffStepValues() {
        for values: [AssessmentValue] in [
            [], [.init(metric: .mood, score: 7.2)], [.init(metric: .mood, score: .nan)],
            [.init(metric: .energy, score: 8), .init(metric: .energy, score: 8)],
        ] {
            #expect(throws: LocalAPIError.self) {
                try AssessmentCommand.record(observedAt: .now, values: values, comment: "")
            }
        }
    }

    @Test func revisionAndCursorPreserveContract() throws {
        let data = Data(
            """
            {"id":"11111111-1111-4111-8111-111111111111","revision":3,"metric":"anxiety","score":4.5,"observed_at":"2026-01-15T12:00:00.1234567Z","source_kind":"self_report","status":"accepted"}
            """.utf8)
        let record = try StateJSON.decoder().decode(AssessmentRecord.self, from: data)
        let command = try AssessmentCommand.revise(
            record, observedAt: record.observedAt, score: 3, reason: "Synthetic correction")
        let request = try client().mutationRequest(command)
        #expect(request.httpMethod == "PATCH")
        #expect(request.url?.lastPathComponent.lowercased() == record.id.uuidString.lowercased())
        let bytes = try #require(request.httpBody)
        let body = try #require(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
        #expect(body["expected_revision"] as? Int == 3)
        #expect(Set(body.keys) == ["expected_revision", "score", "observed_at", "reason"])
        let cursor = "a+/= &?"
        let history = try client().historyRequest(cursor: cursor)
        let historyURL = try #require(history.url)
        #expect(URLComponents(url: historyURL, resolvingAgainstBaseURL: false)?.queryItems?.last?.value == cursor)
        #expect(throws: LocalAPIError.self) {
            try AssessmentCommand.revise(record, observedAt: .now, score: 3, reason: " ")
        }
    }

    @Test @MainActor func retryAfterRestartRetainsPayloadAndCommandId() async throws {
        let root = try temporaryRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let service = AssessmentServiceStub(failFirst: true)
        let command = try command()
        var workspace: AssessmentWorkspace? = try AssessmentWorkspace(
            service: service, journal: FilePendingAssessmentJournal(runtimeRoot: root))
        await workspace!.save(command)
        #expect(workspace?.pending?.id == command.id)
        workspace = nil
        let restarted = try AssessmentWorkspace(
            service: service, journal: FilePendingAssessmentJournal(runtimeRoot: root))
        #expect(restarted.pending?.id == command.id)
        await restarted.retrySave()
        #expect(restarted.pending == nil)
        #expect(restarted.savedCount == 1)
        let sent = await service.commands
        #expect(sent.count == 2)
        #expect(sent[0].id == sent[1].id)
        #expect(sent[0].body == sent[1].body)
        #expect(
            !FileManager.default.fileExists(atPath: root.appendingPathComponent("desktop-pending-assessment.json").path)
        )
    }

    @Test @MainActor func conflictDoesNotOverwriteNewerRevision() async throws {
        let root = try temporaryRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let service = AssessmentServiceStub(rejection: true)
        let workspace = try AssessmentWorkspace(
            service: service, journal: FilePendingAssessmentJournal(runtimeRoot: root))
        await workspace.save(try command())
        await workspace.retrySave()
        #expect(workspace.pending == nil)
        #expect(workspace.savedCount == 0)
        #expect(workspace.saveError?.contains("уже изменена") == true)
        #expect(await service.commands.count == 1)
    }

    @Test @MainActor func diskFailurePreventsDelivery() async throws {
        let service = AssessmentServiceStub()
        let workspace = try AssessmentWorkspace(service: service, journal: FailingJournal())
        await workspace.save(try command())
        #expect(await service.commands.isEmpty)
        #expect(workspace.saveError != nil)
    }

    @Test @MainActor func paginationStopsAtEndAndResetDropsCursor() async throws {
        let root = try temporaryRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let service = AssessmentServiceStub()
        let workspace = try AssessmentWorkspace(
            service: service, journal: FilePendingAssessmentJournal(runtimeRoot: root))
        await workspace.loadHistory()
        await workspace.loadHistory(reset: false)
        await workspace.loadHistory(reset: false)
        await workspace.loadHistory()
        #expect(await service.cursors == [nil, "snapshot+/=", nil])
    }

    @Test @MainActor func journalRejectsSecondWriterAndCorruption() throws {
        let root = try temporaryRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let journal = try FilePendingAssessmentJournal(runtimeRoot: root)
        #expect(throws: LocalAPIError.self) { try FilePendingAssessmentJournal(runtimeRoot: root) }
        try Data("invalid".utf8).write(to: root.appendingPathComponent("desktop-pending-assessment.json"))
        #expect(throws: (any Error).self) { try journal.load() }
    }
    @Test @MainActor func backupCannotReleaseJournalDuringInFlightSubmission() async throws {
        let root = try temporaryRoot()
        defer { try? FileManager.default.removeItem(at: root) }
        let journal = try FilePendingAssessmentJournal(runtimeRoot: root)
        let service = BackupBlockedService()
        let workspace = try AssessmentWorkspace(service: service, journal: journal)
        let command = try command()
        let submission = Task { await workspace.save(command) }
        await service.waitForSubmit()
        #expect(workspace.saving)
        #expect(throws: LocalAPIError.self) { try workspace.beginBackupPause() }
        #expect(throws: LocalAPIError.self) { _ = try FilePendingAssessmentJournal(runtimeRoot: root) }
        await service.release()
        await submission.value
        #expect(!workspace.saving && workspace.pending == nil)
        try workspace.beginBackupPause()
        await workspace.save(command)
        #expect(workspace.pending == nil && workspace.savedCount == 1)
        try workspace.endBackupPause()
    }

}
