# Диаграммы baseline 1.0

16 исходников PlantUML и локально отрендеренных SVG. C4 L1/L2/L3 выражены явно типизированными контейнерами/границами без внешних includes; UML/ER — отдельные представления. Контейнер C4 не означает Docker-container. ER построен по проекту DDL, не по реальному профилю.

| Исходник | Просмотр |
| --- | --- |
| [c4-components.puml](c4-components.puml) | [SVG](rendered/c4-components.svg) |
| [c4-containers.puml](c4-containers.puml) | [SVG](rendered/c4-containers.svg) |
| [c4-context.puml](c4-context.puml) | [SVG](rendered/c4-context.svg) |
| [deployment-native.puml](deployment-native.puml) | [SVG](rendered/deployment-native.svg) |
| [er-finance.puml](er-finance.puml) | [SVG](rendered/er-finance.svg) |
| [er-foundation.puml](er-foundation.puml) | [SVG](rendered/er-foundation.svg) |
| [er-life.puml](er-life.puml) | [SVG](rendered/er-life.svg) |
| [uml-clean-architecture.puml](uml-clean-architecture.puml) | [SVG](rendered/uml-clean-architecture.svg) |
| [uml-domain.puml](uml-domain.puml) | [SVG](rendered/uml-domain.svg) |
| [uml-extraction-state.puml](uml-extraction-state.puml) | [SVG](rendered/uml-extraction-state.svg) |
| [uml-ingestion.puml](uml-ingestion.puml) | [SVG](rendered/uml-ingestion.svg) |
| [uml-job-state.puml](uml-job-state.puml) | [SVG](rendered/uml-job-state.svg) |
| [uml-quest-state.puml](uml-quest-state.puml) | [SVG](rendered/uml-quest-state.svg) |
| [uml-record-assessment.puml](uml-record-assessment.puml) | [SVG](rendered/uml-record-assessment.svg) |
| [uml-use-cases.puml](uml-use-cases.puml) | [SVG](rendered/uml-use-cases.svg) |
| [uml-voice-processing.puml](uml-voice-processing.puml) | [SVG](rendered/uml-voice-processing.svg) |

Рендер: официальный @plantuml/core1.2026.10 в локальном headless Chrome, без Java/Graphviz binary/внешнего сервера. [render-manifest.json](render-manifest.json) связывает SHA256 исходников и SVG, проверяется тестами. Зависимости tools pinned package-lock. `npm ci --prefix tools --ignore-scripts`, затем `node tools/render_diagrams.mjs`; Chrome выбирается DESIGN_CHROME_PATH при необходимости. Рендер не читает private/var и сервер раздаёт только engine JS.

Числовая политика — baseline rules. Статус qualified интеграции не выводится из наличия её на схеме. BPMN находится в [отдельном реестре](../../processes/README.md).
