# CI и выпуск

> Baseline 1.0: нормативные значения и закрытые решения — [BR-01–10](../requirements/baseline-rules.md). Этот документ — вспомогательное представление; прежние пометки «предложено/уточнить» не отменяют зафиксированный baseline. Реализуемость интеграций подтверждается отдельными gates.

Статус: начата реализация. Project checks опубликован и прошёл в PR #1. Product checks добавляет Linux build/analyzers/format/domain/application/architecture и macOS Swift tests; release/E2E/persistence gates подключаются с соответствующим кодом.

## Pipeline

| Gate | Когда | Что блокирует |
| --- | --- | --- |
| project-checks | PR и push | Неработающие локальные ссылки, неверная оболочка схем, запрещённые файлы в Git, явные персональные пути; целостность графов BPMN и синхронность XML/SVG с каталогом |
| architecture + format + static + unit | После появления кода, каждый PR | Нарушение слоёв, анализаторы, доменные/прикладные ошибки |
| contracts + persistence integration | PR с кодом и интеграция develop | Несовместимость адаптеров, транзакции/очередь/миграции |
| build + E2E smoke | PR с готовой вертикалью и develop | Неустанавливаемая сборка, основной путь пользователя |
| full-release-acceptance | release/hotfix перед main | Полный набор этапа, восстановление, UI на macOS, локальный AI benchmark, лицензии и состав архива |

Окончательный required check имеет стабильное имя и проверяет результаты всех нужных jobs; он не должен быть зелёным, если критическая job пропущена или отменена. После появления кода gates добавляются в workflow вместе с первой вертикалью, а не после разработки всего продукта. Не делать фиктивные jobs `echo tests passed`.

Linux подходит для переносимого ядра и контейнерных интеграций; macOS — для сборки/интерфейса/системных API. Целевой Apple Silicon Mac необходим для итоговых замеров моделей; hosted runner другой архитектуры не доказывает целевую производительность. Бесплатные лимиты и доступность runner уточняются перед включением; платные ресурсы не активируются.

Для обычных PR использовать синтетические данные и inference fake; реальная локальная модель — отдельный контролируемый прогон до R2 и при её изменениях. Нельзя подключать публичные PR к self-hosted runner с реальным профилем или ключами. Выделенный чистый локальный стенд запускается только для проверенного кода, без mounted private/var пользователя.

## Workflow сейчас

`.github/workflows/project-checks.yml` вызывает `python3 tools/check_project.py` и регрессионные тесты проверяющего инструмента (`python3 -m unittest discover -s tests/tools -v`). Checkout зафиксирован SHA официального action; права `contents: read`, без сохранения credentials, с таймаутом. Workflow не публикует файлы и не делает push. Тесты продукта в нём не заявлены. Скрипт проверяет текущие Git-visible файлы; он не читает private/var и не является полноценным secret scanner.

При выборе стека добавить специализированный secret scanner, formatter, dependency/license checks, архитектурные и тестовые инструменты с фиксированными версиями. Секреты банка/бота не нужны для тестов PR. CI не собирает личные логи и не отправляет код/профиль внешнему ИИ.

## GitFlow

Feature → develop после checks. Release отделяется для стабилизации целого этапа; main получает только проверенный релиз с тегом и заметками. Hotfix от main проходит обязательные проверки затронутой области и сквозной smoke, затем возвращается в develop/активную release. ИИ уполномочен выполнять слияния/релизы, но отсутствие зелёных проверок блокирует действие.

GitHub branch protection/rulesets и required checks должны быть настроены после первого успешного запуска workflow. Удалённые rulesets пока не настроены; успешные запуски и незакрытые gates фиксируются в progress.md. CI и автоматическая публикация — отдельные процессы; первый не выпускает новую версию при каждом push.

## Основания

В учебном примере просмотрены категории Unit/Integration/E2E, сборочные jobs и запуск Docker-стенда. Для Life Tracker тестовая среда создаётся независимо в каждой job: `needs` задаёт порядок, но не переносит контейнеры между hosted jobs. Общие артефакты передаются явно либо стенд поднимается заново.

Синтаксис, события и права сверены с [официальной документацией GitHub Actions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax); checkout — [официальный репозиторий](https://github.com/actions/checkout). Дата проверки: 19 ноября 2026 года. Фиксация SHA требует регулярного контролируемого обновления.

Стек продуктового CI выбран в [ADR-005](../architecture/decisions/ADR-005-component-selection.md). Дополнительно текущий workflow проверяет `python3 tools/build_processes.py --check`; XSD OMG валидирована локально, автоматический CI сейчас проверяет модель/ссылки/DI без сетевой загрузки XSD.

Текущий продуктовый workflow: `.github/workflows/product-checks.yml`. Stable gate `product-checks` требует успешного завершения обеих jobs, включая случай skip/cancel. Используются стандартные hosted runners без платных large/xlarge labels. Dependencies восстанавливаются в locked mode; actions закреплены SHA. Microsoft.Testing.Platform выбран по [документации .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test).
