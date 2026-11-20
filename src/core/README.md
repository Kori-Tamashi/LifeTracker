# Переносимое ядро

.NET 10; точный SDK и test runner — в корневом `global.json`, версии NuGet — в `configs/dotnet/Directory.Packages.props`, транзитивные зависимости — в `packages.lock.json` каждого проекта. xUnit v3 работает через Microsoft.Testing.Platform, не через VSTest.

```sh
dotnet restore src/core/LifeTracker.slnx --locked-mode
dotnet format src/core/LifeTracker.slnx --no-restore --verify-no-changes
dotnet build src/core/LifeTracker.slnx --no-restore
dotnet test --solution src/core/LifeTracker.slnx --no-build --minimum-expected-tests 1
```

`src/Modules/State` содержит доменную политику, прикладные сценарии и контракты. `IStateRepository` — выходной порт; bootstrap новой SQLite-базы реализован в D-004.1; production repository реализован в D-004.2/004.3. Test double существует только в тестовом проекте; общий контракт запускается также на реальном SQLite. Транспортные DTO и HTTP-маршруты подключаются в D-005.

Зависимости направлены внутрь: Application → Domain/Contracts/Abstractions; Domain → Kernel. Архитектурные тесты проверяют assembly dependencies и ArchUnitNET bytecode rules. Отдельная заведомо ошибочная fixture доказывает обнаружение запрещённой зависимости. Все тестовые данные синтетические.

Проверки реального адаптера, миграций, UI/E2E и release acceptance добавляются вместе с соответствующими компонентами; этот набор не объявляется полным тестированием будущего продукта.

D-004.1: [граница bootstrap, миграции и проверки](../../docs/architecture/storage/bootstrap.md). Create не перезаписывает существующую базу; OpenExisting проверяет точную известную схему. Upgrade и recovery ещё не реализованы.

D-004.4: [durable inbox/jobs/outbox](../../docs/architecture/storage/durable-jobs.md). Общий SqliteStore/CoreDbContext — Infrastructure.Shared; State и Operations adapters независимы.
