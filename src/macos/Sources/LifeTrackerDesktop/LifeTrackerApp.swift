import Foundation
import LifeTrackerClient
import SwiftUI

@main
struct LifeTrackerApp: App {
    private let client: LocalAPIClient?
    private let workspace: AssessmentWorkspace?

    init() {
        let environment = ProcessInfo.processInfo.environment
        let root = environment["LIFETRACKER_RUNTIME_ROOT"]
        let credential = root.map { URL(fileURLWithPath: $0).appendingPathComponent("owner.credential") }
        let token = credential.flatMap { try? String(contentsOf: $0, encoding: .utf8) }
        let port = environment["LIFETRACKER_PORT"] ?? "17841"
        if let token, let number = Int(port), (1...65535).contains(number),
            let url = URL(string: "http://127.0.0.1:\(number)/api/v1")
        {
            let configured = try? LocalAPIClient(
                baseURL: url, ownerToken: token.trimmingCharacters(in: .whitespacesAndNewlines))
            client = configured
            if let configured, let root {
                workspace = try? AssessmentWorkspace(
                    service: configured, journal: FilePendingAssessmentJournal(runtimeRoot: URL(fileURLWithPath: root)))
            } else {
                workspace = nil
            }
        } else {
            client = nil
            workspace = nil
        }
    }

    var body: some Scene {
        Window("LifeTracker", id: "main") {
            if let client, let workspace {
                DashboardView(client: client, workspace: workspace)
            } else {
                ContentUnavailableView(
                    "Нет подключения к локальному сервису",
                    systemImage: "desktopcomputer.trianglebadge.exclamationmark",
                    description: Text(
                        "Запустите сервис и одно окно приложения с общим LIFETRACKER_RUNTIME_ROOT. Если подключение не восстановилось, проверьте доступность каталога данных."
                    )
                )
                .frame(minWidth: 700, minHeight: 540)
            }
        }
        .defaultSize(width: 980, height: 720)
    }
}
