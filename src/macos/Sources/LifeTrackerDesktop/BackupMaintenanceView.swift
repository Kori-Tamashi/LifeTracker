import AppKit
import LifeTrackerClient
import SwiftUI

struct BackupMaintenanceView: View {
    let desktop: DesktopSession
    @Environment(\.dismiss) private var dismiss
    @State private var message: String?
    @State private var confirmingStop = false

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("Резервные копии").font(.title.bold())
            Text("Копия содержит личные записи и ключ доступа. Храните её локально вне папки профиля.")
                .foregroundStyle(.secondary)
            if !desktop.canMaintain && !desktop.maintenanceBusy {
                Text("Сначала остановите подключение. Несохранённую форму закройте или сохраните до остановки.")
                if !desktop.runtime.ownsHost {
                    Text("Внешний Core нужно остановить отдельно: приложение его не завершает.")
                        .foregroundStyle(.orange)
                }
                Button("Остановить подключение…") { confirmingStop = true }
                    .disabled(desktop.workspace?.saving == true || desktop.runtime.phase == .stopping)
            }
            Divider()
            Text("Автокопия при запуске после 09:00 (Москва)").font(.headline)
            Text(
                "Семь суточных копий; до 2 ГБ на профиль, резерв места 256 МиБ. Каталог выбирайте локально в var/backups проекта, отдельно от профиля."
            )
            .font(.callout).foregroundStyle(.secondary)
            if let root = desktop.automaticBackupRoot {
                Text(root.path).font(.caption).textSelection(.enabled)
                Button("Отключить автокопии") { desktop.configureAutomaticBackup(nil) }
                    .disabled(desktop.maintenanceBusy)
            } else {
                Button("Выбрать каталог и включить…") {
                    guard let root = chooseDirectory(prompt: "Каталог автокопий") else { return }
                    desktop.configureAutomaticBackup(root)
                    message =
                        "Автокопии включены со следующего запуска профиля. Непрерывная работа через границу суток пока не покрыта."
                }
                .disabled(desktop.maintenanceBusy)
            }
            Divider()
            Group {
                Button("Создать копию…") { createBackup() }
                Button("Проверить копию…") {
                    guard let backup = chooseDirectory(prompt: "Выбрать резервную копию") else { return }
                    perform(.verify(backup: backup), success: "Копия проверена.")
                }
                Button("Восстановить в новую папку…") { restoreBackup() }
            }
            .disabled(!desktop.canMaintain)
            Text("Существующие папки не заменяются. Восстановление не переключает текущий профиль автоматически.")
                .font(.callout).foregroundStyle(.secondary)
            if desktop.maintenanceBusy {
                ProgressView("Обработка копии… Дождитесь завершения перед выходом.")
            }
            if let message { Text(message).textSelection(.enabled) }
            HStack {
                Spacer()
                Button("Закрыть") { dismiss() }.keyboardShortcut(.cancelAction)
                    .disabled(desktop.maintenanceBusy)
            }
        }
        .padding(28).frame(width: 570)
        .interactiveDismissDisabled(desktop.maintenanceBusy)
        .confirmationDialog("Остановить подключение?", isPresented: $confirmingStop) {
            Button("Остановить", role: .destructive) {
                Task { await desktop.stopForMaintenance() }
            }
        } message: {
            Text("Несохранённая форма будет закрыта. Уже отправленная pending-команда останется в профиле.")
        }
    }

    private func chooseDirectory(prompt: String) -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = false
        panel.prompt = prompt
        return panel.runModal() == .OK ? panel.url : nil
    }

    private func newDestination(prompt: String, suggestedName: String) -> URL? {
        guard let parent = chooseDirectory(prompt: prompt) else { return nil }
        let alert = NSAlert()
        alert.messageText = "Имя новой папки"
        alert.informativeText = "Папка должна отсутствовать. Разрешено только имя, без пути."
        let field = NSTextField(string: suggestedName)
        field.frame = NSRect(x: 0, y: 0, width: 360, height: 24)
        alert.accessoryView = field
        alert.addButton(withTitle: "Продолжить")
        alert.addButton(withTitle: "Отмена")
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        let name = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name != ".", name != "..", !name.contains("/"), !name.contains(":"),
            !name.contains("\0")
        else {
            message = "Укажите простое имя новой папки без разделителей пути."
            return nil
        }
        return parent.appendingPathComponent(name, isDirectory: true)
    }

    private func createBackup() {
        guard let source = chooseDirectory(prompt: "Выбрать исходный профиль"),
            let destination = newDestination(prompt: "Где хранить копию", suggestedName: "LifeTracker-backup")
        else { return }
        perform(.backup(source: source, destination: destination), success: "Копия создана: \(destination.path)")
    }

    private func restoreBackup() {
        guard let backup = chooseDirectory(prompt: "Выбрать резервную копию"),
            let destination = newDestination(prompt: "Где восстановить профиль", suggestedName: "LifeTracker-restored")
        else { return }
        perform(
            .restore(backup: backup, destination: destination),
            success:
                "Профиль восстановлен: \(destination.path). Закройте это окно и выберите новую папку при запуске сервиса."
        )
    }

    private func perform(_ operation: ProfileMaintenanceOperation, success: String) {
        message = nil
        Task {
            let completed = await desktop.maintain(operation)
            message =
                completed
                ? success
                : "Операция не выполнена. Остановите все Core и клиенты этого профиля; проверьте копию, доступ, свободное место и отсутствие папки назначения."
        }
    }
}
