# Трассировка требований и проекта

> Baseline 1.0: нормативные значения и закрытые решения — [BR-01–10](../requirements/baseline-rules.md). Этот документ — вспомогательное представление; прежние пометки «предложено/уточнить» не отменяют зафиксированный baseline. Реализуемость интеграций подтверждается отдельными gates.

Статус: архитектурная трассировка, не отчёт о пройденных тестах. Имена будущих тестовых наборов задают задачи, не ссылаются на существующий код.

| Требования | Владелец и проект | Диаграмма | План проверки |
| --- | --- | --- | --- |
| FR-STA-001–008 | State: ObservationRevision, BalancePolicy | uml-domain / uml-record-assessment | AC-001–005, boundary/property unit |
| FR-CHR-001–003 | Character read model + macOS UI | c4-containers / uml-domain | UI и объяснения; unknown при первом запуске |
| FR-DIA-001–005 | Journal, InferenceGateway, jobs | uml-voice-processing | Extraction contracts, AI corpus, acceptance |
| FR-DIA-006–009 | Insights/Evidence/revisions | uml-domain | AC-006/007, correction и stale retrieval |
| FR-DIA-010 | Journal queries / UI | c4-components | Согласовать виды и E2E просмотра |
| FR-FOOD-001–003 | Nutrition + photo adapter | c4-context | Portion/unknown fixtures, AI eval; этап открыт |
| FR-FIN-001–006 | Financial adapter / Finance | c4-context | SP-06, dedup/transfer contracts; R3 gated |
| FR-BUD-001–008 | MonthlyBudget / Finance | c4-components (future) | AC-012/013, финансовые формулы ещё открыты |
| FR-CRD-001, FR-INV-001 | Finance / Investments | c4-components (future) | Real-adapter contracts после SP-06; формулы отдельным проектированием |
| FR-QST-001–005 | Quests + Character effects | uml-quest-state | State machine, compensation, no-data/illness tests |
| FR-UI-001, FR-BOT-001 | Client / channel adapters | c4-containers / uml-use-cases | Channel contract + UI/E2E |
| FR-LIFE-001 | Lifecycle / Operations | uml-job-state | AC-009/010, sleep/restart/stop |
| FR-NOT-001 | Notifications + deliveries | c4-components | AC-008, timezone/quota/dedup |
| NFR-001–007 | Runtime, Operations, publication | c4-containers / uml-ingestion | AC-010/011/014, SP-03–05, isolation/restore |
| Clean Architecture / SOLID | Layer ports + composition root | uml-clean-architecture | Import graph, port substitution contracts |
| Full testing / CI | Quality gates | ADR-004 | Docs gate сейчас; product gates с первой вертикалью |

Источники диаграмм — в [реестре](diagrams/README.md). Детальная финансовая class/ER-модель, формулы квестов, OpenAPI и физическая SQL-схема не созданы: преждевременная детализация скрыла бы открытые решения ТЗ. Следующие итерации расширяют эту таблицу до «требование → сценарий → тестовый файл/ID → результат».


## Алгоритмы и BPMN

| Требования | Алгоритмы | Процессы |
| --- | --- | --- |
| FR-STA-001–008, FR-CHR | ALG-01/04 | BP01 |
| FR-DIA-001–010 | ALG-02–05 | BP02/03 |
| FR-FIN/BUD/CRD/INV | ALG-06/11 | BP04 (планирование/обзор; инвестиции требуют отдельной модели) |
| FR-QST-001–005 | ALG-07 | BP05 |
| FR-NOT-001 | ALG-08 | BP06 |
| FR-FOOD-001–003 | ALG-09 | BP08 |
| NFR-004/005 | ALG-02/10 | BP02/07 |

Артефакты: [алгоритмы](algorithms/README.md), [BPMN](../processes/README.md), [компоненты](../research/component-comparison.md). Проектные числа не меняют статус исходного ТЗ без явного решения.

Baseline дополнение: BP09 закрытие месяца, BP10 импорт, BP11 портфель. Машиночитаемая полная трассировка 59 требований/43plannedcases — [registry](../requirements/registry.json) и [cases](../quality/acceptance-cases.json).
