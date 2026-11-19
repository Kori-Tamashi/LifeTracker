# Физическая модель SQLite v1

Baseline 1.0. [schema-v1.sql](schema-v1.sql) — проверяемый проект DDL, не применённая миграция приложения. В реализации переносится в EF migrations с эквивалентными constraints/индексами/триггерами и отдельным review diff. SQL хранится здесь как нормативный контракт хранения.

## Представление

41 логическая таблица, включая FTS5 (его внутренние таблицы не являются доменными). `profiles` задаёт владельца; `entities` — стабильная identity и указатель текущей ревизии; `revisions` — версия, автор, статус и snapshot. Типизированные таблицы (`observations`, `meals`, `budgets` и др.) содержат поля, требующие поиска/constraints; JSON snapshot не заменяет их typed schema и не создаёт универсальный бизнес-репозиторий. Дубликаты полей в snapshot и typed row записываются одной unit of work, иначе commit отклоняется сценарием.

| Группа | Таблицы | Инварианты |
| --- | --- | --- |
| Identity | profiles, entities, revisions | Один активный профиль; deferred FK не позволяет current pointer на несуществующую версию |
| Источники | source_events, media, evidence, revision_evidence, dependencies | Source key уникален; evidence/зависимости одного профиля; граф ацикличен (application guard) |
| Состояние | observations, measurements | 4 вида, half-score 0–20, индекс выбора по времени/источнику |
| Дневник | persons, episodes, person_mentions, conversations, messages, journal_entries, extractions | Не смешивать обработку job и принятие extraction; people merge и отрицания проверяет домен |
| Выводы | interpretations, disagreements | Основания и версии; несогласие отдельно; stale не участвует в retrieval |
| Питание | meals | Порция/диапазон в schema-versioned JSON; unknown не 0 |
| Квесты | quest_templates, quest_occurrences, game_effects | Нагрузка/срок, outcome revisions; один исходный эффект и не более одной reversal на эффект |
| Финансы | accounts, ledger_journals, ledger_postings, budgets, budget_lines, reserves, closing_records | Постинг draft→posted только при нулевой сумме по валюте; immutable posted lines; restatement месяца |
| Инвестиции | instruments, investment_events, portfolio_snapshots, calculation_runs | Typed event kind, decimal strings со scale, input revisions и coverage расчёта |
| Фон | jobs, outbox, integration_state | Durable due/lease/fence, уникальные ключи, outbox в транзакции |
| Уведомления | notification_events, deliveries | Три уникальных слота на день, отдельные доставки одного события |
| Копии/поиск | backup_manifests, search_entries | Manifest lifecycle; индекс восстанавливается и не авторитетен |

## Контроль типов и границ

Идентификаторы — UUID в канонической строке, генерирует приложение; SQLite FK используют `(profile_id,id)` или `(profile_id,entity_id,revision)` для связей. Typed `entity.kind`/содержимое body JSON и связь конкретного вида сущности с typed row проверяются Application и contract tests: универсальный FK не доказывает принадлежность к агрегату. Это сознательный общий revision registry, не разрешение смешивать модули.

Время — integer Unix milliseconds UTC. Оценки — integer half units; Money — integer minor units с currency/scale; суммы JSON/API передаются строкой, чтобы JS-клиент не терял точность. Decimal quantities/price/investment values — canonical decimal strings + scale в проверяемом JSON schema, без преобразования в REAL. В API исходное значение источника сохраняется для explain.

Долг — собственный тип отчёта и signed liability, credit limit не проводка дохода. Двойная запись защищена SQL trigger при posted; новый journal всегда draft. Application проверяет число строк, принадлежность счетов, ограничения сумм и экономический смысл. Корректировка posted — обратный journal, не редактирование строк.

## Runtime

SQLite runtime **не ниже 3.51.3** с FTS5/JSON; проверяется фактическая native library при старте, а не только версия NuGet. WAL-reset исправлен в 3.51.3: [официальное описание](https://www.sqlite.org/wal.html). Syntax tests текущего DDL в памяти не сертифицируют старую библиотеку для production WAL.

Native core открывает только абсолютный путь внутри выбранного runtime root; все соединения используют foreign_keys=ON, busy_timeout=5000, WAL и synchronous=FULL. `BEGIN IMMEDIATE` для job claim/квоты/финансового commit; короткие write-транзакции. Одно core instance владеет lock-файлом. Модель и UI не открывают БД. Checkpoint централизован scheduler, не параллельными background loops. WAL size alarm 128 МиБ, checkpoint при idle; долговременные read transaction не удерживаются UI.

Для Docker режим WAL проверяется только внутри локальной файловой системы контейнерной среды. Нельзя одновременно открывать БД host/VM. Перенос native↔container — backup/restore с остановкой писателя; baseline по умолчанию native. Path/ownership/export manifest документируются в deployment.

## Миграция и совместимость

`user_version=1`; отдельная таблица/история EF migration добавляется при реализации. Startup: version check → exclusivity lock → completed backup → миграция transaction/rebuild → foreign_key_check + integrity_check + read smoke → разрешить запись. При неуспехе сохранить исходник/backup, остановить writer, показать recovery UI. Down migration с потерей данных не выполняется автоматически; rollback — восстановление согласованного snapshot и предыдущего app bundle. Клиент и API version независимы от schema version.

## Граница удаления

Обычная правка — revision, не SQL DELETE. Полное удаление единственного профиля останавливает workers и уничтожает управляемый store/media/index/copies; не каскадное удаление одной строки, обходя immutable ledger triggers. Экспорт включает manifest и все активные/исторические данные согласно политике. Политика retention исключает pinned media и незавершённые jobs.
