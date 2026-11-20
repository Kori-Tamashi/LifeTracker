import Foundation

/// One attempt per Moscow day after 09:00; failures retry after 15 minutes, never catch up old days.
@MainActor
public final class DailyBackupSchedule {
    private var completedDay: Date?
    private var retryAfter: Date?
    private var calendar: Calendar {
        var value = Calendar(identifier: .gregorian)
        value.timeZone = TimeZone(identifier: "Europe/Moscow")!
        return value
    }

    public init() {}

    public func isDue(at now: Date) -> Bool {
        calendar.component(.hour, from: now) >= 9 && completedDay != calendar.startOfDay(for: now)
            && (retryAfter.map { now >= $0 } ?? true)
    }

    public func succeeded(at now: Date) {
        guard calendar.component(.hour, from: now) >= 9 else { return }
        completedDay = calendar.startOfDay(for: now)
        retryAfter = nil
    }

    public func failed(at now: Date) { retryAfter = now.addingTimeInterval(15 * 60) }
}
