# Life Tracker — проектный baseline 1.0

20 ноября 2026 года. **Основное проектирование завершено как документированный baseline для реализации.** Реальный доступ к интеграциям и эксплуатационные характеристики проверяются отдельными gates, а не предполагаются выполненными. Baseline опубликован в develop через PR #1; начата реализация. Статус кода и проверок — [журнал разработки](delivery/progress.md).

## Пакет

| Область | Нормативный результат |
| --- | --- |
| Требования | [ТЗ 1.0](requirements/specification.md), [59 требований](requirements/registry.json) |
| Бизнес-правила | [BR-01–10](requirements/baseline-rules.md), [финансы/портфель](requirements/finance-design.md) |
| Архитектура | [Обзор](architecture/overview.md), [Clean Architecture/SOLID](architecture/clean-architecture.md), [классы/модули](architecture/module-implementation.md) |
| Алгоритмы и процессы | [ALG-01–11](architecture/algorithms/README.md), [11 BPMN](processes/README.md) |
| Данные | [Физическая схема](architecture/storage/README.md), [DDL41 таблица](architecture/storage/schema-v1.sql) |
| API | [OpenAPI51 операция](architecture/contracts/openapi.yaml), [семантика](architecture/contracts/application-api.md) |
| Диаграммы | [16 C4/UML/ER/deployment](architecture/diagrams/README.md), SVG и source hashes |
| Интерфейсы | [macOS screens и Telegram](ux/interface-specification.md) |
| ИИ | [Local policy/corpus/qualification](architecture/ai-policy.md) |
| Эксплуатация | [Deployment/lifecycle](architecture/deployment.md), backup/retention/recovery |
| Качество | [43 planned cases](quality/acceptance-cases.json), [CI/quality gates](quality/release-acceptance.md) |
| Решения | [ADR](architecture/decisions/README.md), [ADR-006 freeze](architecture/decisions/ADR-006-design-baseline.md) |
| Реализация | [24 задачи и 4 этапа](delivery/baseline/implementation-plan.md), GitFlow |

## Что зафиксировано

C#/.NET10 core с чистыми слоями, SwiftUI/AppKit, SQLite≥3.51.3, единый writer, persisted jobs/inbox/outbox, FTS5, native llama.cpp/whisper.cpp, HTTP/JSON/SSE и Bot API long polling. Native default, Docker optional после storage/footprint gate. Самооценки/ИИ-предположения/игровые атрибуты раздельны. Все основные бизнес-формулы и дефолты определены BR; новые изменения проходят versioned ADR.

## Что не объявлено доказанным

- Screen Time: официальный доступ к нужным данным ещё не квалифицирован. Согласованный R1 не выпускается без pass или решения владельца об изменении scope.
- HealthKit bridge: бесплатная подпись/entitlements/фон проверяются, Watch не блокирует R1.
- Т-Банк: полный автоматический read-only доступ не подтверждён; R3 откладывается при недоступности, без ручной подмены.
- ИИ: конкретный quant/projector/manifest и 15s warm/cold, качество/диск/RAM требуют benchmark. Облако не включается скрыто.
- Локальный FoodCatalog: лицензированные данные должны быть квалифицированы до числового КБЖУ.
- Runtime квалификация, реальная установка/подпись и release acceptance ещё не выполнены; модульные проверки и GitHub CI отражаются в журнале разработки. Project/schema/model checks не подменяют приёмку приложения.

Это условия реализации/выпуска, для каждого определены pass/fail и поведение unavailable. Они сохранены в [реестре qualification](integrations/qualification.md).

## Проверка пакета

`python3 tools/check_project.py`; `python3 tools/build_processes.py --check`; `python3 -m unittest discover -s tests/tools -v`; `node tools/validate_api.mjs` после установки tools dependencies. BPMN дополнительно проверен официальной OMG XSD локально. Схема выполняется в synthetic in-memory SQLite, no real profile/WAL. PlantUML отрендерен локально официальным JS-движком и связан с source/outputSHA manifest.

Политика публикации соблюдается независимо от видимости репозитория. Этот baseline не создаёт приложение/release и не публикует материалы автоматически.
