# Life Tracker

Личное приложение для контроля расходов, наблюдения за состоянием и отслеживания привычек. Интерфейсы — macOS и Telegram с поэтапным вводом функций. Расширение на других пользователей возможно в будущем.

**Стадия:** проектный baseline1.0 завершён; [вход в пакет проектирования](docs/DESIGN.md). Начата реализация; [текущее состояние и очередь](docs/delivery/current-status.md). Реализованы [локальный State API](src/core/LifeTracker.Host/README.md) и [macOS-клиент](src/macos/README.md): персонаж, ввод, история и исправления оценок, локальная упаковка `.app`, управление собственным сервисом и menu bar. Визуальная приёмка и M2 остаются открытыми. Qualification интеграций ещё не выполнена.

## Первая тестовая версия

Тестовая линия — `0.1.0-beta.1`: [установка и первые 10 минут](docs/releases/v0.1.0-beta.1.md), [границы B0](docs/delivery/beta-plan.md). Архив, контрольные суммы и evidence публикуются в [GitHub prerelease](https://github.com/Kori-Tamashi/LifeTracker/releases/tag/v0.1.0-beta.1) только после main release checks. Docker доступен как отдельное размещение в будущем; эта бета использует native Core.

В develop подготовлен [кандидат beta.2](docs/releases/v0.1.0-beta.2.md) с wake/backup и отдельными настройками профиля. Это кандидат для ручной проверки; ссылка выше остаётся опубликованным beta.1 release.

## Документация

- [Индекс и правила документации](docs/README.md)
- [Концепция и границы](docs/product/vision.md)
- [Открытые вопросы](docs/product/discovery.md)
- [ТЗ baseline1.0](docs/requirements/specification.md) — требования, этапы, приёмка и открытые решения
- [Архитектура и диаграммы](docs/architecture/overview.md)
- [Структура репозитория](docs/architecture/repository-layout.md) и [стандарт кода](docs/architecture/coding-standard.md)
- [Исследование интеграций](docs/integrations/catalog.md)
- [План разработки: вехи, задачи, риски и контроль](docs/delivery/development-plan.md)
- [План проектирования](docs/delivery/design-plan.md)
- [Алгоритмы](docs/architecture/algorithms/README.md), [BPMN-процессы](docs/processes/README.md) и [сравнение компонентов](docs/research/component-comparison.md)
- [Clean Architecture и SOLID](docs/architecture/clean-architecture.md)
- [Тестирование](docs/delivery/testing-strategy.md) и [CI](docs/delivery/ci-design.md)

Основные правила, модули, DDL/API, алгоритмы/BPMN, интерфейсы и план реализации зафиксированы. Фактический доступ Apple/банка и ресурсные/AI gates остаются проверками до допуска соответствующих релизов.

Правила размещения материалов: [политика публикации](docs/delivery/publication-policy.md). Личные данные и исходные интервью хранятся локально вне Git.

Локальные проверки проектных материалов: `python3 tools/check_project.py` и `python3 -m unittest discover -s tests/tools -v`. Продуктовые тесты запускаются отдельно: [ядро](src/core/README.md), [Swift-клиент](src/macos/README.md).
