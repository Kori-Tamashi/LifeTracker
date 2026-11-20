import LifeTrackerClient
import SwiftUI

struct AssessmentEditor: View {
    let workspace: AssessmentWorkspace
    let record: AssessmentRecord?
    @Environment(\.dismiss) private var dismiss
    @State private var enabled: Set<StateMetric> = []
    @State private var scores: [StateMetric: Double] = [:]
    @State private var observedAt = Date()
    @State private var note = ""
    @State private var validationError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(record == nil ? "Как вы себя чувствуете?" : "Исправление оценки").font(.title2.bold())
            DatePicker("Время состояния", selection: $observedAt, in: ...Date())
                .disabled(workspace.pending != nil || workspace.saving)
            if let record {
                Text("\(record.metric.title) · \(record.sourceKind.title) · версия \(record.revision)")
                scoreControl(record.metric)
            } else {
                ForEach(StateMetric.allCases, id: \.self) { metric in
                    HStack {
                        Toggle(
                            metric.title,
                            isOn: Binding(
                                get: { enabled.contains(metric) },
                                set: { if $0 { enabled.insert(metric) } else { enabled.remove(metric) } }
                            )
                        ).frame(width: 220, alignment: .leading)
                            .disabled(workspace.pending != nil || workspace.saving)
                        if enabled.contains(metric) { scoreControl(metric) }
                    }
                }
                Text("Отметьте только те показатели, которые хотите записать. Для тревоги меньше — лучше.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            TextField(
                record == nil ? "Комментарий (необязательно)" : "Причина исправления (обязательно)", text: $note,
                axis: .vertical
            )
            .lineLimit(2...4)
            .disabled(workspace.pending != nil || workspace.saving)
            if let error = validationError ?? workspace.saveError {
                Text(error).foregroundStyle(.orange).font(.callout)
            }
            HStack {
                Button("Закрыть") { dismiss() }.keyboardShortcut(.cancelAction)
                Spacer()
                if workspace.pending != nil {
                    Button("Повторить отправку") { Task { await workspace.retrySave() } }
                } else {
                    Button("Сохранить") { save() }.keyboardShortcut(.defaultAction)
                }
            }
            .disabled(workspace.saving || workspace.backupPaused)
        }
        .padding(28).frame(width: 620)
        .onAppear {
            if let record {
                observedAt = record.observedAt
                scores[record.metric] = record.score
            }
        }
        .onChange(of: workspace.savedCount) { _, _ in dismiss() }
    }

    private func scoreControl(_ metric: StateMetric) -> some View {
        Stepper(value: Binding(get: { scores[metric] ?? 5 }, set: { scores[metric] = $0 }), in: 0...10, step: 0.5) {
            Text((scores[metric] ?? 5).formatted(.number.precision(.fractionLength(0...1))) + " / 10")
                .monospacedDigit().frame(minWidth: 90)
        }
        .accessibilityLabel(metric.title)
        .disabled(workspace.pending != nil || workspace.saving)
    }

    private func save() {
        do {
            let command: AssessmentCommand
            if let record {
                command = try .revise(
                    record, observedAt: observedAt, score: scores[record.metric] ?? record.score, reason: note)
            } else {
                command = try .record(
                    observedAt: observedAt,
                    values: StateMetric.allCases.filter { enabled.contains($0) }.map {
                        AssessmentValue(metric: $0, score: scores[$0] ?? 5)
                    },
                    comment: note)
            }
            validationError = nil
            Task { await workspace.save(command) }
        } catch {
            validationError =
                "Выберите хотя бы один показатель. Причина исправления обязательна (до 1000 символов), комментарий — до 2000."
        }
    }
}
