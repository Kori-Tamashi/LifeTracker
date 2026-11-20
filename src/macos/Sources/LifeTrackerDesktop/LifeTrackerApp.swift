import AppKit
import Foundation
import LifeTrackerClient
import Observation
import SwiftUI

@MainActor @Observable
private final class DesktopConnection {
    var client: LocalAPIClient?
    var workspace: AssessmentWorkspace?
    var error: String?

    init() {
        let environment = ProcessInfo.processInfo.environment
        if let root = environment["LIFETRACKER_RUNTIME_ROOT"] {
            connect(root: URL(fileURLWithPath: root), port: Int(environment["LIFETRACKER_PORT"] ?? "17841") ?? 0)
        }
    }

    func connect(root: URL, port: Int) {
        do {
            guard (1...65535).contains(port), let url = URL(string: "http://127.0.0.1:\(port)/api/v1") else {
                throw LocalAPIError.invalidLocalEndpoint
            }
            let token = try String(contentsOf: root.appendingPathComponent("owner.credential"), encoding: .utf8)
            let configured = try LocalAPIClient(
                baseURL: url, ownerToken: token.trimmingCharacters(in: .whitespacesAndNewlines))
            let journal = try FilePendingAssessmentJournal(runtimeRoot: root)
            workspace = try AssessmentWorkspace(service: configured, journal: journal)
            client = configured
            error = nil
        } catch {
            self.error =
                "Не удалось открыть профиль. Выберите папку данных работающего сервиса, проверьте доступ к файлам и закройте другое окно LifeTracker."
        }
    }
}

@main
struct LifeTrackerApp: App {
    @State private var connection = DesktopConnection()

    var body: some Scene {
        Window("LifeTracker", id: "main") {
            if let client = connection.client, let workspace = connection.workspace {
                DashboardView(client: client, workspace: workspace)
            } else {
                ConnectionView(connection: connection)
            }
        }
        .defaultSize(width: 980, height: 720)
    }
}

private struct ConnectionView: View {
    let connection: DesktopConnection
    @State private var port = "17841"

    var body: some View {
        VStack(spacing: 20) {
            Image(systemName: "person.crop.square").font(.system(size: 64)).foregroundStyle(.teal)
            Text("Добро пожаловать в LifeTracker").font(.largeTitle.bold())
            Text("Запустите локальный сервис и выберите его папку данных, чтобы открыть своего персонажа.")
                .multilineTextAlignment(.center).foregroundStyle(.secondary)
            HStack {
                Text("Порт сервиса")
                TextField("17841", text: $port).frame(width: 100)
            }
            Button("Выбрать папку профиля…") {
                let panel = NSOpenPanel()
                panel.canChooseFiles = false
                panel.canChooseDirectories = true
                panel.allowsMultipleSelection = false
                panel.prompt = "Открыть профиль"
                if panel.runModal() == .OK, let root = panel.url {
                    connection.connect(root: root, port: Int(port) ?? 0)
                }
            }
            .buttonStyle(.borderedProminent)
            if let error = connection.error { Text(error).foregroundStyle(.orange).multilineTextAlignment(.center) }
        }
        .padding(40).frame(minWidth: 760, minHeight: 540)
    }
}
