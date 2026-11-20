import Foundation
import Testing

@testable import LifeTrackerClient

struct BackupScheduleTests {
    private func date(_ text: String) throws -> Date {
        try #require(ISO8601DateFormatter().date(from: text))
    }

    @Test @MainActor func moscowBoundaryDedupRetryAndMissedDays() throws {
        let schedule = DailyBackupSchedule()
        let morning = try date("2026-11-21T06:00:00Z")
        #expect(!schedule.isDue(at: morning.addingTimeInterval(-1)))
        #expect(schedule.isDue(at: morning))
        schedule.failed(at: morning)
        #expect(!schedule.isDue(at: morning.addingTimeInterval(899)))
        #expect(schedule.isDue(at: morning.addingTimeInterval(900)))
        schedule.succeeded(at: morning.addingTimeInterval(900))
        #expect(!schedule.isDue(at: morning.addingTimeInterval(3600)))
        #expect(schedule.isDue(at: morning.addingTimeInterval(4 * 86400)))
        schedule.succeeded(at: morning.addingTimeInterval(4 * 86400))
        #expect(!schedule.isDue(at: morning.addingTimeInterval(4 * 86400 + 60)))
    }

    @Test @MainActor func beforeNineSuccessCannotSuppressTheMorningAttempt() throws {
        let schedule = DailyBackupSchedule()
        let morning = try date("2026-11-21T06:00:00Z")
        schedule.succeeded(at: morning.addingTimeInterval(-1))
        #expect(schedule.isDue(at: morning))
    }

    @Test @MainActor func journalPauseBlocksWritesAndDetectsLeaseTheft() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let journal = try FilePendingAssessmentJournal(runtimeRoot: root)
        let command = try AssessmentCommand.record(
            observedAt: .now, values: [.init(metric: .energy, score: 8)], comment: "")
        try journal.store(command)
        try journal.suspendLease()
        #expect(throws: LocalAPIError.self) { try journal.clear() }
        #expect(throws: LocalAPIError.self) { try journal.store(command) }
        do {
            let competitor = try FilePendingAssessmentJournal(runtimeRoot: root)
            #expect(throws: LocalAPIError.self) { try journal.resumeLease() }
            // Keep the competitor alive through the failed reacquisition.
            #expect(try competitor.load()?.id == command.id)
        }
        try journal.resumeLease()
        #expect(try journal.load()?.id == command.id)
    }

    @Test @MainActor func changedPendingBytesNeverResumeWriting() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let journal = try FilePendingAssessmentJournal(runtimeRoot: root)
        let first = try AssessmentCommand.record(
            observedAt: .now, values: [.init(metric: .energy, score: 8)], comment: "")
        try journal.store(first)
        try journal.suspendLease()
        do {
            let competitor = try FilePendingAssessmentJournal(runtimeRoot: root)
            try competitor.clear()
            #expect(try competitor.load() == nil)
        }
        #expect(throws: LocalAPIError.self) { try journal.resumeLease() }
        #expect(throws: LocalAPIError.self) { try journal.store(first) }
    }
}
