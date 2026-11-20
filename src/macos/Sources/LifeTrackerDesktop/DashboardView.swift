import LifeTrackerClient
import SwiftUI

struct DashboardView: View {
    @State private var dashboard: StateDashboard
    @State private var selected: StateMetric?
    @Environment(\.scenePhase) private var scenePhase

    init(loader: any CurrentStateLoading) {
        _dashboard = State(initialValue: StateDashboard(loader: loader))
    }

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { timeline in
            let current = dashboard.snapshotIsCurrent(at: timeline.date)
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    HStack {
                        VStack(alignment: .leading) {
                            Text("Ваш персонаж").font(.largeTitle.bold())
                            Text("Сводка самооценок и предположений ИИ").foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("Обновить", systemImage: "arrow.clockwise") {
                            Task { await dashboard.refresh() }
                        }
                        .disabled(dashboard.isLoading)
                    }
                    if let error = dashboard.errorMessage {
                        Label(error, systemImage: "wifi.exclamationmark").foregroundStyle(.orange)
                    }
                    HStack(spacing: 36) {
                        CharacterView().frame(width: 180, height: 240)
                        VStack(alignment: .leading, spacing: 12) {
                            Text("Баланс").font(.headline)
                            Text(balanceText(current: current)).font(.system(size: 42, weight: .bold, design: .rounded))
                            Text(
                                current
                                    ? (dashboard.state?.level.title ?? "Недостаточно данных") : "Нет актуальной сводки")
                            Text("Это сводка состояния, а не медицинская оценка.")
                                .font(.caption).foregroundStyle(.secondary)
                            if let state = dashboard.state {
                                Text("Получено: \(state.computedAt.formatted(date: .abbreviated, time: .standard))")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        Spacer()
                    }
                    if dashboard.isLoading { ProgressView("Обновление…") }
                    if let state = dashboard.state {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 250))], spacing: 16) {
                            ForEach(state.metrics, id: \.metric) { metric in
                                Button {
                                    selected = metric.metric
                                } label: {
                                    metricCard(metric, at: timeline.date)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    } else if !dashboard.isLoading {
                        Text("Состояние ещё не загружено. Нажмите «Обновить».").foregroundStyle(.secondary)
                    }
                }
                .padding(32)
            }
            .sheet(item: $selected) { metric in
                if let value = dashboard.state?.metrics.first(where: { $0.metric == metric }) {
                    VStack(alignment: .leading, spacing: 16) {
                        metricCard(value, at: timeline.date)
                        Text(metric == .anxiety ? "Для тревоги меньше — лучше." : "Больше — лучше.")
                        Button("Закрыть") { selected = nil }.keyboardShortcut(.cancelAction)
                    }.padding(32).frame(width: 420)
                }
            }
        }
        .frame(minWidth: 700, minHeight: 540)
        .task {
            while !Task.isCancelled {
                await dashboard.refresh()
                do { try await Task.sleep(for: .seconds(60)) } catch { return }
            }
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { Task { await dashboard.refresh() } }
        }
    }

    private func balanceText(current: Bool) -> String {
        guard current, let balance = dashboard.state?.balance else { return "—" }
        return "\(balance.formatted(.number.precision(.fractionLength(0...1)))) / 10"
    }

    private func metricCard(_ metric: SelectedMetric, at date: Date) -> some View {
        let expired = dashboard.state?.validUntil.map { date >= $0 } ?? false
        return VStack(alignment: .leading, spacing: 10) {
            Text(metric.metric.title).font(.headline)
            Text(metric.score.map { $0.formatted(.number.precision(.fractionLength(0...1))) + " / 10" } ?? "Нет данных")
                .font(.title2.monospacedDigit())
            Text(metric.sourceKind.title).font(.caption)
            if metric.score != nil, !metric.fresh {
                Label("Последнее известное значение", systemImage: "clock").font(.caption).foregroundStyle(.orange)
            }
            if expired, metric.fresh {
                Text("Сводка требует обновления").font(.caption).foregroundStyle(.orange)
            }
            if let date = metric.observedAt {
                Text(date.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(20)
        .background(.quaternary.opacity(0.4), in: RoundedRectangle(cornerRadius: 16))
        .accessibilityElement(children: .combine)
    }
}

extension StateMetric: Identifiable {
    public var id: String { rawValue }
}

/// Neutral vector silhouette until art/animation is qualified; never invents health effects.
private struct CharacterView: View {
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 28).fill(.teal.opacity(0.08))
            VStack(spacing: 5) {
                Circle().fill(.teal.gradient).frame(width: 48, height: 48)
                HStack(spacing: 6) {
                    Capsule().fill(.teal.opacity(0.7)).frame(width: 16, height: 72).rotationEffect(.degrees(10))
                    RoundedRectangle(cornerRadius: 12).fill(.teal.gradient).frame(width: 58, height: 86)
                    Capsule().fill(.teal.opacity(0.7)).frame(width: 16, height: 72).rotationEffect(.degrees(-10))
                }
                HStack(spacing: 10) {
                    Capsule().fill(.teal.opacity(0.8)).frame(width: 23, height: 64)
                    Capsule().fill(.teal.opacity(0.8)).frame(width: 23, height: 64)
                }
            }
        }
        .accessibilityLabel("Нейтральный силуэт персонажа")
    }
}
