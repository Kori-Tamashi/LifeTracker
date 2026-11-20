# Структура репозитория

Рабочая структура принята при реализации D-004.2. Каталоги создаются только с реальными файлами; отдельные верхнеуровневые `apps`, `services`, `adapters`, `contracts`, `migrations`, `infra` не нужны.

```text
LifeTracker/
  docs/                         # ТЗ, baseline, ADR, диаграммы, схемы, исследования, процесс
  src/
    core/
      LifeTracker.slnx
      LifeTracker.Kernel/
      LifeTracker.Application.Abstractions/
      Modules/State/
        LifeTracker.State.Domain/
        LifeTracker.State.Contracts/
        LifeTracker.State.Application/
        LifeTracker.State.Infrastructure/Migrations/
    macos/
      Package.swift
      Sources/LifeTrackerClient/
      Tests/LifeTrackerClientTests/
  configs/
    dotnet/                     # общие build/package properties
    swift-format.json           # полный профиль форматирования Swift
  tests/
    core/                       # .NET unit, architecture, integration и shared contracts
    tools/                      # проверки проектных материалов
  tools/                        # скрипты сборки/форматирования/проверок и их package manifests
  .github/workflows/            # обязательное место GitHub Actions
```

SwiftPM `Tests` остаётся внутри Swift package по его стандартному соглашению. Будущие iPhone/inference/host/канальные компоненты добавляются внутри `src` при реализации; миграции принадлежат Infrastructure своего модуля. Transport contracts располагаются в Contracts или в проектной документации, а не в дополнительном корневом каталоге. Границы Clean Architecture определяются зависимостями сборок и тестами, а не названием верхнего каталога.

`global.json`, `.editorconfig`, `.gitignore`, `Directory.Build.props`, `Directory.Packages.props` — корневые точки автоматического обнаружения инструментами. Два MSBuild-файла содержат только import настроек из configs/dotnet. README и AGENTS остаются корневыми входами для пользователя и агента. Baseline перенесён в docs/DESIGN.md. Настройки запуска/упаковки при появлении относятся к configs, исполняемый tooling — к tools.

Локальные private/local/var не отслеживаются: интервью, персональная конфигурация, данные, модели и резервные копии не являются исходниками. Runtime-root выбирается при установке, не выводится из cwd. Генерируемые bin/obj/.build/TestResults/node_modules исключены из Git. Наличие папки на локальном диске не делает её публикуемой.

`tools/check_project.py` проверяет допустимые корневые группы Git-visible файлов; перестановки требуют обновления ссылок, csproj/solution, tooling и CI в том же PR. Правила кода — [coding-standard.md](coding-standard.md).
