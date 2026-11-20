import Foundation
import Testing

@testable import LifeTrackerClient

struct LiveAssessmentTests {
    @Test(.enabled(if: ProcessInfo.processInfo.environment["LIFETRACKER_INTEGRATION_URL"] != nil))
    @MainActor func recordRetryHistoryCorrectionAgainstRealHost() async throws {
        let environment = ProcessInfo.processInfo.environment
        let address = try #require(environment["LIFETRACKER_INTEGRATION_URL"])
        let root = URL(fileURLWithPath: try #require(environment["LIFETRACKER_INTEGRATION_ROOT"]))
        let token = try String(contentsOf: root.appendingPathComponent("owner.credential"), encoding: .utf8)
        let client = try LocalAPIClient(baseURL: #require(URL(string: address)), ownerToken: token)
        let workspace = try AssessmentWorkspace(
            service: client, journal: FilePendingAssessmentJournal(runtimeRoot: root))
        let initial = try await client.currentState()
        #expect(initial.balance == nil)
        let command = try AssessmentCommand.record(
            observedAt: .now,
            values: [
                .init(metric: .energy, score: 8), .init(metric: .mood, score: 7.5),
                .init(metric: .anxiety, score: 2), .init(metric: .physicalWellbeing, score: 9),
            ], comment: "Synthetic integration")
        await workspace.save(command)
        #expect(workspace.savedCount == 1)
        #expect(workspace.saveError == nil)
        _ = try await client.submit(command)
        let recorded = try await client.currentState()
        #expect(recorded.balance == 7.5)
        let page = try await client.history()
        #expect(page.items.count == 4)
        let energy = try #require(page.items.first { $0.metric == .energy })
        let correction = try AssessmentCommand.revise(
            energy, observedAt: energy.observedAt, score: 6, reason: "Synthetic correction")
        await workspace.save(correction)
        #expect(workspace.savedCount == 2)
        let corrected = try await client.currentState()
        #expect(corrected.balance == 6)
        let conflict = try AssessmentCommand.revise(
            energy, observedAt: energy.observedAt, score: 5, reason: "Stale synthetic edit")
        await workspace.save(conflict)
        #expect(workspace.savedCount == 2)
        #expect(workspace.saveError?.contains("уже изменена") == true)
        let history = try await client.history()
        #expect(history.items.first { $0.id == energy.id }?.revision == 2)
        #expect(history.items.count == 4)
    }
}
