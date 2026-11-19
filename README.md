# Life Tracker

Личное приложение для контроля расходов, наблюдения за состоянием и отслеживания привычек. Интерфейсы — macOS и Telegram с поэтапным вводом функций. Расширение на других пользователей возможно в будущем.

**Стадия:** проектный baseline1.0 завершён; [вход в пакет проектирования](DESIGN.md). Реализация продукта и qualification интеграций ещё не выполнены.

## Документация

- [Индекс и правила документации](docs/README.md)
- [Концепция и границы](docs/product/vision.md)
- [Открытые вопросы](docs/product/discovery.md)
- [ТЗ baseline1.0](docs/requirements/specification.md) — требования, этапы, приёмка и открытые решения
- [Архитектура и диаграммы](docs/architecture/overview.md)
- [Исследование интеграций](docs/integrations/catalog.md)
- [План проектирования](docs/delivery/design-plan.md)
- [Алгоритмы](docs/architecture/algorithms/README.md), [BPMN-процессы](docs/processes/README.md) и [сравнение компонентов](docs/research/component-comparison.md)
- [Clean Architecture и SOLID](docs/architecture/clean-architecture.md)
- [Тестирование](docs/delivery/testing-strategy.md) и [CI](docs/delivery/ci-design.md)

Основные правила, модули, DDL/API, алгоритмы/BPMN, интерфейсы и план реализации зафиксированы. Фактический доступ Apple/банка и ресурсные/AI gates остаются проверками до допуска соответствующих релизов.

Правила размещения материалов: [политика публикации](docs/delivery/publication-policy.md). Личные данные и исходные интервью хранятся локально вне Git.

Локальные проверки проектных материалов: `python3 tools/check_project.py` и `python3 -m unittest discover -s tests/tools -v`. Эти команды не запускают тесты ещё не реализованного приложения.
