import LifeTrackerClient
import SwiftUI

struct AssessmentHistory: View {
    let workspace: AssessmentWorkspace
    @State private var editing: AssessmentRecord?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Text("История оценок").font(.title2.bold())
                Spacer()
                Button("Обновить историю") { Task { await workspace.loadHistory() } }
                    .disabled(workspace.historyLoading)
            }
            if let error = workspace.historyError { Text(error).foregroundStyle(.orange) }
            if workspace.records.isEmpty, !workspace.historyLoading {
                Text("Записей пока нет.").foregroundStyle(.secondary)
            }
            ForEach(workspace.records) { record in
                HStack {
                    VStack(alignment: .leading, spacing: 5) {
                        Text("\(record.metric.title): \(record.score.formatted()) / 10").font(.headline)
                        Text(
                            "\(record.sourceKind.title) · \(record.observedAt.formatted(date: .abbreviated, time: .shortened)) · версия \(record.revision)"
                        )
                        .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Исправить") { editing = record }
                        .disabled(workspace.saving || workspace.pending != nil)
                }
                Divider()
            }
            if workspace.historyLoading { ProgressView("Загрузка истории…") }
            if workspace.nextCursor != nil {
                Button("Загрузить ещё") { Task { await workspace.loadHistory(reset: false) } }
                    .disabled(workspace.historyLoading)
            }
        }
        .task { await workspace.loadHistory() }
        .sheet(item: $editing) { record in AssessmentEditor(workspace: workspace, record: record) }
    }
}
