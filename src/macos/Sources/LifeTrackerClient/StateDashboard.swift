import Foundation
import Observation

public protocol CurrentStateLoading: Sendable {
    func currentState() async throws -> CurrentState
}

extension LocalAPIClient: CurrentStateLoading {}

/// Presentation state only. Balance and freshness policy remain in the server.
@MainActor @Observable
public final class StateDashboard {
    public private(set) var state: CurrentState?
    public private(set) var isLoading = false
    public private(set) var errorMessage: String?
    private let loader: any CurrentStateLoading

    public init(loader: any CurrentStateLoading) {
        self.loader = loader
    }

    public func refresh() async {
        guard !isLoading else { return }
        isLoading = true
        errorMessage = nil
        defer { isLoading = false }
        do {
            let next = try await loader.currentState()
            try Task.checkCancellation()
            state = next
        } catch is CancellationError {
            // Keep the last snapshot; cancellation is not a connection failure.
        } catch {
            errorMessage = "Не удалось обновить состояние. Проверьте, что локальный сервис запущен."
        }
    }

    public func snapshotIsCurrent(at date: Date) -> Bool {
        guard errorMessage == nil, let deadline = state?.validUntil else { return false }
        return date < deadline
    }
}

extension StateMetric {
    public var title: String {
        switch self {
        case .energy: "Энергия"
        case .mood: "Настроение"
        case .anxiety: "Тревога"
        case .physicalWellbeing: "Физическое самочувствие"
        }
    }
}

extension AssessmentSource {
    public var title: String {
        switch self {
        case .selfReport: "Самооценка"
        case .aiEstimate: "Предположение ИИ"
        case .unknown: "Нет данных"
        }
    }
}

extension BalanceLevel {
    public var title: String {
        switch self {
        case .undefined: "Недостаточно данных"
        case .attention: "Требует внимания"
        case .satisfactory: "Удовлетворительно"
        case .good: "Хорошо"
        case .excellent: "Отлично"
        }
    }
}
