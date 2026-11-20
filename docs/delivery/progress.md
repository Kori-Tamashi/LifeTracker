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

## D-004.4: durable storage primitives

Добавлен модуль Operations и общий Infrastructure.Shared: SQLite store/migration boundary больше не принадлежит State. Inbox acceptance, job/receipt/outbox атомарны; поддержаны claim/lease/heartbeat, fence guard, ограниченные retries, отмена, terminal state и повторное открытие. Изменения State также фиксируют outbox в своей транзакции. Подробная граница и незавершённые host-политики — [durable-jobs](../architecture/storage/durable-jobs.md).

Старые базы с pending migration не обновляются автоматически. Scheduler/delivery loop, fairness, input-revision materialization, ручной retry и lifecycle всё ещё требуют host/use-case реализации. Следующий основной пакет — D-005 local API; R1 не выпущен.

Для D-004.4 локально пройден 101 .NET test (включая State regression), сборка без предупреждений. Старые Swift/tooling проверки не заменяют новый удалённый CI; результат на head PR проверяется перед merge.

## D-005: локальный API и внутренняя веха M1

Реализован запускаемый [Host](../../src/core/LifeTracker.Host/README.md): loopback, machine owner credential, пять State/capabilities operations, строгий HTTP DTO и typed errors. Остальные 46 нормативных операций возвращают capability unavailable. Cursor history использует commit watermark/HMAC/24h TTL; новые записи и коррекции не меняют начатую выдачу. Общая migration дополнена технической commit sequence, без скрытого upgrade старых баз.

Локально пройдены 121 .NET test и process smoke с настоящим остановленным/повторно запущенным CLI: record → restart → read/retry → correction. Шесть реально полученных ответов соответствуют пяти OpenAPI schemas. Полный проектный набор и удалённый CI проверяются перед merge. Это M1 backend/demo scope, не R1 release и не UI acceptance.

Следующий пакет — D-006 SwiftUI-персонаж, быстрый ввод и история через этот API. Автозапуск/lifecycle, scheduler, backup/restore и квалификация Apple остаются отдельными gates.

## Минимальный корень репозитория

Удалены корневые props, .editorconfig и AGENTS.md; SDK config перенесён в configs/dotnet. Общие настройки подключаются через tools/dotnet.sh с явными MSBuild paths, Roslyn использует configs/.editorconfig. CI/команды обновлены, checker запрещает возвращение служебных файлов в корень и AGENTS.md в отслеживаемое дерево. Политики проекта остаются в docs.

## D-006.1: главное окно состояния

Добавлен SwiftUI executable LifeTrackerDesktop: нейтральный 2D-силуэт, серверный баланс, карточки показателей и подробности с источником/временем. Обновление ручное, при активации и раз в минуту. При истечении серверного valid_until баланс скрывается до обновления; ошибка не стирает последний snapshot и не выдаёт его за текущий. Креденциал читается локально из runtime-root; настройки запуска — src/macos/README.md и tools/run_macos.sh.

Локально Swift executable собран, 8 Swift tests пройдены; presentation tests покрывают expiry boundary, failure со старым snapshot и cancellation. Это реализация D-006.1 без объявления визуальной/UI E2E-приёмки. D-006.2 (ввод/история/исправления), D-006.3 (.app/установка) и R1 gates остаются открытыми. Remote CI проверяется на head PR.

## D-006.2: ввод, история и исправления

Добавлены частичная самооценка 1–4 показателей, шаг 0,5, время/комментарий, cursor history и исправление с причиной/expected revision. Payload и command ID сохраняются до отправки в локальный журнал; повтор после неопределённого результата или перезапуска использует прежний idempotency key. Явный revision conflict не перезаписывает новую версию. При ошибке диска отправка не начинается, повреждённый журнал не сбрасывается; отдельная блокировка предотвращает второй desktop writer.

Локально 16 Swift tests прошли, отдельно live Swift/host integration проверила record → retry → history → correction → conflict. Последняя добавлена в macOS CI вместе со сборкой реального host. Tooling/schema: 30 tests; строгий Swift lint пройден. GUI-автоматизация не смогла выбрать незапакованный executable как native app; visual/UI E2E gate остаётся открытым до D-006.3. Установка, lifecycle и релиз R1 не объявляются готовыми.

## D-006.3: локальный desktop bundle

Добавлены Release `.app`, Info.plist в configs/macos, sealed ad-hoc подпись и манифест исходного commit/dirty status. Установщик допускает только известный состав пакета, проверяет подпись, отказывается обновлять запущенный клиент; staged update восстанавливает прежний пакет при ошибке rename и сохраняет backup при ошибке rollback. Профиль не копируется и не изменяется установкой. Окно без environment предлагает выбрать папку профиля/порт уже работающего host.

Локальный пакет собран/проверен; tooling tests дополнены сценариями безопасности установки. CI дополнен реальной сборкой, verify, установкой и повторным обновлением пакета. После ошибки сеанса повторная GUI-проверка получила «Computer Use permissions are not granted»; визуальная приёмка, UI E2E и M2 остаются открытыми. Это не R1 release; notarization, автозапуск, supervisor и подготовка полного установочного решения не объявляются готовыми.
