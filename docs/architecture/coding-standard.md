# Единый стандарт кода

Общие правила: UTF-8, LF, пробелы; выразительные имена на английском; комментарий объясняет причину; пользовательский текст интерфейса — русский. Генерируемые артефакты не форматируются вручную. Не добавлять пустые слои и универсальные репозитории; бизнес-правила остаются в Domain, сценарии/порты в Application, SQL/EF и системные API — в Infrastructure. Конструкторы получают зависимости явно; глобальный mutable service locator запрещён.

## Автоматически проверяется

- `configs/.editorconfig`: единый профиль, подключённый относительными ссылками в src/tests (четыре пробела для C#/Swift/Python, два для JSON/YAML/JS/XML; LF, braces и namespace для C#). Swift дополнительно использует свой native formatter. Корневого .editorconfig нет.
- C#: file-scoped namespace, явные braces, Nullable, warnings-as-errors и EnforceCodeStyleInBuild. `dotnet format` проверяет whitespace/style/analyzers; architecture tests запрещают зависимости внутренних слоёв от Infrastructure. Числа денег/времени хранятся согласно контрактам, без неявного перевода в float.
- Swift: профиль configs/swift-format.json и `swift-format lint --strict` в macOS CI. Используется форматтер установленного Apple toolchain; его обновление требует осознанной проверки форматирования. UI не повторяет вычисления бизнес-правил ядра.
- Python/JS/SQL tooling следует соглашениям существующих файлов и проходит поведенческие проверки; автоматический formatter/linter всего tooling пока не подключён. Общая EditorConfig не выдаётся за статический анализ всех языков.

```sh
sh tools/dotnet.sh format --no-restore --verify-no-changes
sh tools/format_swift.sh lint
```

Для исправления: та же dotnet-команда без verify flag; `sh tools/format_swift.sh format`. NuGet версии — configs/dotnet/Directory.Packages.props, транзитивные версии — checked-in lock files. tools/dotnet.sh задаёт явные пути MSBuild для src и tests; корневых imports нет.

## Проверяется при review и тестировании

Async use cases получают CancellationToken; синхронные транзакционные участки SQLite не обещают немедленную отмену во время busy wait. Изменение команды атомарно с receipt; повтор команды не создаёт новые эффекты. Исправления добавляют ревизии; не переписывают прошлые snapshot. SQL использует параметры; техническая ошибка не включает содержимое личных записей. Ресурсы имеют явного владельца и Dispose/rollback path.

Fake не является production fallback. Общий repository contract suite запускается для fake и SQLite; реальные tests дополнительно проверяют ограничения, сбои, rollback, restart и конкуренцию. Тест описывает наблюдаемое правило/отказ; количество тестов само по себе не доказывает полноту. Test fixtures исключительно синтетические. Publication policy применяется к кодам ошибок, snapshot и тестовым отчётам.
