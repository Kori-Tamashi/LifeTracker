import Foundation

public struct AssessmentRecord: Decodable, Sendable, Identifiable {
    public let id: UUID
    public let revision: Int
    public let metric: StateMetric
    public let score: Double
    public let observedAt: Date
    public let sourceKind: AssessmentSource
    public let status: String
}

public struct AssessmentPage: Decodable, Sendable {
    public let items: [AssessmentRecord]
    public let nextCursor: String?
}

public struct AssessmentValue: Encodable, Sendable {
    public let metric: StateMetric
    public let score: Double

    public init(metric: StateMetric, score: Double) {
        self.metric = metric
        self.score = score
    }
}

public struct AssessmentCommand: Codable, Sendable {
    public let id: UUID
    let path: String
    let method: String
    let body: Data

    public static func record(observedAt: Date, values: [AssessmentValue], comment: String) throws -> Self {
        guard (1...4).contains(values.count), Set(values.map(\.metric)).count == values.count,
            values.allSatisfy({ validScore($0.score) }), comment.utf16.count <= 2000
        else { throw LocalAPIError.invalidAssessment }
        struct Body: Encodable {
            let observedAt: Date
            let values: [AssessmentValue]
            let comment: String
        }
        return try Self(
            path: "assessments", method: "POST", payload: Body(observedAt: observedAt, values: values, comment: comment)
        )
    }

    public static func revise(_ record: AssessmentRecord, observedAt: Date, score: Double, reason: String) throws
        -> Self
    {
        guard record.revision > 0, validScore(score), !reason.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
            reason.utf16.count <= 1000
        else { throw LocalAPIError.invalidAssessment }
        struct Body: Encodable {
            let expectedRevision: Int
            let observedAt: Date
            let score: Double
            let reason: String
        }
        return try Self(
            path: "assessments/\(record.id.uuidString)", method: "PATCH",
            payload: Body(expectedRevision: record.revision, observedAt: observedAt, score: score, reason: reason))
    }

    private init<T: Encodable>(path: String, method: String, payload: T) throws {
        let encoder = JSONEncoder()
        encoder.keyEncodingStrategy = .convertToSnakeCase
        encoder.dateEncodingStrategy = .iso8601
        body = try encoder.encode(payload)
        self.path = path
        self.method = method
        id = UUID()
    }

    private static func validScore(_ value: Double) -> Bool {
        value.isFinite && (0...10).contains(value) && (value * 2).rounded() == value * 2
    }
}

public struct AssessmentReceipt: Decodable, Sendable {
    public let commandId: UUID
    public let status: String
}

public protocol AssessmentServicing: Sendable {
    func history(cursor: String?) async throws -> AssessmentPage
    func submit(_ command: AssessmentCommand) async throws -> AssessmentReceipt
}

extension LocalAPIClient: AssessmentServicing {
    func mutationRequest(_ command: AssessmentCommand) -> URLRequest {
        var request = authorizedRequest(path: command.path)
        request.httpMethod = command.method
        request.httpBody = command.body
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue(command.id.uuidString, forHTTPHeaderField: "Idempotency-Key")
        request.setValue(command.id.uuidString, forHTTPHeaderField: "X-Command-ID")
        return request
    }

    public func submit(_ command: AssessmentCommand) async throws -> AssessmentReceipt {
        let data = try await send(mutationRequest(command))
        let receipt = try StateJSON.decoder().decode(AssessmentReceipt.self, from: data)
        guard receipt.commandId == command.id, receipt.status == "completed" else {
            throw LocalAPIError.invalidResponse
        }
        return receipt
    }

    func historyRequest(cursor: String?) -> URLRequest {
        var request = authorizedRequest(path: "assessments")
        var components = URLComponents(url: request.url!, resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "limit", value: "50")]
        if let cursor { components.queryItems!.append(URLQueryItem(name: "cursor", value: cursor)) }
        request.url = components.url
        return request
    }

    public func history(cursor: String? = nil) async throws -> AssessmentPage {
        try StateJSON.decoder().decode(AssessmentPage.self, from: await send(historyRequest(cursor: cursor)))
    }
}
