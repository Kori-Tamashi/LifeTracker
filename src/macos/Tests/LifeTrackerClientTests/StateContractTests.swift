import Foundation
import Testing

@testable import LifeTrackerClient

struct StateContractTests {
    private func payload(
        balance: String = "null", level: String = "undefined", score: String = "null", source: String = "unknown",
        fresh: Bool = false
    ) -> Data {
        let metrics = ["energy", "mood", "anxiety", "physical_wellbeing"].map {
            """
            {"metric":"\($0)","score":\(score),"source_kind":"\(source)","observed_at":null,"revision_id":null,"fresh":\(fresh)}
            """
        }.joined(separator: ",")
        return Data(
            """
            {"metrics":[\(metrics)],"balance":\(balance),"level":"\(level)","computed_at":"2026-01-15T12:00:00.1234567Z","valid_until":null,"rule_version":"state-v1"}
            """.utf8)
    }

    @Test func unknownStateRemainsUnknown() throws {
        let state = try CurrentState.decode(payload())
        #expect(state.balance == nil)
        #expect(state.level == .undefined)
        #expect(state.metrics.count == 4)
        #expect(state.metrics.allSatisfy { $0.score == nil && $0.sourceKind == .unknown && !$0.fresh })
    }

    @Test func staleValuesAreNotSilentlyDiscardedOrRebalanced() throws {
        let state = try CurrentState.decode(payload(score: "8", source: "self_report"))
        #expect(state.balance == nil)
        #expect(state.metrics.allSatisfy { $0.score == 8 && !$0.fresh })
    }

    @Test func serverBalanceAndAiProvenanceArePreserved() throws {
        let state = try CurrentState.decode(
            payload(balance: "7.5", level: "good", score: "8", source: "ai_estimate", fresh: true))
        #expect(state.balance == 7.5)
        #expect(state.level == .good)
        #expect(state.metrics.allSatisfy { $0.sourceKind == .aiEstimate && $0.fresh })
        #expect(state.metrics.last?.metric == .physicalWellbeing)
    }

    @Test func localRequestUsesContractRouteAndInjectedCredential() throws {
        let client = try LocalAPIClient(
            baseURL: URL(string: "http://127.0.0.1:17841/api/v1")!, ownerToken: "synthetic-test-token")
        let request = client.currentStateRequest()
        #expect(request.url?.absoluteString == "http://127.0.0.1:17841/api/v1/state/current")
        #expect(request.httpMethod == "GET")
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer synthetic-test-token")
    }

    @Test func rejectsRemoteEndpointAndEmptyCredential() {
        #expect(throws: LocalAPIError.self) {
            try LocalAPIClient(baseURL: URL(string: "https://example.invalid/api/v1")!, ownerToken: "synthetic")
        }
        #expect(throws: LocalAPIError.self) {
            try LocalAPIClient(baseURL: URL(string: "http://127.0.0.1:17841/api/v1")!, ownerToken: "")
        }
    }
}
