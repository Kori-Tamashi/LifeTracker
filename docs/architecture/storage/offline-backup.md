# Offline backup State-профиля

Реализовано в develop для следующей версии; установленная beta.1 этих CLI-команд не содержит. Это часть D-009.1: ручной offline snapshot, проверка и восстановление. UI, расписание, retention, автоматическое управление диском и recovery после ошибки миграции остаются открытыми.

## Операторский сценарий

Полностью остановить клиент и Core, включая отдельно запущенный внешний host. Закрытия окна недостаточно. Из корня репозитория после сборки:

```sh
sh tools/dotnet.sh build --no-restore
LIFETRACKER_RUNTIME_ROOT="$PWD/var/beta-1" dotnet src/core/LifeTracker.Host/bin/Debug/net10.0/LifeTracker.Host.dll backup "$PWD/var/backups/snapshot-001"
dotnet src/core/LifeTracker.Host/bin/Debug/net10.0/LifeTracker.Host.dll verify-backup "$PWD/var/backups/snapshot-001"
dotnet src/core/LifeTracker.Host/bin/Debug/net10.0/LifeTracker.Host.dll restore "$PWD/var/backups/snapshot-001" "$PWD/var/restored-001"
```

Все пути абсолютные. Каталог назначения обязан отсутствовать; исходный и целевой каталоги не могут содержать друг друга. Для проверки результата запустить приложение с новым восстановленным профилем. Исходный профиль не заменяется. Примеры не создают копию автоматически: команды выполняет оператор.

## Состав и алгоритм

Scope `state-profile-offline-v1`, format 1, schema 1. Содержит SQLite snapshot, owner.credential, необязательный desktop-pending-assessment.json и manifest с profile ID, временем и SHA-256 каждого артефакта. Pending journal переносится побайтно; его envelope проверяет desktop при открытии. Сохранение credential и durable receipts обеспечивает прежнюю identity и idempotent replay.

Создание получает исключительные блокировки desktop journal и Core writer. SQLite Backup API создаёт согласованный snapshot вместо копирования файла открытой WAL-базы. Временный каталог рядом с назначением проверяется на точную версию миграций, integrity, foreign keys и единственный активный профиль; затем публикуется rename. Обычное открытие/закрытие SQLite может изменить физическое представление исходных WAL/DB; логические записи сохраняются.

Verify сверяет полный inventory и hashes, затем проверяет disposable копию БД, не меняя backup. Restore повторно сверяет скопированные файлы, валидирует БД и публикует новый каталог. Существующий каталог не перезаписывается. Неизвестные файлы/каталоги и ссылки внутри профиля запрещены, чтобы будущие медиа или модули не терялись молча. Файлы блокировок и WAL не входят в результат.

Отмена проверяется до публикации; синхронный вызов SQLite Backup API не прерывается cancellation token. При обычном отказе временный каталог удаляется. Аварийное выключение может оставить staging-каталог; гарантия fsync каталога и квалификация power-loss/large-profile/low-space ещё не заявлены. Автомиграция неподдерживаемой схемы не выполняется.

## Хранение и проверки

Локальные копии размещать в ignored `var/backups`, вне каталога профиля. Копии содержат личные данные и действующий credential: не публиковать в Git, CI artifacts или issues. SHA-256 выявляет повреждение, но не заменяет шифрование или подпись. На Unix новые файлы создаются 0600, staging-каталог 0700. Копия на том же диске не защищает от его потери.

HTTP/infrastructure tests и `tools/smoke_profile_backup.py` используют только синтетику: живые блокировки, roundtrip, неизменность backup при verify/restore, повтор receipt без дубликата, отказ на повреждении, существующем назначении, неизвестном содержимом и несовместимой схеме. Process smoke включён в Linux CI и macOS CI с упакованным Core. Это не GUI-приёмка.
