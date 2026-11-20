# Общая конфигурация

- dotnet/Directory.Build.props — общие параметры компиляции/анализа.
- dotnet/Directory.Packages.props — центральные версии NuGet.
- swift-format.json — общий профиль Swift formatter.

Корневые global.json и .editorconfig остаются в точках обнаружения инструментами. Root Directory.*.props — только MSBuild imports. GitHub требует workflows в .github/workflows; SwiftPM требует Package.swift в корне своего пакета. Реальные секреты и настройки конкретной установки здесь не размещаются.
