# Текущее исполнение

Контрольная дата: 24 ноября 2026 года (Москва). Проверенная база develop: `8df2757`, PR #21; кандидат beta.2 проходит отдельную подготовку. Это оперативный источник статусов и ближайшей очереди. [План](development-plan.md) определяет правила и WBS, [progress](progress.md) хранит историю, [baseline](baseline/implementation-plan.md) — исходный scope. Процент готовности всего продукта и дата R1 не рассчитаны.

## Состояние результатов

«Реализовано» означает наличие кода; «проверено» — конкретное автоматическое evidence; «принято» — выполнение критериев результата; «выпущено» — включение в main/tag. Выпуск B0 не закрывает приёмку полного R1. Pass дочернего технического результата не закрывает весь D-пакет.

| Результат | Реализация / проверка | Приёмка / выпуск | Остаток и следующий шаг |
| --- | --- | --- | --- |
| D-002/003: фундамент State | Domain/Application, архитектурные и контрактные тесты | Реализованный срез включён в B0 | Coverage/mutation evidence не заявлено |
| D-004/005: хранение и API | SQLite/revisions/receipts, реальные HTTP/restart проверки; [история](progress.md) | State-срез включён в B0 | Host policies jobs и full R1 quality/performance gates открыты |
| D-006: desktop | Персонаж, ввод, история, исправления, упаковка; Swift/live/package CI | B0 выпущен; M2/UI E2E не принят | Закрыть ручные сценарии и UI automation gap |
| D-007.1: owned runtime | Start/stop/menu/autostart implementation и process tests | В B0; реальный login/GUI/crash recovery не принят | Квалифицировать lifecycle, не менять power/login настройки без действия владельца |
| D-007.2.1: wake revalidation | [PR #15](https://github.com/Kori-Tamashi/LifeTracker/pull/15), автоматические wake/race cases | Только develop, не выпущено | Реальный sleep/wake, orphan/jobs/quota reconciliation открыты |
| D-009.1.1: offline backup | [PR #16](https://github.com/Kori-Tamashi/LifeTracker/pull/16), roundtrip/replay/locks/corruption smoke Linux/macOS | Технический CLI-срез проверен; только develop | [Операторская инструкция](../architecture/storage/offline-backup.md); UI и partial schedule/retention реализованы в следующих срезах; GUI/общий disk/recovery открыты |
| D-008: Telegram/уведомления | Не реализовано | Не принято и не выпущено | После ограниченного цикла стабилизации |
| D-001/010: Screen Time | Документальный spike; gate unqualified | R1 исходного объёма заблокирован этим gate | Владелец предоставляет provisioning prerequisites либо решает scope; собственного трекера нет |
| D-011…024 | Основные AI/финансовые/quest-модули не реализованы | Не приняты и не выпущены | Не начинать полный модуль до соответствующего qualification |

Последний проверенный PR #21: 139 .NET tests, 33 regular Swift + 6 live cases и 49 tooling tests в успешном CI. Это evidence проверенных сценариев, не процент готовности. Полная R1 [приёмка](../quality/release-acceptance.md), включая performance, coverage/mutation и target Mac measurements, остаётся открытой.

## B0 и обратная связь

Выпущен `v0.1.0-beta.1`, main SHA `791ddecc3b7dc70a98cb7917155801fbccfb8e16`. Установленная beta.1 не содержит wake/backup изменений из develop. Подтверждение владельца: приложение запускается, явных ошибок не замечено. 21 ноября владелец уточнил: сохранение после полного завершения, исправление в истории и повторное открытие окна через menu bar ещё не проверялись. Эти проверки имеют статус **not run**, не pass и не defect.

Полный сценарий: [первые 10 минут](../releases/v0.1.0-beta.1.md). В публичных отчётах фиксировать только обезличенный результат; не публиковать записи профиля. Реальный sleep/wake develop требует отдельно собранного тестового пакета; успешный запуск beta.1 не закрывает этот gate.

## Ближайший ограниченный цикл

Цель: закрывать хвосты приёмки и защиты данных до расширения Telegram. Это уточнение порядка работ внутри baseline, без уменьшения R1 scope, обещания даты или автоматического релиза beta.2.

| Порядок / ID | Проверяемый результат | Статус / исполнитель следующего действия | Критерий завершения |
| --- | --- | --- | --- |
| 1 — AC-B0 | Ручная проверка трёх существующих путей | Waiting: владелец, когда удобно; не блокирует независимую инженерию | Отдельный результат каждого сценария и defect при отказе |
| 2 — D-007.2.2 | Квалификация аварийного desktop/Core lifecycle | Дочерний D-007.2.2a: local/remote live pass, PR #18; desktop orphan остаётся открытым | Synthetic process regression для выбранного сбоя; сохранность pending/receipt; отдельно записанные непроверенные GUI/OS пути |
| 3 — D-009.1.2 | Доступный пользователю offline путь backup/restore | D-009.1.2a реализован; process adapter local pass; UI acceptance not run | Явная остановка writers, verify, новый профиль, ошибки без потери исходного; отдельная UI-проверка |
| 4 — D-009.1.3 | Автокопии/retention/disk policy | D-009.1.3a/b: startup/7 daily/local quota и owned Core polling реализованы; UI/OS/RPO и общий footprint открыты | Согласованная с baseline политика, low-space/отказ/restore evidence; декомпозиция до кода |
| 5 — AC-NEXT | Кандидат beta.2 для ручной проверки | Подготовка metadata/evidence/artifact; main/tag не публикуются | Зафиксированный scope/evidence/ограничения; release gates, без переноса старого тега |
| 6 — D-008.1 | Минимальный Telegram State путь | Backlog | Pairing/owner, read/write, retry/dedup после DoR |

Частично выполненные D-006/007/009 остаются на учёте; перевод в backlog не означает завершение. Один активный дочерний результат, один набор ожидания обратной связи AC-B0. Внешние gates Apple/branch protection учитываются как риски/действия, а не дополнительные запущенные задачи разработки.

## Контрольные проблемы

| Проблема | Действие / условие закрытия |
| --- | --- |
| Устаревающие статусы в нескольких документах | Текущие статусы обновлять здесь; progress только дополнять, baseline не переписывать |
| Нет истории фактических трудозатрат | [Учёт исполнения](execution-measurement.md) со следующего инженерного пакета; прошлое unknown |
| Coverage/mutation/target performance пока без полного evidence | Перед R1 выделить проверяемые пакеты качества; не считать число тестов заменой gate |
| Server-enforced checks не подтверждены заново | [PM-01 audit](ci-gate-audit.md) остаётся датированным снимком; CI pass не равен защите ветки |
| Apple gate может блокировать весь R1 | Повторять spike только при новых prerequisites; изменение scope решает владелец |

Обновлять после принятого результата, появления дефекта, изменения gate или смены версии. Прогноз пересматривать после пяти сопоставимых измеренных пакетов; до этого сроки и общий процент — unknown.

D-007.2.2a: добавлены live SIGKILL Core → failed → restart и replay pending/receipt без дубликата, а также stop attached → внешний host доступен. Локально и в macOS CI 4 live cases passed ([PR #18](https://github.com/Kori-Tamashi/LifeTracker/pull/18)). Это не аварийное завершение desktop и не тест незавершённой SQLite-транзакции. Подробности — [progress](progress.md).

D-009.1.2a: экран offline maintenance и process adapter добавлены; локально 5 live cases passed, включая roundtrip через Swift runner. Полная UI-приёмка, автокопии/retention/disk, desktop orphan и R1 остаются открытыми. Cleanup workflow из PR #18 успешно удалил завершённые remote ветки; после fetch/prune локально и удалённо остались main/develop (до открытия следующей задачи).

D-009.1.3a: [запусковые суточные копии](../architecture/storage/managed-daily-backup.md), 139 .NET cases local pass, включая deterministic Moscow schedule, rotation/restore, low-space/corruption и recovery незавершённой ротации. Startup UI/quit/progress не проверены вручную. RPO/RTO, pre-migration и общий disk gate не закрыты. PR #19 прошёл CI и cleanup, локальные/remote ветки убраны; UI acceptance остаётся not run.

D-009.1.3b: одна background задача tick/60s и wake, bounded retry, coordinated journal/Core pause; submit не прерывается, workspace сохраняется. Внешний Core не останавливается. Local real packaged-Core: 6 live cases pass, включая сохранность pending/workspace и restart после failed backup. GUI-form/real sleep/RPO, pre-migration, общий footprint и desktop orphan ещё открыты. PR #20 прошёл CI/cleanup; исходные ветки убраны, beta.1 остаётся установленной версией.

B1: [кандидат beta.2](../releases/v0.1.0-beta.2.md), техническая передача для ручной проверки. Release workflow расширен backup smoke и обязательными live cases; develop push release metadata выдаёт artifact без публикации. Настройки beta-версий разделены, beta.1 профиль не переключается. Ручные AC-B0/B1 остаются not run; beta.2 ещё не опубликован. PR #21 прошёл CI и branch cleanup.

Ручная проверка beta.2: владелец подтвердил сохранение после завершения/повторного запуска, исправление с revision 2 и закрытие/повторное открытие через menu bar. UI verify копии показал успех. Полный restore roundtrip ещё не отмечен принятым: после запуска показано предупреждение автокопии. Найден дефект B1-FINDER: обычный `.DS_Store` внутри managed каталога приводил к отказу. Исправление проверяется на синтетике; личные значения/снимки в Git не включаются.

## Приоритет бизнеса: 24 ноября

По явному указанию владельца основная инженерная очередь переключена с эксплуатационных доработок на бизнес-логику. Следующий этап по прежнему продуктовому приоритету — дневник. Открытые R1/backup/UI gates остаются в backlog и не выдаются за пройденные; release scope не сокращён. Не блокировать независимую Journal Domain/Application работу ожиданием этих gates.

D-012.1: [Journal review](../../src/core/Modules/Journal/README.md) — immutable proposals/decisions, тип/субъект/полярность, цитаты/версии источника, autoconfirm, stale/eligibility и review use case с idempotency/revision port. Локально 162 .NET tests pass (20 новых Journal cases); real repository, извлекатель, речь и UI ещё не подключены. Q-AI-TEXT сохраняется перед actual local inference/RAG. Дальнейшая очередь: real-store conversations/messages/review → qualification локального извлекателя → пользовательский дневник; Insights/питание отдельными срезами.
