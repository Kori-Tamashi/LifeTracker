# Правила работы с проектом

Перед коммитом, PR, публикацией или релизом прочитать `docs/delivery/publication-policy.md` и проверить полный staged diff. Репозиторий рассматривается как публичный независимо от видимости.

Не публиковать содержимое `private/`, `local/`, `var/`, исходные интервью, реальные финансовые/медицинские/поведенческие данные, секреты и персональные идентификаторы. Не копировать эти сведения в PR, issues, логи и сообщения коммитов. Для тестов и демонстраций использовать синтетические данные.

Персональная история исследования хранится только локально. В документации описывать требования к продукту без привязки функций к биографии владельца. Не использовать `git add -f` для обхода исключений.

Проектирование baseline1.0 завершено и опубликовано; пользователь разрешил начать разработку. Приняты полный GitFlow и поэтапные редкие релизы; см. `docs/delivery/repository-workflow.md`.

Пользователь требует Clean Architecture, SOLID, полное тестирование и CI. Соблюдать `docs/architecture/clean-architecture.md`, `docs/delivery/testing-strategy.md` и `docs/delivery/ci-design.md`. Нормативный baseline1.0 зафиксирован в docs/DESIGN.md и ADR-006; вспомогательные черновые пометки заменяются BR-01–10. Не выдавать планируемые тесты за пройденные. Baseline опубликован через PR #1; реализация ведётся отдельными feature-ветками от develop.

Базовые компоненты выбраны проектировщиком в ADR-005 по поручению пользователя сравнить варианты; выбор не означает выполненных benchmark/API-проверок. Алгоритмы — `docs/architecture/algorithms/README.md`, процессы — `docs/processes/README.md`. Числовые бизнес-правила зафиксированы проектировщиком в BR-01–10. BPMN/SVG генерируются из `docs/processes/catalog.json`; перед завершением изменений запускать `python3 tools/build_processes.py --check` и `python3 -m unittest discover -s tests/tools -v`.

Начата реализация State и Swift-клиентского каркаса; текущее состояние — docs/delivery/progress.md. Квалификационные gates не объявлять выполненными. Проверки: установить tools/requirements-design.txt, npm ci --prefix tools --ignore-scripts; затем project checker, unittest, BPMN --check и node tools/validate_api.mjs. Продуктовые проверки: команды в src/core/README.md и src/macos/README.md. Рендер UML/C4/ER локальный через tools/render_diagrams.mjs; source/output manifest обязателен после изменения .puml.

Код — src/core и src/macos; общие конфиги — configs; .NET tests — tests/core. Соблюдать docs/architecture/repository-layout.md и coding-standard.md. C# проверяется полной dotnet format, Swift — sh tools/format_swift.sh lint; оба gates обязательны в CI. Не возвращать apps/services в корень.
