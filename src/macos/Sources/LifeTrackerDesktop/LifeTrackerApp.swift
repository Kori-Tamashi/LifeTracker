import AppKit
import Foundation
import LifeTrackerClient
import Observation
import ServiceManagement
import SwiftUI

@MainActor @Observable
final class DesktopSession {
    let runtime = NativeRuntime()
    var workspace: AssessmentWorkspace?
    var maintenanceBusy = false
    var refreshToken = UUID()
    var error: String?
    var loginStatus = SMAppService.mainApp.status
    private var restored = false
    private var rootKey: String {
        Bundle.main.infoDictionary?["LifeTrackerReleaseChannel"] as? String == "beta"
            ? "betaRuntimeRoot" : "runtimeRoot"
    }
    private var portKey: String {
        Bundle.main.infoDictionary?["LifeTrackerReleaseChannel"] as? String == "beta"
            ? "betaRuntimePort" : "runtimePort"
    }

    func restore() async {
        guard !restored else { return }
        restored = true
        let environment = ProcessInfo.processInfo.environment
        if let root = environment["LIFETRACKER_RUNTIME_ROOT"] ?? UserDefaults.standard.string(forKey: rootKey) {
            let port =
                environment["LIFETRACKER_PORT"].flatMap(Int.init)
                ?? (UserDefaults.standard.object(forKey: portKey) as? Int) ?? 17841
            await start(root: URL(fileURLWithPath: root), port: port)
        }
    }

    func start(root: URL, port: Int) async {
        guard !maintenanceBusy, runtime.phase == .stopped || runtime.phase == .failed else { return }
        error = nil
        workspace = nil
        let command = coreCommand
        await runtime.start(
            NativeHostConfiguration(
                executable: command.executable, arguments: command.hostArguments, root: root, port: port))
        guard runtime.phase == .ready, let client = runtime.client else { return }
        do {
            workspace = try AssessmentWorkspace(
                service: client, journal: FilePendingAssessmentJournal(runtimeRoot: root))
            UserDefaults.standard.set(root.path, forKey: rootKey)
            UserDefaults.standard.set(port, forKey: portKey)
        } catch {
            _ = await runtime.stop()
            self.error =
                "Не удалось открыть профиль. Проверьте доступ к папке данных и закройте другой клиент LifeTracker."
        }
    }

    private var coreCommand: OfflineProfileMaintenance {
        let environment = ProcessInfo.processInfo.environment
        let candidates = [
            environment["LIFETRACKER_DOTNET"], "/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet",
            FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".dotnet/dotnet").path,
        ].compactMap { $0 }
        let dotnet = candidates.first { FileManager.default.isExecutableFile(atPath: $0) } ?? candidates[0]
        let host =
            environment["LIFETRACKER_HOST_DLL"].map { URL(fileURLWithPath: $0) }
            ?? (Bundle.main.resourceURL ?? Bundle.main.bundleURL).appendingPathComponent("Core/LifeTracker.Host.dll")
        return OfflineProfileMaintenance(executable: URL(fileURLWithPath: dotnet), hostArguments: [host.path])
    }

    var canMaintain: Bool {
        !maintenanceBusy && !runtime.ownsHost && workspace == nil
            && (runtime.phase == .stopped || runtime.phase == .failed)
    }

    func stopForMaintenance() async {
        guard !maintenanceBusy, workspace?.saving != true else { return }
        if await runtime.stop() { workspace = nil }
    }

    func maintain(_ operation: ProfileMaintenanceOperation) async -> Bool {
        guard canMaintain else { return false }
        maintenanceBusy = true
        defer { maintenanceBusy = false }
        do {
            try await coreCommand.run(operation)
            return true
        } catch { return false }
    }

    func reconcileAfterWake() async {
        guard runtime.phase == .ready || runtime.phase == .degraded else { return }
        let connected = await runtime.reconcile()
        guard runtime.phase == .ready || runtime.phase == .degraded else { return }
        refreshToken = UUID()
        if connected { await workspace?.loadHistory() }
    }

    func toggleLogin() async {
        do {
            if SMAppService.mainApp.status == .enabled {
                try await SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
            error = nil
        } catch {
            self.error = "Не удалось изменить автозапуск. Проверьте разрешение LifeTracker в настройках входа macOS."
        }
        loginStatus = SMAppService.mainApp.status
    }
}

@MainActor
private final class DesktopDelegate: NSObject, NSApplicationDelegate {
    var session: DesktopSession?
    private var quitting = false
    private var wakeObserver: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in await self?.session?.reconcileAfterWake() }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        if let wakeObserver { NSWorkspace.shared.notificationCenter.removeObserver(wakeObserver) }
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        session?.loginStatus = SMAppService.mainApp.status
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard let session else { return .terminateNow }
        guard !session.maintenanceBusy else { return .terminateCancel }
        guard !quitting else { return .terminateLater }
        quitting = true
        Task {
            let stopped = await session.runtime.stop()
            if stopped { session.workspace = nil } else { quitting = false }
            sender.reply(toApplicationShouldTerminate: stopped)
        }
        return .terminateLater
    }
}

@main
struct LifeTrackerApp: App {
    @NSApplicationDelegateAdaptor(DesktopDelegate.self) private var delegate
    @State private var desktop = DesktopSession()
    @State private var showingBackups = false
    @Environment(\.openWindow) private var openWindow

    var body: some Scene {
        Window("LifeTracker", id: "main") {
            Group {
                if let client = desktop.runtime.client, let workspace = desktop.workspace {
                    VStack(spacing: 0) {
                        if desktop.runtime.phase == .recovering {
                            ProgressView("Восстановление подключения…").padding(12)
                        }
                        if let error = desktop.runtime.errorMessage {
                            HStack {
                                Text(error).foregroundStyle(.orange)
                                Button("Проверить подключение") { Task { await desktop.reconcileAfterWake() } }
                                    .disabled(desktop.runtime.phase == .recovering)
                            }.padding(12)
                        }
                        DashboardView(
                            client: client, workspace: workspace, refreshToken: desktop.refreshToken,
                            connectionAvailable: desktop.runtime.phase == .ready)
                    }
                } else {
                    ConnectionView(desktop: desktop)
                }
            }
            .sheet(isPresented: $showingBackups) {
                BackupMaintenanceView(desktop: desktop)
            }
            .onAppear {
                delegate.session = desktop
                Task { await desktop.restore() }
            }
            .onChange(of: desktop.runtime.phase) { _, phase in
                if phase == .failed { desktop.workspace = nil }
            }
        }
        .defaultSize(width: 980, height: 720)
        .commands {
            CommandMenu("Профиль") {
                Button("Резервные копии…") { showingBackups = true }
                    .disabled(desktop.maintenanceBusy)
            }
            CommandGroup(replacing: .appTermination) {
                Button(desktop.runtime.ownsHost ? "Полностью остановить LifeTracker" : "Завершить LifeTracker") {
                    NSApp.terminate(nil)
                }
                .keyboardShortcut("q")
            }
        }
        MenuBarExtra("LifeTracker", systemImage: "person.crop.circle") {
            Text("LifeTracker " + (Bundle.main.infoDictionary?["LifeTrackerReleaseVersion"] as? String ?? "dev"))
            Text(desktop.runtime.phase.title)
            if desktop.runtime.phase == .ready, !desktop.runtime.ownsHost {
                Text("Подключён внешний сервис; он управляется отдельно")
            }
            if desktop.runtime.phase == .degraded {
                Button("Проверить подключение") { Task { await desktop.reconcileAfterWake() } }
            }
            Button("Открыть LifeTracker") {
                openWindow(id: "main")
                NSApp.activate(ignoringOtherApps: true)
            }
            Button("Резервные копии…") {
                openWindow(id: "main")
                NSApp.activate(ignoringOtherApps: true)
                showingBackups = true
            }
            .disabled(desktop.maintenanceBusy)
            Divider()
            Button(desktop.loginStatus == .enabled ? "Отключить запуск при входе" : "Запускать при входе") {
                Task { await desktop.toggleLogin() }
            }
            if desktop.loginStatus == .requiresApproval { Text("Автозапуск ожидает разрешения macOS") }
            if let error = desktop.error { Text(error) }
            Divider()
            Button(desktop.runtime.ownsHost ? "Полностью остановить LifeTracker" : "Завершить LifeTracker") {
                NSApp.terminate(nil)
            }
        }
    }
}

private struct ConnectionView: View {
    let desktop: DesktopSession
    @State private var port = "17841"

    var body: some View {
        VStack(spacing: 20) {
            Image(systemName: "person.crop.square").font(.system(size: 64)).foregroundStyle(.teal)
            Text("LifeTracker").font(.largeTitle.bold())
            Text(Bundle.main.infoDictionary?["LifeTrackerReleaseVersion"] as? String ?? "Development build").font(
                .headline)
            Text(
                "Ранняя бета: используйте отдельную папку для тестирования. Резервное копирование ещё не автоматизировано."
            )
            .font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
            Text("Выберите папку данных. Приложение запустит локальный сервис или подключится к уже работающему.")
                .multilineTextAlignment(.center).foregroundStyle(.secondary)
            Text("Для запуска сервиса нужен установленный .NET 10.").font(.caption).foregroundStyle(.secondary)
            HStack {
                Text("Порт сервиса")
                TextField("17841", text: $port).frame(width: 100)
            }
            Button("Выбрать папку и запустить…") {
                let panel = NSOpenPanel()
                panel.canChooseFiles = false
                panel.canChooseDirectories = true
                panel.canCreateDirectories = true
                panel.allowsMultipleSelection = false
                panel.prompt = "Выбрать папку данных"
                if panel.runModal() == .OK, let root = panel.url {
                    Task { await desktop.start(root: root, port: Int(port) ?? 0) }
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(desktop.runtime.phase == .starting || desktop.runtime.phase == .stopping)
            if desktop.runtime.phase == .starting { ProgressView("Запуск локального сервиса…") }
            if let error = desktop.error ?? desktop.runtime.errorMessage {
                Text(error).foregroundStyle(.orange).multilineTextAlignment(.center)
            }
        }
        .padding(40).frame(minWidth: 760, minHeight: 540)
    }
}

extension RuntimePhase {
    fileprivate var title: String {
        switch self {
        case .stopped: "Сервис остановлен"
        case .starting: "Сервис запускается"
        case .ready: "Сервис работает"
        case .recovering: "Восстановление подключения"
        case .degraded: "Подключение недоступно"
        case .stopping: "Сервис останавливается"
        case .failed: "Сервис недоступен"
        }
    }
}
