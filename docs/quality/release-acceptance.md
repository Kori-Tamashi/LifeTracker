# Приёмка, CI и измеримые цели baseline 1.0

## Нефункциональные цели

Все значения ниже — проектные gates, **не уже измеренные результаты**. Фиксируются workload, target Mac, build/machine/model manifests, cold/warm режим и synthetic dataset.

| Метрика | Gate |
| --- | --- |
| Current state API | p95≤100ms на 100000 synthetic observations, warm core |
| Сохранение оценки | p95≤250ms включая durable commit, без model inference |
| Cold open UI | p95≤2s до usable skeleton, core starting отображается отдельно |
| Core ready | p95≤5s без миграции/загрузки моделей |
| Manual input path | ≤30s и ≤3 основных действий в обычном сценарии |
| AI response | p95≤15s после минутного аудио отдельно warm и cold при установленных весах; иначе gate fail |
| Idle core | median≤1% одного CPU за 10min без jobs; UI закрыт |
| Core RAM | steady RSS≤512MiB без inference; UI≤512MiB |
| Runtime AI+app RAM | peak≤12GiB на qualified bundle, не весь macOS |
| Диск | native installed footprint+models+data+copies/cache≤10GB; допускается явно зафиксированный переход на 20GB |
| Backup/restore | RPO≤24h активной работы; restore≤10min на synthetic profile≤1GB |
| Автофинансы | успешное обновление не реже 5h активной работы при доступном qualified source |

Невыполнение critical gate не превращается в warning. Исправить либо оформить отклонение с owner decision. Никакие реальные разговоры/операции не нужны для CI.

## CI jobs после scaffolding

1. `project-checks`: ссылки/публикация, BPMN generation+graph, OpenAPI schema/reference validation, SQL design tests, requirement/test trace.
2. `core-fast`: .NET restore locked, format/analyzers, build, architecture/domain/application unit, changed contracts. Linux, timeout10min.
3. `core-integration`: реальный SQLite той же native версии, transaction/lease/outbox/disk/migration/restore tests. Linux, timeout15min.
4. `macos-client`: build SwiftUI, Swift Testing/XCTest, app smoke, synthetic XCUITest. macOS, timeout20min. Не квалифицирует Metal latency другого target.
5. `contract-e2e`: HTTP/Telegram simulators + generated client contract, installed local stack with fake inference. timeout15min.
6. `required-gate`: `if: always()`, проверяет нужные jobs по manifest этапа. failure/cancelled/неожиданный skipped — fail; только заранее документированный нерелевантный scope допускает skip.
7. `release-acceptance`: release/hotfix workflow, full suite этапа + isolated target Mac AI/energy/disk run, package install/upgrade, licence/SBOM/archive publication. Не запускается на произвольном fork PR.

Это целевая матрица полного R1. Фактически реализованы Project checks, Product checks (core-fast/macos-client/product-checks) и отдельный Beta release для [B0](../delivery/beta-plan.md). Ранний B0 не объявляет весь набор R1 gates пройденным. Добавление runnable product job обязательно в той же задаче, что первый соответствующий код. Нельзя писать `echo tests passed`, suppress errors или `continue-on-error` для обязательного сценария. Remote workflows выполнялись и проходили в PR; актуальное evidence — [оперативный реестр](../delivery/current-status.md). Branch protection учитывается отдельно в датированном [PM-01 audit](../delivery/ci-gate-audit.md); наличие CI не доказывает server-enforced запрет обхода.

## Покрытие и качество

Critical pure policies (balance, source selection, money/reserve, quota/effect) —100% branch coverage; Domain/Application минимум 90% lines/85% branches. Это targets, не счётчик «качества всей системы». Значимые мутации critical policy должны убиваться (min→max, TTL boundary, money sign, double effect); оставшиеся mutants разбираются до релиза. Property-based tests проверяют инварианты на генерации и фиксированном seed отчёта. Integration — реальный SQLite, не in-memory ORM provider; unit без network/DB/model.

AI gates — [ai-policy](../architecture/ai-policy.md). UI accessibility/navigation не заменяется screenshot-only тестом. Instability имеет issue/владельца, не маскируется retry. Новые ошибки получают regression case. Acceptance registry связывает **каждое** требование с проектом, релизом и planned тестами, но не заявляет execution до реализации.

## Инструменты и pinning

xUnit v3+ArchUnitNET, Swift Testing/XCTest, Python unittest для project tooling; product formatter/analyzers встроенные в .NET/Swift toolchain, лицензии сторонних пакетов проверяются. SDK 10.0.400 доступен на текущем build стенде; global.json exact SDK и NuGet lock формируются с первой сборкой после проверки security patch актуального LTS. Не объявлять произвольно выбранный пакет совместимым без restore/build. Для CI actions — pinned commit SHA, минимальные permissions, без выгрузки профиля/медийных данных.

## Release evidence

Хранить commit SHA, app/API/schema/rule/model versions, test suite totals с skipped/fail, coverage/mutation результаты, target benchmark, migration/restore report, capabilities manifest и known limitations. В Git только synthetic/aggregate отчёты и лицензированные артефакты; приватные профили не источник fixtures. Version tag создаётся только после evidence. R1 не проходит без решения Q-APPLE-ACTIVITY; R3 не проходит без Q-TBANK. Не отмечать весь продукт готовым по одному docs CI.
