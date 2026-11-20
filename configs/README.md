# Общая конфигурация

- dotnet/global.json — SDK и Microsoft.Testing.Platform runner.
- dotnet/Directory.Build.props — общая компиляция/анализ.
- dotnet/Directory.Packages.props — центральные версии NuGet.
- .editorconfig — единый профиль редактора/форматтера; src/tests содержат относительные ссылки на него.
- macos/Info.plist — идентификатор и метаданные локального desktop bundle.
- swift-format.json — профиль Swift formatter.
- api/routes.json — генерируемый перечень маршрутов OpenAPI.

Вход для .NET CLI: `sh tools/dotnet.sh {restore|build|format|test|run|publish-host} [options]`. Wrapper запускает CLI из каталога SDK config и передаёт MSBuild paths через окружение. Он работает независимо от исходного cwd. Корневые props/global.json/.editorconfig не используются; AGENTS.md не публикуется. Реальные секреты и настройки установки здесь не размещаются.
