import Foundation

public enum LocalAPIError: Error, Sendable {
    case invalidLocalEndpoint
    case missingCredential
    case invalidResponse
    case httpStatus(Int)
}

public struct LocalAPIClient: Sendable {
    private let baseURL: URL
    private let ownerToken: String
    private let session: URLSession

    public init(baseURL: URL, ownerToken: String, session: URLSession = .shared) throws {
        guard baseURL.scheme == "http", baseURL.host == "127.0.0.1",
            baseURL.user == nil, baseURL.password == nil,
            baseURL.query == nil, baseURL.fragment == nil
        else {
            throw LocalAPIError.invalidLocalEndpoint
        }
        guard !ownerToken.isEmpty, !ownerToken.contains(where: { $0.isWhitespace || $0.isNewline }) else {
            throw LocalAPIError.missingCredential
        }
        self.baseURL = baseURL
        self.ownerToken = ownerToken
        self.session = session
    }

    // Internal so transport contract can be verified without a running server.
    func currentStateRequest() -> URLRequest {
        var request = URLRequest(url: baseURL.appendingPathComponent("state/current"))
        request.httpMethod = "GET"
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("Bearer \(ownerToken)", forHTTPHeaderField: "Authorization")
        return request
    }

    public func currentState() async throws -> CurrentState {
        let (data, response) = try await session.data(for: currentStateRequest())
        guard let response = response as? HTTPURLResponse else { throw LocalAPIError.invalidResponse }
        guard response.statusCode == 200 else { throw LocalAPIError.httpStatus(response.statusCode) }
        return try CurrentState.decode(data)
    }
}
