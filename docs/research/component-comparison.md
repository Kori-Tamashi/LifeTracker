# Сравнение компонентов и выбранная конфигурация

Дата: 19 ноября 2026 года. Статус: **выбор проектировщика для реализации**, не утверждение измеренной производительности. Основания — официальная документация и требования продукта. Модели ИИ допущены условно до benchmark; пользовательские API — до проверки доступа. Платные сервисы не выбираются.

## Метод

Сначала жёсткие фильтры: локальная обработка, возможность бесплатного использования, macOS/Apple Silicon, контролируемое хранение и тестирование. Затем инженерное сравнение: ясность слоёв/контрактов, нативные возможности, ресурсы, эксплуатация, поддержка. Оценки ниже — качественное суждение под Life Tracker, не публичный benchmark универсально «лучших» технологий. Стоимость интеграции нескольких языков учитывается: Swift для Apple, C# для ядра, готовые нативные inference binaries; Python остаётся инструментом проверок, не четвёртым продуктовым сервисом.

## 1. Бизнес-ядро

| Вариант | Сильные стороны | Цена для проекта | Решение |
| --- | --- | --- | --- |
| C# / .NET 10 / ASP.NET Core | Статические контракты, DI/host, зрелые инструменты тестов, естественное разделение сборок | Отдельный runtime от Swift; inference через порт | **Выбран** |
| Python / FastAPI | Валидация через модели, OpenAPI, DI, удобство AI-экосистемы | Больше дисциплины для статических границ, отдельная среда зависимостей; не нужен для готовых C++ движков | Резерв при Python-only AI необходимости |
| Swift для всего | Один Apple-язык, прямые системные API | Серверные/контейнерные адаптеры и миграции требуют иной инфраструктуры; ядро сильнее привязано к Apple-инструментариям команды | UI/bridge, не всё ядро |

Выбор C# основан на требованиях к тестируемой бизнес-логике и модульным сборкам, не только на учебном примере. Берём LTS .NET 10, а не автоматически учебный .NET 8. Патч SDK/runtime фиксируется в global.json/lock при создании кода и обновляется контролируемо. [.NET lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0), [FastAPI features](https://fastapi.tiangolo.com/features/).

## 2. macOS-интерфейс

| Вариант | Преимущества | Компромисс | Решение |
| --- | --- | --- | --- |
| SwiftUI + точечный AppKit | Нативные окна/меню/уведомления, прямой Apple SDK, общий язык с iPhone bridge | Второй язык и отдельные UI-тесты | **Выбран** |
| Tauri 2 + web UI | Системный webview, веб-компоненты и кроссплатформенность | Дополнительные web/Rust границы к Apple API; кроссплатформенность сейчас не цель | Резерв при расширении ОС |
| Electron | Web-экосистема, Chromium process model | Дополнительный браузерный runtime/процессы для локального приложения с моделями | Не выбран |

RPG-вид не требует игрового движка: сначала SwiftUI-анимации; SpriteKit — только если прототип покажет необходимость сложной 2D-сцены. Unity/Godot не включаются без обоснования. Не утверждаем разницу в мегабайтах/скорости без замера. [SwiftUI](https://developer.apple.com/swiftui/), [Tauri](https://v2.tauri.app/start/), [Electron process model](https://www.electronjs.org/docs/latest/tutorial/process-model).

## 3. Авторитетное хранилище

| Вариант | Для нашего профиля | Ограничение | Решение |
| --- | --- | --- | --- |
| SQLite | Локальный файл, транзакции, FTS5, без отдельного DB-сервера | Один writer; WAL не для сетевого файла, блокировки/backup требуют дисциплины | **Выбран** |
| PostgreSQL | MVCC, server concurrency, богатые типы/SQL | Дополнительный сервис/диск/резервирование при одном профиле | Резерв при сервере/многопользовательской нагрузке |
| Документные JSON-файлы | Простое чтение вручную | Нет общей транзакции inbox+агрегат+outbox; собственная миграция/индексы | Только экспорт/config |

EF Core SQLite — стандартные агрегаты/миграции; короткие специализированные SQL через Microsoft.Data.Sqlite внутри Infrastructure для job claim, FTS и backup. Все изменения одной операции используют одну транзакцию/соединение, ORM не маскирует границу. Для ограниченных write-конкуренции и одной core instance SQLite достаточность ещё проверяется SP-05.

Точные денежные суммы хранить как integer minor units с валютой/scale там, где масштаб определён; цены/доли — decimal с явным масштабом и правилами сериализации. Не конвертировать деньги в double ради ORM. UTC-время и application-managed revision tokens. Перед выбором миграции проверить ограничения конкретной версии provider. [SQLite use cases](https://www.sqlite.org/whentouse.html), [WAL](https://www.sqlite.org/wal.html), [EF provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/), [EF limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations), [PostgreSQL concurrency](https://www.postgresql.org/docs/current/mvcc-intro.html).

БД в Docker: только процесс ядра открывает файл; не монтировать один WAL-файл для одновременной работы host и VM. Использовать локальный volume внутри среды ядра, backup API выдаёт completed копии в проектную структуру. Раскладку persistent store/volume, её фактический локальный путь и переносимость проверить до данных; неподтверждённый bind-mount не объявляется безопасным. Если обязательная структура проекта несовместима с выбранной volume-схемой, принять нативный core или отдельно согласовать путь, а не скрыто нарушить требование.

## 4. Очередь и планировщик

| Вариант | Что даёт | Цена | Решение |
| --- | --- | --- | --- |
| SQLite inbox/jobs/outbox + BackgroundService | Атомарность с агрегатами, одна точка восстановления | Нужно реализовать ограниченный lease/retry/fencing | **Выбран** |
| Hangfire / Quartz | Готовое расписание/инструменты задач | Проверка лицензии/поддержки storage, дополнительная семантика retry | Резерв при сложном расписании |
| Redis/RabbitMQ + worker | Независимая очередь и несколько узлов | Отдельный сервис и согласование с БД/outbox | Не нужен для одного Mac |
| In-memory queue | Малый стартовый объём | Потеря принятого ввода при остановке | Только внутренний wake-up сигнал |

Не разрабатываем универсальный workflow engine: jobs имеют конечный набор этапов, versioned payload, due time, lease и retry. BPMN описывает бизнес-сценарии, не требует запуска Camunda/Flowable. Планировщик сна/пробуждения сверяет persisted due dates, не «нагоняет» все cron ticks. API/фон используют встроенный host .NET; durable semantics — наши транзакционные алгоритмы, не свойство BackgroundService само по себе.

## 5. Поиск и RAG

| Вариант | Плюс | Ограничение | Решение |
| --- | --- | --- | --- |
| SQLite FTS5 + связи Person/Episode | Нет отдельного сервиса/модели embeddings, прозрачные основания | Лексический поиск слабее семантического; русский recall надо измерять | **Выбран baseline** |
| SQLite + локальные embeddings | Семантическая выдача без сервера | Дополнительная модель, storage, оценка качества/фильтрации | Добавить только при недостаточном recall baseline |
| Qdrant / pgvector | Специализированный/vector поиск | Сервис PostgreSQL/Qdrant и инфраструктура без подтверждённой потребности | Отложен |

FTS5 обеспечивает полнотекстовый поиск, не психологическую достоверность. Не обещаем полноценную русскую морфологию стандартного tokenizer. Основания/версии в SQL, индекс восстанавливаемый. BM25 и entity links — старт; hybrid ranking после corpus comparison. [SQLite FTS5](https://www.sqlite.org/fts5.html).

## 6. Локальное языковое/визуальное исполнение

| Runtime | Преимущества | Компромисс | Решение |
| --- | --- | --- | --- |
| llama.cpp native | GGUF, Metal, локальный server, явный выбор бинарника/весов | Нужны упаковка, manifest, обновления | **Выбран runtime** |
| MLX-LM | Apple Silicon и удобная локальная работа с LLM | Другая модельная упаковка/Python runtime, более узкая portability | Benchmark challenger |
| Ollama local | Удобное управление моделями/сервером | Отдельный менеджер/модельное хранилище; cloud-функции требуют явного отключения | Удобен исследованию, не обязательная зависимость приложения |
| Docker Model Runner | Поддержка Apple Silicon, совместимые API, управление моделями | Зависимость от Docker Desktop и его модельного storage; на Mac движок вне контейнера | Резерв, если уменьшит эксплуатационную сложность |

Не утверждаем, что DMR не поддерживает GPU Mac: официальная документация различает контейнерный passthrough и host inference. Выбор direct llama.cpp — контроль артефактов, путей и API; не доказательство превосходства tokens/sec. [llama.cpp build/Metal](https://github.com/ggml-org/llama.cpp/blob/master/docs/build.md), [MLX-LM](https://github.com/ml-explore/mlx-lm), [Ollama FAQ](https://docs.ollama.com/faq), [DMR](https://docs.docker.com/ai/model-runner/).

## 7. Модели: условный выбор до замеров

| Кандидат | Роль | Решение и проверка |
| --- | --- | --- |
| Qwen3.5-4B | Диалог + изображения, потенциально общие веса вместо двух моделей | **Основной кандидат**: 4-bit GGUF и vision projector, совместимость pinned llama.cpp; проверить русский, extraction, latency и размер |
| Qwen3-4B-Instruct-2507 | Text-only non-thinking | **Fallback для текста**, если новая multimodal модель не проходит runtime/качество/скорость; фото тогда отдельная функция |
| Qwen3-VL-4B-Instruct | Отдельное зрение | Резерв для фото; две модели ухудшают бюджет диска и не устанавливаются автоматически |
| Более крупные 8B+ | Потенциально лучше сложный диалог | Только при доказанном улучшении и пересмотре диска/latency; не дефолт |

Модельные карточки подтверждают тип/лицензию, а не выполнение наших 15 секунд или качество советов. У Qwen3.5-4B и указанных Qwen3 указана Apache-2.0; лицензия конкретного GGUF/конвертации тоже проверяется. Не подменяем модель скачанным неизвестным quant. Pin origin revision, converter version, tokenizer/template, quant type, projector, bytes и SHA-256. Для low latency проверять поддерживаемый non-thinking режим и ограниченный контекст; не обещать работу полного рекламируемого context window. [Qwen3.5-4B card](https://huggingface.co/Qwen/Qwen3.5-4B), [text fallback](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507), [VL fallback](https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct).

## 8. Распознавание речи

Выбран whisper.cpp native + multilingual small как baseline. Medium — challenger на русском корпусе, tiny/base — только если качество достаточно при лучшей скорости. Не использовать `.en` модель для русской речи. Faster-whisper — резерв для другого runtime/аппаратуры, Apple Speech — дополнительный кандидат после проверки offline/языковых ограничений. Один локальный движок с Metal/Core ML возможностями уменьшает зависимость от Python-пакетов; ускорение и точная конфигурация проверяются.

В README проекта small занимает 466 MiB, medium 1.5 GiB; это размеры указанных исходных вариантов, не итог всей поставки/памяти. Baseline small выбран по диску и ресурсу, не по недоказанному WER. [whisper.cpp](https://github.com/ggml-org/whisper.cpp).

## 9. Транспорт, бот, тесты и моделирование

- API: HTTP/JSON versioned contracts + OpenAPI, SSE для статуса/потока ответа. Внутренний direct вызов application остаётся без HTTP. gRPC сравнен как типизированная альтернатива, но streaming HTTP достаточно и проще Swift/core/debug; latency измеряется. Loopback binding и минимальная привязка владельца; контейнер не делает API публичным по умолчанию.
- Telegram: Bot API long polling — не требует публичного входящего URL, подходит Mac. Webhook — будущий сервер. Checkpoint после durable intake; outage >24h может потерять ещё не принятые updates. Библиотеку клиента фиксировать после проверки совместимости, канальные DTO не пускать в домен. [Telegram API](https://core.telegram.org/bots/api#getupdates).
- Core tests: xUnit v3, ArchUnitNET для dependency rules; свойства/мутации подключаются с реальным кодом. Swift Testing для логики клиента, XCTest/XCUITest для UI. Не переносим обязательность Moq/FluentAssertions из примера: простые fakes и встроенные assertions достаточны, лицензии и version compatibility проверяются. [xUnit](https://xunit.net/docs/getting-started/v3/getting-started), [ArchUnitNET](https://github.com/TNG/ArchUnitNET), [Swift Testing](https://developer.apple.com/documentation/testing/).
- CI: GitHub Actions, Linux для ядра, macOS для клиента, отдельный isolated Apple Silicon benchmark. Текущий workflow проверяет только проектные материалы. Продуктовые jobs появляются с кодом, не фиктивным success.
- BPMN: файлы BPMN 2.0 XML с DI, импорт в совместимый локальный редактор. Без runtime engine для одного профиля; код реализует алгоритмы и state machines. [OMG BPMN](https://www.omg.org/spec/BPMN/2.0.2/).

## 10. Диск и стоимость

Предложенный конверт R2, **не замер**: до 3.5 ГБ на quant LLM+projector, до 0.6 ГБ STT baseline, до 1 ГБ app/runtime, до 1 ГБ базы/evidence, до 2 ГБ копий/media, до 0.4 ГБ cache/log, 1.5 ГБ запас = 10 ГБ. Это бюджеты категорий, не заявления о реальных размерах Qwen/GGUF. Media/history quotas обязательны. Дополнительный Docker Desktop/VM/images может превысить остаток; полный footprint измерять отдельно с чистой установкой и сравнивать нативный core.

Если комплект превышает 10 ГБ, использовать согласованный предел 20 ГБ только с прозрачным отчётом; не удалять уникальные данные скрыто. Для фото общие multimodal веса предпочтительнее второй LLM, но выбранный кандидат обязан пройти compatibility/качество. Обучение моделей не планируется.

Бесплатное использование Docker Desktop сверяется с условиями для конкретного типа использования, а не объявляется универсально бесплатным. Бесплатный Apple Account не равен безусловной фоновой установке всех entitlements: подпись/возможности iPhone bridge остаются шлюзом. [Docker Mac terms](https://docs.docker.com/desktop/setup/install/mac-install/), [Apple account capabilities](https://developer.apple.com/help/account/basics/about-your-developer-account).

## Итог выбора

**Базовый стек:** C#/.NET 10 + ASP.NET Core; SwiftUI/AppKit; SQLite + EF Core и точечный SQL; persisted jobs/outbox + BackgroundService; FTS5; native llama.cpp и whisper.cpp; HTTP/JSON/SSE; Telegram long polling; xUnit/ArchUnitNET и Swift Testing/XCTest; GitHub Actions. Контейнерное ядро сохраняется как предпочтение при прохождении footprint/storage gate, нативный запуск — поддерживаемая альтернатива.

**Условный AI bundle:** Qwen3.5-4B quant + multilingual Whisper small. Они выбраны для первого сравнения на устройстве; устанавливать/объявлять production-ready до замеров нельзя. Обязательные протоколы эксперимента: не менее 20 синтетических минутных аудио, cold/warm отдельно, p50/p95, WER/extraction precision/recall, retrieval recall@k, disk/RAM/energy. Цель p95 ≤15 секунд — предлагаемая строгая интерпретация ориентира ТЗ; при неуспехе сообщить и изменить решение/требование явно.
