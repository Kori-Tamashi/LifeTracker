# Переносимое ядро

.NET 10; точный SDK и test runner — в корневом `global.json`, версии NuGet — в `Directory.Packages.props`, транзитивные зависимости — в `packages.lock.json` каждого проекта. xUnit v3 работает через Microsoft.Testing.Platform, не через VSTest.

```sh
dotnet restore services/core/LifeTracker.slnx --locked-mode
dotnet format whitespace services/core/LifeTracker.slnx --no-restore --verify-no-changes
dotnet build services/core/LifeTracker.slnx --no-restore
dotnet test --solution services/core/LifeTracker.slnx --no-build --minimum-expected-tests 1
```

`src/Modules/State` содержит доменную политику, прикладные сценарии и контракты. `IStateRepository` — выходной порт; production-хранилище подключается в D-004. Test double существует только в тестовом проекте и не является доказательством корректности SQLite или конкурентных транзакций. Транспортные DTO и HTTP-маршруты подключаются в D-005.

Зависимости направлены внутрь: Application → Domain/Contracts/Abstractions; Domain → Kernel. Архитектурные тесты проверяют assembly dependencies и ArchUnitNET bytecode rules. Отдельная заведомо ошибочная fixture доказывает обнаружение запрещённой зависимости. Все тестовые данные синтетические.

Проверки реального адаптера, миграций, UI/E2E и release acceptance добавляются вместе с соответствующими компонентами; этот набор не объявляется полным тестированием будущего продукта.
