# ADR-007: стабильный snapshot истории State

Статус: принято для реализации D-005. Требование: cursor pagination не должна пропускать/дублировать наблюдения при записи и исправлении между страницами; cursor связан с запросом и переживает restart.

## Решение

В physical schema добавляется вспомогательная state_commits с INTEGER PRIMARY KEY AUTOINCREMENT sequence и unique/FK на `(profile, entity, revision)`. State repository добавляет sequence в ту же транзакцию, что revision/receipt/outbox. Это техническая последовательность коммитов наблюдений, не новая доменная сущность и не SSE sequence.

Первая страница фиксирует watermark. Последующие выбирают максимальную ревизию каждой identity, зафиксированную не позже него; current pointer не подменяет исторический snapshot. Сортировка — полное observed_at desc + canonical UUID asc, границы from inclusive/to exclusive. Token содержит watermark, последний ключ, expiry и hash профиля/фильтров/limit, защищён HMAC. Signature key выводится из постоянного owner credential; его замена инвалидирует cursors. Срок — 24 часа.

Неподписанный offset не подходит: новые записи сдвигают страницы. Timestamp watermark недостаточен при одинаковом времени/сдвиге часов. Неявный SQLite rowid не используется как долгосрочный контракт: техническая последовательность имеет явный INTEGER PRIMARY KEY и не переиспользуется после удаления. Хранить полную отдельную копию выдачи для каждого cursor не требуется.

## Последствия

Новая EF migration добавляет/backfill вспомогательной таблицы; logical schema version остаётся 1, точная совместимость определяется также EF history. Старые базы без migration не обновляются автоматически: нужен backup-before-upgrade из D-009. Технические таблицы EF/commit sequence дополняют 41 логическую таблицу нормативной модели, а не меняют число доменных агрегатов.

В этом срезе history reader материализует профильный snapshot перед фильтрацией/сортировкой в Application. Для больших историй требуется benchmark и при необходимости SQL keyset optimization, с сохранением контракта; обещание целевой latency не дано. Snapshot tests проверяют исправление времени, новые записи, ties, restart, tampering и expiry. Хранение старых ревизий остаётся авторитетным; удаление профиля/retention должны учитывать техническую последовательность.
