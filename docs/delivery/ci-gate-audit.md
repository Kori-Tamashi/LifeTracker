# PM-01: аудит серверных gates

20 ноября 2026 года. Публичный GitHub REST read для `branches/main`, `branches/develop`, `rulesets` показал `protected=false` у обеих веток и пустой список rulesets. Эти значения — снимок на момент проверки, не постоянное свойство репозитория.

Project checks и Product checks существуют и прошли в PR #2/#3. Итоговый product-checks требует успеха core-fast и macos-client. Проверка workflow не равна branch protection: обход merge до gates сейчас не запрещён серверными правилами.

Доступный GitHub connector позволяет работать с Git objects/PR/Actions, но не предоставляет методов управления rulesets/branch protection. Удалённые настройки этим пакетом не изменены. PM-01 завершён как аудит; обеспечение server-enforced gates остаётся открытым операционным действием. Перед каждым merge ИИ проверяет CI на точном head SHA и не обходит красные/пропущенные jobs.

## Конкретная конфигурация для применения

- Target branches: `main` и `develop`; enforcement active; PR required, force-push/deletion запрещены.
- Required check contexts после сверки имён реальных check runs: `project-checks` и `product-checks`, источник GitHub Actions.
- Требовать актуальность относительно base перед merge; независимый approved review не требовать для единственного исполнителя, чтобы не создать невозможный процесс.
- Применение и возможные bypass actors проверяются отдельным authenticated read после сохранения правила. До этого нельзя писать «ветки защищены».
- Для main сохраняются release/hotfix scope и full-release acceptance; два текущих checks не заменяют будущую приёмку релиза.

Источник интерфейса проверки: [GitHub branches REST API](https://docs.github.com/en/rest/branches/branches), [repository rulesets API](https://docs.github.com/en/rest/repos/rules). Проверка не использовала и не публиковала секреты или содержимое пользовательского профиля.
