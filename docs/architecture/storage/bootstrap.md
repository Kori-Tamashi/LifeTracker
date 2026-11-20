# D-004.1: bootstrap новой State-базы

Реализация — `LifeTracker.State.Infrastructure`, .NET10/EF Core SQLite.Core 10.0.15, SQLitePCLRaw bundle 3.0.5. Транзитивная native SQLite закреплена lock-файлом; фактическая версия проверяется через `sqlite_version()` до миграции, floor 3.51.3. JSON/FTS5 проверяются отдельно. В текущем bundle native package `SQLite` 3.53.4; версия пакета не заменяет runtime gate.

## Граница среза

`NewStateStore.CreateAsync(absoluteRuntimeRoot)` получает exclusive writer lease, атомарно резервирует новый `lifetracker.sqlite`, открывает непуленное соединение с FK/busy timeout, квалифицирует runtime, включает WAL/FULL и применяет reviewed EF migration. После неё выполняются foreign_key_check/integrity_check. Lease удерживается до DisposeAsync; connection принадлежит store.

Перенесены точные определения profiles/entities/revisions/source_events/observations и двух индексов из нормативной DDL. История EF migration и user_version=1 добавляются при создании. Остальные таблицы baseline появятся с соответствующими компонентами. user_version=1 не означает, что все 41 таблица уже реализованы; набор приложения определяется также EF history/capability manifest.

Пока DbContext — migration boundary без row mappings. Raw SQL внутри EF migration сохраняет STRICT/deferred FK, не представимые обычной генерацией EF. Future EF migrations обязаны учитывать эти артефакты и использовать reviewed rebuild при изменении; automatic EnsureCreated/EnsureDeleted запрещены. Текущий срез не предоставляет production IStateRepository; это D-004.2/004.3.

Существующий файл отклоняется через FileMode.CreateNew без truncate или миграции. Ошибка инициализации закрывает connection/lease, сохраняет файл для inspection, не удаляет его автоматически. Повторный запуск после неудачи требует отдельного recovery-пути; upgrade/backup/restore реализуются позднее, без скрытого обхода. Data-losing Down не допускается.

## Проверки

Real-file tests: runtime floor/JSON/FTS5, WAL/FULL/FK/timeout; первая миграция и EF history; lease второго writer; существующая запись после закрытия/повторного открытия; pre-cancel/relative root; deferred identity/revision commit; active-profile/score/metric/FK constraints; rollback DDL при конфликте схемы; буквальное соответствие применённого DDL нормативному подмножеству. Это не тесты полноценного repository, cross-process crash recovery или backup.

Архитектурные тесты дополнены правилом, запрещающим зависимость внутренних слоёв от Infrastructure. Проверки идут в обычном core CI на реальной bundled SQLite, не EF InMemory provider.

## Зависимости и лицензии

NuGet metadata: EF Core — MIT; SQLitePCLRaw — Apache-2.0; native SQLite package LICENSE.txt — Public Domain с ссылкой на [SQLite copyright](https://sqlite.org/copyright.html). Полный release SBOM/license manifest остаётся отдельным gate. Зависимости закреплены центрально и транзитивно; моделей/платных сервисов bootstrap не использует.

Особенности EF SQLite и ручных rebuild проверены по [официальной документации](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations). Runtime migration пока разрешена только для новой синтетической/пустой установки; startup migration существующих данных без предварительной verified backup не добавлена.

## D-004.2/004.3: repository

`SqliteStateRepository` реализует profile-scoped current/history/identity/receipt. Batch и исправление фиксируют typed observations, immutable revision snapshot, current pointer и source_events receipt одной IMMEDIATE-транзакцией. Ключ дедупликации ограничен профилем/каналом; другой payload или повторный command ID под другим ключом отклоняются. Receipt найденной команды возвращается до optimistic revision check. Свежесть и баланс вычисляет Domain, SQL не дублирует правила.

Shared contract tests компилируются для test fake и SQLite из tests/core/Contracts. Специфические tests проверяют rollback посреди batch/исправления, параллельные retries через разные repository instances, конфликт expected revision, persistent receipt после reopen, metadata provenance и рассогласованный snapshot. Временные поля поиска хранятся в миллисекундах, snapshot сохраняет полную точность timestamps для round-trip; их соответствие проверяется при чтении.

OpenExistingAsync открывает только базу с точным известным набором EF migrations и user_version; не применяет upgrade автоматически. Backup/restore, crash recovery и jobs/outbox остаются отдельными пакетами. Shared semaphore принадлежит store: все repository instances сериализуют доступ к одному connection. Прямой connection — только для bootstrap/композиции/тестов, не для UI или бизнес-логики. Cancellation проверяется до транзакции и commit; SQLite busy wait может занимать заданный timeout.
