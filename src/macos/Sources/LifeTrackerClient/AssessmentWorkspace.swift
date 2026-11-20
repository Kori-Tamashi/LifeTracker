import Foundation
import Observation

@MainActor @Observable
public final class AssessmentWorkspace {
    public private(set) var records: [AssessmentRecord] = []
    public private(set) var nextCursor: String?
    public private(set) var historyLoading = false
    public private(set) var historyError: String?
    public private(set) var saving = false
    public private(set) var saveError: String?
    public private(set) var pending: AssessmentCommand?
    public private(set) var savedCount = 0
    private var reloadRequested = false
    private let journal: any PendingAssessmentJournal
    private let service: any AssessmentServicing

    public init(service: any AssessmentServicing, journal: any PendingAssessmentJournal) throws {
        self.service = service
        self.journal = journal
        pending = try journal.load()
    }

    public func loadHistory(reset: Bool = true) async {
        if historyLoading {
            if reset { reloadRequested = true }
            return
        }
        guard reset || nextCursor != nil else { return }
        historyLoading = true
        historyError = nil
        defer {
            historyLoading = false
            if reloadRequested {
                reloadRequested = false
                Task { await loadHistory() }
            }
        }
        do {
            let page = try await service.history(cursor: reset ? nil : nextCursor)
            if reset { records = page.items } else { records.append(contentsOf: page.items) }
            nextCursor = page.nextCursor
        } catch {
            historyError = "Не удалось загрузить историю. Повторите загрузку или обновите список с начала."
        }
    }

    public func save(_ command: AssessmentCommand) async {
        guard pending == nil, !saving else { return }
        do {
            try journal.store(command)
            pending = command
            await retrySave()
        } catch {
            saveError = "Не удалось сохранить отправку на диск. Проверьте свободное место; данные не отправлены."
        }
    }

    public func retrySave() async {
        guard let command = pending, !saving else { return }
        saving = true
        saveError = nil
        defer { saving = false }
        do {
            _ = try await service.submit(command)
            try journal.clear()
            pending = nil
            savedCount += 1
            await loadHistory()
        } catch LocalAPIError.rejected(let status, let code) where (400...499).contains(status) {
            // An explicit rejection is not an ambiguous delivery. Never overwrite a newer revision.
            do { try journal.clear() } catch {
                saveError = "Не удалось очистить локальную отправку. Повторите после проверки диска."
                return
            }
            pending = nil
            saveError =
                code == "REVISION_CONFLICT"
                ? "Запись уже изменена. Обновите историю и откройте исправление заново."
                : "Сервис отклонил запись. Проверьте значения, время и причину исправления."
        } catch {
            saveError =
                "Ответ не получен. Повторите ту же отправку: новая запись не будет создана дважды. Отправка сохранена локально и доступна после перезапуска."
        }
    }
}
