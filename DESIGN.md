# Life Tracker — проектный baseline 1.0

20 ноября 2026 года. **Основное проектирование завершено как документированный baseline для реализации.** Реальный доступ к интеграциям и эксплуатационные характеристики проверяются отдельными gates, а не предполагаются выполненными. Код приложения ещё не реализован.

## Пакет

| Область | Нормативный результат |
| --- | --- |
| Требования | [ТЗ 1.0](docs/requirements/specification.md), [59 требований](docs/requirements/registry.json) |
| Бизнес-правила | [BR-01–10](docs/requirements/baseline-rules.md), [финансы/портфель](docs/requirements/finance-design.md) |
| Архитектура | [Обзор](docs/architecture/overview.md), [Clean Architecture/SOLID](docs/architecture/clean-architecture.md), [классы/модули](docs/architecture/module-implementation.md) |
| Алгоритмы и процессы | [ALG-01–11](docs/architecture/algorithms/README.md), [11 BPMN](docs/processes/README.md) |
| Данные | [Физическая схема](docs/architecture/storage/README.md), [DDL41 таблица](docs/architecture/storage/schema-v1.sql) |
| API | [OpenAPI51 операция](docs/architecture/contracts/openapi.yaml), [семантика](docs/architecture/contracts/application-api.md) |
| Диаграммы | [16 C4/UML/ER/deployment](docs/architecture/diagrams/README.md), SVG и source hashes |
| Интерфейсы | [macOS screens и Telegram](docs/ux/interface-specification.md) |
| ИИ | [Local policy/corpus/qualification](docs/architecture/ai-policy.md) |
| Эксплуатация | [Deployment/lifecycle](docs/architecture/deployment.md), backup/retention/recovery |
| Качество | [43 planned cases](docs/quality/acceptance-cases.json), [CI/quality gates](docs/quality/release-acceptance.md) |
| Решения | [ADR](docs/architecture/decisions/README.md), [ADR-006 freeze](docs/architecture/decisions/ADR-006-design-baseline.md) |
| Реализация | [24 задачи и 4 этапа](docs/delivery/baseline/implementation-plan.md), GitFlow |

## Что зафиксировано

C#/.NET10 core с чистыми слоями, SwiftUI/AppKit, SQLite≥3.51.3, единый writer, persisted jobs/inbox/outbox, FTS5, native llama.cpp/whisper.cpp, HTTP/JSON/SSE и Bot API long polling. Native default, Docker optional после storage/footprint gate. Самооценки/ИИ-предположения/игровые атрибуты раздельны. Все основные бизнес-формулы и дефолты определены BR; новые изменения проходят versioned ADR.

## Что не объявлено доказанным

- Screen Time: официальный доступ к нужным данным ещё не квалифицирован. Согласованный R1 не выпускается без pass или решения владельца об изменении scope.
- HealthKit bridge: бесплатная подпись/entitlements/фон проверяются, Watch не блокирует R1.
- Т-Банк: полный автоматический read-only доступ не подтверждён; R3 откладывается при недоступности, без ручной подмены.
- ИИ: конкретный quant/projector/manifest и 15s warm/cold, качество/диск/RAM требуют benchmark. Облако не включается скрыто.
- Локальный FoodCatalog: лицензированные данные должны быть квалифицированы до числового КБЖУ.
- Runtime tests, реальная установка/подпись и GitHub CI ещё не выполнены. Project/schema/model checks не подменяют приёмку приложения.

Это условия реализации/выпуска, для каждого определены pass/fail и поведение unavailable. Они сохранены в [реестре qualification](docs/integrations/qualification.md).

## Проверка пакета

`python3 tools/check_project.py`; `python3 tools/build_processes.py --check`; `python3 -m unittest discover -s tests/tools -v`; `node tools/validate_api.mjs` после установки tools dependencies. BPMN дополнительно проверен официальной OMG XSD локально. Схема выполняется в synthetic in-memory SQLite, no real profile/WAL. PlantUML отрендерен локально официальным JS-движком и связан с source/outputSHA manifest.

Политика публикации соблюдается независимо от видимости репозитория. Этот baseline не создаёт приложение/release и не публикует материалы автоматически.
