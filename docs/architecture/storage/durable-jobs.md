# D-004.4: durable inbox/jobs/outbox

Пакет реализует storage primitives, не запускает scheduler, модель или внешнюю доставку. Модуль Operations разделён на Domain (retry policy и модели), Application (порты) и Infrastructure (SQLite). Общие connection/lease/migrations перенесены из State в LifeTracker.Infrastructure.Shared; State и Operations не зависят от Infrastructure друг друга.

## Транзакционные границы

- Enqueue атомарно пишет source_events inbox/receipt, queued job и job.accepted outbox. Повтор source/external ID и нормализованного input/kind возвращает исходную квитанцию, даже после reopen; другой payload или коллизия command/job identity даёт conflict. Нормализация JSON убирает незначимые пробелы, но не обещает одинаковый hash при перестановке ключей объекта. Новое намерение пользователя требует нового внешнего ключа.
- Claim использует IMMEDIATE transaction, due-time и стабильный порядок ready_ms/id. Attempts и fence возрастают; lease назначается владельцу. Истёкший lease разрешает повторный захват, но не обнуляет attempts. Текущий claim — FIFO по готовности, не окончательный fairness scheduler.
- Heartbeat/completion/failure требуют profile/job ID, owner, точного fence и lease_until > now. На границе истечения старый worker не имеет права записи, даже до reclaim. Heartbeat не сокращает уже имеющийся lease.
- Completion фиксирует только job result, source state и outbox одной транзакцией. Модель работает вне транзакции. Внешние эффекты и доменные результаты нельзя записывать отдельным вызовом State repository перед fenced completion: применение извлечённых бизнес-данных с input revision checks появится отдельной атомарной операцией при реализации обработчика.
- Cancellation повышает fence, завершает активное задание и инвалидирует lease. Терминальные completed/failed/cancelled повторно не захватываются.
- State batch/correction теперь добавляют state.changed outbox в ту же транзакцию, что revision/current pointer/receipt. Ошибка вставки outbox откатывает весь результат; повтор команды не порождает второй event.

## Retry и восстановление

Автоматически повторяются только timeout/unavailable: максимум 5 общих попыток, full jitter `U(0,min(15min,5s*2^(attempt-1)))`. Random — внедрённый IRetryJitter; тесты используют детерминированное значение. Validation/auth/conflict/terminal немедленно становятся failed. Потерянный последний worker переводится в failed при следующем Claim, с повышением fence и outbox. Исходный input остаётся в jobs.

Reopen сохраняет running job и lease. Новому worker не нужно «обнулять очередь»: после истечения lease можно reclaim. Wake orchestration, fairness 3:1, ограничение тяжёлых inference задач, пользовательский ручной повтор и lifecycle Stop подключаются в host/lifecycle; данный пакет не выдаётся за их реализацию. Тесты моделируют брошенную работу и повторное открытие файла; force-kill процесса/аппаратный сбой не объявляются проверенными.

## Outbox: at-least-once

ReadPending возвращает события со стабильным ID до MarkDispatched. Отправка выполняется вне DB transaction. Падение между отправкой и ack может привести к повтору: получатель обязан дедуплицировать по event ID. Ack ограничен profile/event и идемпотентен; exactly-once внешняя доставка не обещается. Порядок при одинаковом created_ms детерминирован по ID, но не является причинным порядком событий; потребитель сверяет актуальное состояние/версию в авторитетной БД.

Нет активного delivery loop или сетевых обращений. Бот/уведомления получат собственные delivery guards в соответствующем пакете. Личные входы и результаты не попадают в логи или CI fixtures.

## Миграции и допуск

Общий CoreDbContext применяет InitialState и новую DurableJobs migration при Create новой базы. Перенесены точные определения jobs/outbox/jobs_due из нормативной schema-v1.sql. Logical user_version остаётся 1; конкретный поддерживаемый набор определяется также EF history. OpenExisting открывает только точное совпадение history, не применяет pending migrations. Старые одно-миграционные базы отклоняются без изменения таблиц/профиля; backup-before-upgrade остаётся D-009. Следующая полноценная установка приложения не должна обходить этот gate.

## Проверки

Синтетические real-store fixtures покрывают acceptance/receipt dedup, параллельные accept/claim, owner/profile/fence checks, точную границу expiry, heartbeat, due time, transient/terminal ошибки, исчерпание попыток, cancellation, reopen, outbox redelivery/ack, отказ DDL upgrade и rollback при ошибке outbox. Domain tests проверяют jitter bounds и cap attempts; architecture tests — отсутствие зависимостей внутренних слоёв от адаптеров и независимость модулей. State regression suite остаётся обязательной.
