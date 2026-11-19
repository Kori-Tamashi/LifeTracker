# API v1 — семантика baseline 1.0

Нормативный машиночитаемый контракт: [openapi.yaml](openapi.yaml), 51 операция, OpenAPI3.0.3. Это проверенный проект интерфейса, не работающий сервер. Внешние DTO не являются Domain/ORM объектами.

## Адреса и scope

Native local `http://127.0.0.1:17841/api/v1`, port в install manifest. Machine-generated owner credential передаётся bearer; нет дополнительного пользовательского пароля/TouchID. Trusted adapters назначают профиль/роль сами. Bridge имеет отдельный credential только на Health import и отдельный paired TLS endpoint; Stop/restore/delete доступны только локальному supervisor/owner, не LLM/Telegram/bridge. Capability manifest ограничивает маршруты этапа; невыпущенный endpoint возвращает CAPABILITY_UNAVAILABLE/503, не фиктивные данные.

## Команды

Mutation требует Idempotency-Key8–128 chars. Key scoped на owner+route+semantic operation; одинаковый key/payload возвращает прежний durable receipt, другойpayload —409. Команда содержит client command ID в инфраструктурном envelope; HTTP trace correlation формируется сервером. expected_revision обязателен там, где меняется сущность; settings patch сериализуется под Profile transaction, сервер отвечает текущей revision. Наблюдения будущего времени/некорректного шага/повторного metric внутри одного request отвергаются целиком 422.

200 — завершённая синхронная команда;202 — durable приём с job_id, не завершённый анализ; ошибка не подтверждает сохранение. Receipt command_id/resource_id/revision/job_id позволяет продолжить после разрыва. JSON sum values — string minor units+currency+scale, timestampISO-8601 UTC. Максимальная длительность voice30min/50MiB, photo20MiB/40MP, text20000chars. Media проверяется по декодированному виду/размеру, а не только MIME/filename; decompress bomb отклоняется до обработки. Raw absolute path в запросе не принимается.

`MessageCreate`: ровно одно из text/media_id (semantic validator); text+media одновременно 422. Media ID должен принадлежать тому же профилю, быть durable и допустимого типа. Upload подтверждается только после сохранения; неиспользованный upload убирается через 24h после проверки ссылок/pins. Просмотр media/artifact требует owner auth, не произвольный файловый путь.

## Queries и события

Cursor pagination50 по умолчанию, max100, sort `(observed_at desc, id asc)` для временных записей; cursor непрозрачный, содержит query fingerprint/snapshot watermark и не принимает изменение фильтра на следующей странице. `from` включительно, `to` исключительно. Неполная история/нет source данных показывается как empty/incomplete, не 0. EntitySummary — карточка; подробный explain/revision возвращается соответствующим query, не чтением произвольной таблицы.

SSE `/events`: monotonic persisted outbox sequence, event_id и safe payload(resource ID/type/revision/status), no raw diary text. Last-Event-ID до 24h; устаревшийcursor → явное snapshot_required и полная перезагрузка queries. Heartbeat15s без бизнес-события, реконнект экспоненциальный с cap30s; не обязан вызывать model или держать DB read transaction. Точное событие — invalidation, authoritative value UI получает query; оно не обещает глобального exactly-once.

## Ошибки

400malformed,401missing/invalid credential,403valid credential outside scope,404not found,409revision/idempotency conflict,413input too large,422domain validation,503unavailable dependency/capability. Все ошибки typed code+safe message+correlation_id; нет stack trace, текста разговора, ключей и персональных путей.

## Inference и imports

Локальный model port описан в [AI policy](../ai-policy.md), нормализованные import batches — в [qualification](../../integrations/qualification.md). Версии protocol/API, schema БД, model manifest и rules независимы. При несовместимом major startup блокирует клиент с update/recovery action; не интерпретирует поля другого major молча. HTTP serialization/typed endpoints не снимают доменные проверки и expected revision.

Изменение endpoint/schema отражается в OpenAPI, contract suite и release notes. Контракт валидирован отдельным OpenAPI validator; примеры-score проверены JSON Schema tests. Это не доказывает, что сервер реализует все 51 операции.
