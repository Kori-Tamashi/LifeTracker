# Прогресс реализации

20 ноября 2026 года. Проектирование baseline1.0 зафиксировано в [PR #1](https://github.com/Kori-Tamashi/LifeTracker/pull/1), проверка Project checks прошла, PR влит в develop. Main остаётся веткой выпуска; R1 не выпущен.

## Первый срез: D-002 / D-003

- Созданы .NET solution, Kernel, Application.Abstractions и State.Domain/Contracts/Application. Версии SDK и NuGet закреплены, restore выполняется в locked mode.
- Реализованы score 0–10/0,5, актуальность 6 часов, приоритет свежей самооценки перед AI, нормализация тревоги, минимум и пороги баланса. Неизвестные данные не заменяются числами; устаревшие доступны для отображения.
- Прикладные сценарии: частичная запись 1–4 показателей, корректировка новой ревизией, текущая сводка. Порт требует атомарного сохранения receipt, дедупликации, истории и optimistic concurrency. Production-адаптера пока нет.
- Создан Swift package: типизированная сводка и клиент локального API. SwiftUI окно и устанавливаемый `.app` ещё не реализованы.
- 34 .NET-теста локально пройдены: domain, application и architecture, включая отрицательную fixture. 25 design/tooling/schema tests пройдены. 5 Swift contract tests локально пройдены. В PR #2 product/project workflows прошли; PR слит в develop.
- CI: product workflow собирает ядро с анализаторами и форматированием, запускает тесты и Swift package на macOS; итоговый gate требует успеха обеих jobs. Результаты конкретного SHA доступны в PR и Actions; эта запись не заменяет журнал прогона.

Это старт реализации, не завершение R1. Общая матрица 43 acceptance cases остаётся планом; runtime/integration/E2E/restore/installation gates не объявляются пройденными.

## Следующие задачи

D-004: реальный SQLite adapter и транзакционные contract tests, миграции, inbox/outbox. Затем D-005: локальный HTTP API и transport contracts; D-006: SwiftUI персонаж, история и быстрый ввод. D-001 квалифицирует источник Screen Time отдельно; отсутствие квалификации блокирует согласованный R1, но не текущую разработку. Ограничения интеграций, AI и ресурсов остаются в [qualification register](../integrations/qualification.md).

## Управление разработкой

Подготовлен рабочий [план](development-plan.md): ближайшие действия — PM-01 (remote CI gates), ограниченный D-001 Screen Time qualification, затем D-004.1…004.3 и вертикаль M1 через D-005. Scope baseline не изменён; календарные обязательства назначаются после калибровки фактического темпа.

## D-004.1 и проверки рисков

- [PM-01 audit](ci-gate-audit.md): публичные ветки не защищены, rulesets пуст; connector не умеет применять эти настройки. Server-enforced gate остаётся открытым действием, CI перед merge обязателен.
- [D-001.1](../integrations/screen-time-spike.md): изучены export/report/macOS APIs. D-001.2 ожидает специального provisioning prerequisite; Q-APPLE-ACTIVITY не пройден. Scope R1 не изменён.
- D-004.1: [bootstrap](../architecture/storage/bootstrap.md) новой файловой SQLite-базы через EF migration с native qualification, WAL/FULL, lease и проверкой целостности. Существующие базы не изменяются. Добавлены 10 storage tests и одно правило architecture; локально пройдены 45 .NET tests. Linux CI подтверждается отдельно на head PR.
- Следующий основной пакет: D-004.2 — реальные profile-scoped repositories, atomic batch/receipt и idempotency; затем D-004.3 — revisions/concurrency. Текущий DbContext migration-only, repository ещё не реализован.

## D-004.2/004.3 и единая структура

Код перенесён в src/core и src/macos, .NET-тесты — tests/core, общие настройки — configs; baseline entry — docs/DESIGN.md. Сборочные пути и CI обновлены. EditorConfig, полный dotnet format и строгий swift-format закрепляют языковые стандарты; checker проверяет допустимые корневые группы. Исключения auto-discovery описаны в repository-layout.

Реализован SqliteStateRepository: atomic batch/revision/receipt, profile scope, история, optimistic conflicts и открытие известной существующей схемы без upgrade. Контрактные тесты общие для fake и SQLite; отдельные real-file тесты покрывают rollback/retry/reopen/concurrency и snapshot integrity. Локально пройдены 69 .NET tests, 5 Swift tests и 28 tooling/schema tests, locked restore и обе проверки стиля. Удалённые прогоны фиксируются на head PR; приёмка приложения целиком не объявляется выполненной.

Следующий пакет после gates этого PR — D-004.4 durable inbox/outbox и затем D-005 local API. Schema upgrade/backup/restore и lifecycle ещё не реализованы.
