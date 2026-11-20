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
    private var refreshRequested = false
    private let loader: any CurrentStateLoading

    public init(loader: any CurrentStateLoading) {
        self.loader = loader
    }

    public func refresh() async {
        if isLoading {
            refreshRequested = true
            return
        }
        isLoading = true
        defer { isLoading = false }
        repeat {
            refreshRequested = false
            errorMessage = nil
            do {
                let next = try await loader.currentState()
                try Task.checkCancellation()
                state = next
            } catch is CancellationError {
                return
            } catch {
                if Task.isCancelled { return }
                errorMessage = "Не удалось обновить состояние. Проверьте, что локальный сервис запущен."
            }
        } while refreshRequested && !Task.isCancelled
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
