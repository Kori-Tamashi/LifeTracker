# План реализации baseline 1.0

Проектирование и реализация — разные стадии. Ниже задачи для GitHub backlog; issues/PR этим документом не создаются. Каждая задача — вертикальный или проверяемый технический результат, одна основная задача в работе.

| ID | Результат | Зависит от | Проверка / выпуск |
| --- | --- | --- | --- |
| D-001 | Квалификация источника Screen Time | Нет | Q-APPLE-ACTIVITY; при неуспехе решение владельца об R1 |
| D-002 | .NET/Swift scaffold, solution/module boundaries, version locks | Baseline | Build, import rules, forbidden dependency negative fixture |
| D-003 | Domain State + application scenarios + fake ports | D-002 | Полная матрица score/TTL/source/revision |
| D-004 | SQLite repositories/revisions/inbox/outbox, migrations | D-002 | Real-store contracts, constraints, transaction/fencing failures |
| D-005 | Local API v1, owner scope, idempotency | D-003/004 | OpenAPI contract + request/response validation |
| D-006 | macOS персонаж/history/quick assessment | D-005 | UI/E2E synthetic first-run/unknown/correction |
| D-007 | Lifecycle/menu/autostart/stop/wake | D-005/006 | Sleep/restart/controlled shutdown, no data loss |
| D-008 | Telegram R1 и notifications | D-005/007 | Pairing, quota/quiet/dedup, unknown update handling |
| D-009 | Backup/restore/export/delete/disk policy | D-004/007 | Snapshot restore, migration failure, low space |
| D-010 | Activity adapter при qualified source | D-001/005 | Data freshness/unknown, no silent tracker substitution |
| R1 | Персонаж и основные пользовательские пути | D-001…010 | Все R1 gates; редкий release, main tag v0.1.0 |
| D-011 | Local inference bundle qualification | D-002 | Q-AI-TEXT, warm/cold latency/quality/disk report |
| D-012 | Journal/extraction/evidence/people/revisions | D-004/011 | Type/negation/dedup/confirmation/stale tests |
| D-013 | FTS5/RAG/interpretation reports | D-012 | recall/precision, evidence/correction corpus |
| D-014 | Voice UI/Telegram + diary views | D-006/012/013 | End-to-end accepted→processed→recorded |
| D-015 | Nutrition and photo + food dataset qualification | D-011/012 | Q-AI-VISION/Q-FOOD-DATA, bounded questions/unknown |
| D-016 | Optional iPhone Health bridge | D-005 | Q-APPLE-HEALTH; independent of R1 release |
| R2 | Дневник, память, питание | D-011…015 | Full AI/restore/UI gates; tag v0.2.0 |
| D-017 | Banking adapter qualification | Нет | Q-TBANK; если blocked, продолжить R4 после R2 |
| D-018 | Ledger/accounts/import reconciliation | D-017/004 | Double entry, transfers/pending/refund/credit tests |
| D-019 | Budget/reserves/closing/restatement | D-018 | Financial invariant/property fixtures |
| D-020 | Portfolio/cash flow/corporate actions/taxes/calculations | D-018 | Quote/cost coverage, TWR/Dietz/XIRR negative cases |
| D-021 | Finance UI/Telegram | D-019/020 | Plan/actual/approval/explanation E2E |
| R3 | Автоматический финансовый модуль | D-017…021 | Полный финансовый набор, tag v0.3.0 |
| D-022 | Quest rule engine/assignment/adaptation | R2 | No medical/purchase side effects, caps/recovery/no-data |
| D-023 | Outcome/effects/reversal/rebuild | D-022/004 | Duplicate/late/corrected outcome and series tests |
| D-024 | RPG progression/quests UI+bot | D-022/023 | Autonomous assignment, explicit source/explanation |
| R4 | Квесты и геймификация | D-022…024 | Full invariants/UI/restore; tag v0.4.0 |

Если R3 отложен, номер версии не используется для ложного заявления готовности финансов: feature/capability manifest перечисляет реально выпущенное, следующая версия выбирается по порядку выпуска, а не механически по строке таблицы. До 1.0 несовместимое изменение API/schema всё равно требует миграции и заметок, не оправдывается «это 0.x».

## GitFlow и gates

Feature `feature/<D-id>-<slug>` от develop, PR только после unit/architecture/contract checks; release/<version> после завершения этапа и полной приёмки; main tag указывает проверенный SHA; merge-back в develop. Hotfix/<version> от main с критическим regression test, затем в main/develop/активную release. ИИ может сливать и выпускать по авторизации пользователя, но не обходить gates. Branch protection требуется при первом CI запуске; пока удалённые настройки не изменены.

PR DoD: linked requirements/cases; версионированные контракт/миграция; synthetic fixtures; отсутствие личных данных; зелёные jobs без скрытых skips. Release DoD: все тесты этапа, package install/update, restore, manifest/license, target Mac acceptance, известные ограничения, changelog. Календарных частых релизов нет. Не создавать релиз только из-за изменения документации.
