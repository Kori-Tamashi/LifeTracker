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
      LifeTracker.Infrastructure.Shared/ # SQLite store и общие миграции
      LifeTracker.Host/             # local HTTP composition/transport
      Modules/State/
        LifeTracker.State.Domain/
        LifeTracker.State.Contracts/
        LifeTracker.State.Application/
        LifeTracker.State.Infrastructure/
      Modules/Operations/          # Domain/Application/Infrastructure
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

SwiftPM `Tests` остаётся внутри Swift package по его стандартному соглашению. Будущие iPhone/inference/host/канальные компоненты добавляются внутри `src` при реализации; общие миграции находятся в Infrastructure.Shared; модульные persistence adapters остаются в Infrastructure своего модуля. Transport contracts располагаются в Contracts или в проектной документации, а не в дополнительном корневом каталоге. Границы Clean Architecture определяются зависимостями сборок и тестами, а не названием верхнего каталога.

В корне из файлов остаются README.md и .gitignore. SDK/test runner — configs/dotnet/global.json; build/package properties — configs/dotnet; профиль Roslyn — configs/.editorconfig. tools/dotnet.sh выбирает cwd для SDK и экспортирует DirectoryBuildPropsPath/DirectoryPackagesPropsPath, поэтому автоматические корневые файлы не нужны. Все команды сборки/тестов/форматирования в CI и документации используют этот wrapper. EditorConfig обнаруживается через относительные ссылки src/.editorconfig и tests/.editorconfig; единственный файл настроек находится в configs. Нативный IDE при ручной работе должен наследовать те же MSBuild paths; обычный dotnet из корня не является поддерживаемым входом сборки.

AGENTS.md не отслеживается и не переносится в другой каталог репозитория. Правила архитектуры, тестирования и публикации остаются обычной документацией. GitHub workflows остаются в обязательном .github/workflows; языковые package manifests — внутри соответствующих пакетов. Baseline — docs/DESIGN.md.

Локальные private/local/var не отслеживаются: интервью, персональная конфигурация, данные, модели и резервные копии не являются исходниками. Runtime-root выбирается при установке, не выводится из cwd. Генерируемые bin/obj/.build/TestResults/node_modules исключены из Git. Наличие папки на локальном диске не делает её публикуемой.

`tools/check_project.py` проверяет допустимые корневые группы Git-visible файлов; перестановки требуют обновления ссылок, csproj/solution, tooling и CI в том же PR. Правила кода — [coding-standard.md](coding-standard.md).
