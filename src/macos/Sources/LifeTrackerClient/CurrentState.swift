import Foundation

public enum StateMetric: String, Decodable, Sendable {
    case energy, mood, anxiety
    case physicalWellbeing = "physical_wellbeing"
}

public enum AssessmentSource: String, Decodable, Sendable {
    case selfReport = "self_report"
    case aiEstimate = "ai_estimate"
    case unknown
}

public enum BalanceLevel: String, Decodable, Sendable {
    case undefined, attention, satisfactory, good, excellent
}

public struct SelectedMetric: Decodable, Sendable {
    public let metric: StateMetric
    public let score: Double?
    public let sourceKind: AssessmentSource
    public let observedAt: Date?
    public let revisionId: String?
    public let fresh: Bool
}

/// Server-computed state. The client does not recalculate domain policy.
public struct CurrentState: Decodable, Sendable {
    public let metrics: [SelectedMetric]
    public let balance: Double?
    public let level: BalanceLevel
    public let computedAt: Date
    public let validUntil: Date?
    public let ruleVersion: String

    public static func decode(_ data: Data) throws -> CurrentState {
        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .convertFromSnakeCase
        decoder.dateDecodingStrategy = .custom { value in
            let container = try value.singleValueContainer()
            let text = try container.decode(String.self)
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            if let date = formatter.date(from: text) { return date }
            formatter.formatOptions = [.withInternetDateTime]
            guard let date = formatter.date(from: text) else {
                throw DecodingError.dataCorruptedError(in: container, debugDescription: "Invalid ISO 8601 timestamp")
            }
            return date
        }
        return try decoder.decode(Self.self, from: data)
    }
}
