# Локальный API v1 — State

Запускаемый .NET host, только loopback `127.0.0.1`, порт по умолчанию 17841. Это внутренний результат M1, не релиз R1 и не готовое macOS-приложение.

Из корня репозитория:

```sh
dotnet restore src/core/LifeTracker.slnx --locked-mode
LIFETRACKER_RUNTIME_ROOT="$PWD/var/profile" dotnet run --no-restore --project src/core/LifeTracker.Host --no-launch-profile
```

Runtime-root должен быть абсолютным и находиться вне отслеживаемых исходников. Для первого запуска используйте новый каталог. Порт можно задать через LIFETRACKER_PORT; значение 0 предназначено для тестов с автоматически выбранным свободным портом. Остановка — Ctrl-C; lifecycle/menu/autostart остаются D-007.

На первом запуске создаются БД, один профиль и машинный bearer credential в `owner.credential` внутри runtime-root. Токен не печатается и не является пользовательским паролем. Клиент получает его локально; не копировать файл в Git, логи, команды общего доступа или issues. На поддерживаемых Unix системах новый файл имеет permissions 0600. При перезапуске профиль и credential сохраняются. Потеря credential или неподдерживаемая schema требуют отдельного recovery, без скрытого пересоздания профиля/миграции.

## Реализованные операции

| Метод и путь от /api/v1 | Результат |
| --- | --- |
| POST /assessments | Частичная запись 1–4 оценок; durable receipt |
| PATCH /assessments/{id} | Новая ревизия с expected_revision и причиной |
| GET /state/current | Четыре показателя, источник/время/fresh, баланс или unknown |
| GET /assessments | Последняя ревизия каждого наблюдения на snapshot, фильтры и cursor pagination |
| GET /capabilities | Явно включённые/отключённые/неподтверждённые возможности |

Все маршруты требуют owner bearer. Профиль назначается сервером, его нельзя выбрать в JSON. Mutation требует Idempotency-Key (8–128 символов); необязательный X-Command-ID — UUID клиента. Без него ID создаёт сервер. Ключ ограничен профилем и семантическим маршрутом; receipt повторной команды сохраняет исходный command ID. Batch receipt не содержит произвольный первый resource_id; IDs доступны в истории. HTTP DTO отделены от Domain/ORM.

JSON: snake_case, RFC3339 timestamp с явным offset, UTC в ответах; required/unknown/duplicate properties проверяются. Ошибки содержат safe code/message/correlation_id. 64 КиБ — технический предел State JSON в этом host; media endpoints ещё выключены и не ограничиваются им как реализованный upload. Trace/header/credential/текст записи не записываются в общие логи.

Остальные 46 операций нормативного контракта возвращают 503 CAPABILITY_UNAVAILABLE. Неизвестный путь — 404. Route inventory configs/api/routes.json генерируется из OpenAPI, проверяется CI и встроен в host; публичные YAML docs не нужны при запуске бинарника. SSE, runtime supervisor, jobs API, journal/finance/quests и фоновые циклы пока не включены.

## История и проверки

История показывает effective revision на начало выдачи, а не все ревизии одного ID. Старые версии остаются в БД; отдельный audit/explain endpoint в этом срезе не включён. Limit 50 (1–100), from включительно, to исключительно, сортировка observed_at desc/id asc. Подписанный cursor связывает профиль/фильтры/limit/snapshot, живёт 24 часа и переживает restart при том же credential. Изменение фильтра, порча или истечение требует нового запроса без cursor. Механизм — [ADR-007](../../../docs/architecture/decisions/ADR-007-history-cursors.md).

```sh
python3 tools/build_api_manifest.py --check
python3 tools/smoke_local_api.py src/core/LifeTracker.Host/bin/Debug/net10.0/LifeTracker.Host.dll
```

HTTP tests используют настоящий Kestrel и синтетический профиль. В CI реально полученные ответы сверяются с OpenAPI через tools/validate_runtime_contract.py; тела/секреты не выводятся. Full install/update/restore, UI и performance qualification этим не подменяются.
