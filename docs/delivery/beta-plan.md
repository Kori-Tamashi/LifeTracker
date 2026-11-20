# B0 — ранняя бета перед полным R1

Цель: дать тестировщику первую законченную вертикаль State на Mac, опубликовать её в main с тегом `v0.1.0-beta.1` и GitHub prerelease. Scope заморожен: персонаж/самооценки/история/исправления/owned native lifecycle. Новые интеграции и sleep/wake не добавляются в стабилизацию B0.

Это отдельная ступень приёмки, разрешённая решением о ранней тестовой версии. R1 scope и gates не отменяются. Для B0 GUI/close-window/Login Items становятся явными заданиями ручного тестирования после доставки; отсутствие Computer Use не подменяется отметкой pass. Backup/restore и orphan recovery не считаются готовыми; тестирование начинается в отдельном профиле. ИИ/банк/Telegram/Apple gates неприменимы к реализованному B0, но сохраняются для следующих выпусков.

## Exit B0

- Согласованные version/build/channel/minimum OS совпадают в configs/releases/beta.json и Info.plist.
- Чистый SHA проходит product/project CI и повторные release checks; нет подавления ошибок и неожиданно пропущенных live integrations.
- Проверены настоящий пакет, подпись, allowlist/checksums, первая установка и повторное обновление, реальный встроенный Core start/stop/restart.
- Состав пакета не содержит профиля/ключей/PDB; third-party notices и dependency inventory вложены. Данные не мигрируют автоматически.
- Подготовлены инструкция первых 10 минут, известные ограничения и evidence. Скачанный ad-hoc пакет не объявляется notarized.
- GitFlow: release/0.1.0-beta.1 → main через PR после gates → автоматический prerelease на exact main SHA → возврат main в develop через PR.
- Тег/публичные assets не перезаписываются. Failed draft допускает повторную загрузку только для того же SHA; другой SHA требует новой beta-версии.

Docker подтверждён доступным на локальном стенде; B0 использует квалифицированный native host. Доступность Docker сама по себе не закрывает Q-DOCKER-STORAGE и не является поводом менять хранилище перед тестом.

Следующий цикл начинается с результатов ручного теста, исправлений и beta.2, затем D-007.2/backup/Telegram. Полный R1 выпускается только по прежним acceptance gates.

## B1 — кандидат beta.2

После B0 накоплен законченный технический срез lifecycle/backup. [Кандидат beta.2](../releases/v0.1.0-beta.2.md) готовится в develop для конкретной ручной проверки, без публикации main/tag этим пакетом. Новые модули до результатов проверки не добавляются. Текущее config/Info.plist задаёт beta.2/build2; beta.1 остаётся выпущенной/установленной версией.

Candidate workflow на develop запускается только push с изменением release metadata/tools/notes/workflow; выполняет полные gates и выдаёт seven-day Actions artifact. Main publish остаётся отдельным job с exact-main guard. PR в main по-прежнему выполняет build gates. `evidence` format2 требует шесть конкретных live cases и отдельный successful offline backup smoke; scope/manual UI/RPO/disk указаны явно. Чистый SHA/manifest/checksums и защита старого тега сохраняются.

Папка/порт/каталог автокопий beta.2 изолированы от preferences beta.1. На тесте использовать отдельную папку данных, полностью остановив прежнюю версию: общая bundle identity не означает возможность одновременного использования одного порта. Файлы профиля автоматически не переносятся и не удаляются.

Exit технической подготовки: полный candidate workflow, доступный `.app`/ZIP/evidence/TESTING, явный remaining manual checklist. Exit публикации B1 — отдельно: разобранная ручная проверка, устранённые блокирующие дефекты и exact-main release gates. Полный R1 не объявляется принятым.
