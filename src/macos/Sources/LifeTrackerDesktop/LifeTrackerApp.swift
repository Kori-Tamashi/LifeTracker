import Foundation
import LifeTrackerClient
import SwiftUI

@main
struct LifeTrackerApp: App {
    private let client: LocalAPIClient?

    init() {
        let environment = ProcessInfo.processInfo.environment
        let root = environment["LIFETRACKER_RUNTIME_ROOT"]
        let credential = root.map { URL(fileURLWithPath: $0).appendingPathComponent("owner.credential") }
        let token = credential.flatMap { try? String(contentsOf: $0, encoding: .utf8) }
        let port = environment["LIFETRACKER_PORT"] ?? "17841"
        if let token, let number = Int(port), (1...65535).contains(number),
            let url = URL(string: "http://127.0.0.1:\(number)/api/v1")
        {
            client = try? LocalAPIClient(
                baseURL: url, ownerToken: token.trimmingCharacters(in: .whitespacesAndNewlines))
        } else {
            client = nil
        }
    }

    var body: some Scene {
        WindowGroup("LifeTracker") {
            if let client {
                DashboardView(loader: client)
            } else {
                ContentUnavailableView(
                    "Нет подключения к локальному сервису",
                    systemImage: "desktopcomputer.trianglebadge.exclamationmark",
                    description: Text("Запустите сервис и приложение с одним LIFETRACKER_RUNTIME_ROOT.")
                )
                .frame(minWidth: 700, minHeight: 540)
            }
        }
        .defaultSize(width: 980, height: 720)
    }
}
