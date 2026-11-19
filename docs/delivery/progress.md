# Прогресс реализации

20 ноября 2026 года. Проектирование baseline1.0 зафиксировано в [PR #1](https://github.com/Kori-Tamashi/LifeTracker/pull/1), проверка Project checks прошла, PR влит в develop. Main остаётся веткой выпуска; R1 не выпущен.

## Первый срез: D-002 / D-003

- Созданы .NET solution, Kernel, Application.Abstractions и State.Domain/Contracts/Application. Версии SDK и NuGet закреплены, restore выполняется в locked mode.
- Реализованы score 0–10/0,5, актуальность 6 часов, приоритет свежей самооценки перед AI, нормализация тревоги, минимум и пороги баланса. Неизвестные данные не заменяются числами; устаревшие доступны для отображения.
- Прикладные сценарии: частичная запись 1–4 показателей, корректировка новой ревизией, текущая сводка. Порт требует атомарного сохранения receipt, дедупликации, истории и optimistic concurrency. Production-адаптера пока нет.
- Создан Swift package: типизированная сводка и клиент локального API. SwiftUI окно и устанавливаемый `.app` ещё не реализованы.
- 34 .NET-теста локально пройдены: domain, application и architecture, включая отрицательную fixture. 25 design/tooling/schema tests пройдены. 5 Swift contract tests локально пройдены. Удалённый product workflow проверяется перед слиянием feature-ветки.
- CI: product workflow собирает ядро с анализаторами и форматированием, запускает тесты и Swift package на macOS; итоговый gate требует успеха обеих jobs. Результаты конкретного SHA доступны в PR и Actions; эта запись не заменяет журнал прогона.

Это старт реализации, не завершение R1. Общая матрица 43 acceptance cases остаётся планом; runtime/integration/E2E/restore/installation gates не объявляются пройденными.

## Следующие задачи

D-004: реальный SQLite adapter и транзакционные contract tests, миграции, inbox/outbox. Затем D-005: локальный HTTP API и transport contracts; D-006: SwiftUI персонаж, история и быстрый ввод. D-001 квалифицирует источник Screen Time отдельно; отсутствие квалификации блокирует согласованный R1, но не текущую разработку. Ограничения интеграций, AI и ресурсов остаются в [qualification register](../integrations/qualification.md).
