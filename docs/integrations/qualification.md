# Допуск внешних компонентов

Baseline 1.0. Статусы `unqualified/enabled/unavailable/degraded/disabled` — продуктовые состояния, не незакрытое решение о поведении. Ни один провайдер не считается рабочим только по наличию диаграммы/API-порта.

| Gate | Что должно быть доказано | Если не проходит |
| --- | --- | --- |
| Q-APPLE-ACTIVITY | Разрешённый автоматический доступ к нужным Mac/iPhone aggregate/category данным, entitlement, регион и доступная установка | Capability unavailable; R1 согласованного объёма блокирован до решения владельца, собственный трекер не включать скрыто |
| Q-APPLE-HEALTH | HealthKit-набор (сон/шаги/тренировки/restingHR), подпись без оплаты, фон и pairing batch без дублей; известные ограничения Personal Team | Bridge unavailable, R1 не блокируется; не обещать постоянную автоматизацию при истечении подписи |
| Q-TBANK | Устойчивый автоматический доступ к операциям всех нужных типов счетов/продуктов, read-only, история/cursors/rate limit и 5h freshness | R3 отложен без ручной подмены; доступность Invest API сама по себе не открывает бюджет дебетовой карты |
| Q-AI-TEXT | manifest/license, русский corpus, extraction/retrieval/latency/RAM/disk | R2 блокируется; R1 продолжает ручные оценки |
| Q-AI-VISION | Те же условия плюс image decoding/projector и unknown/portion cases | Питание по фото не выдаётся за работающую оценку, уменьшение R2 scope только явно |
| Q-FOOD-DATA | Локальный справочник, единицы, порции, лицензия распространения, версия | Блюдо/фото сохраняются, КБЖУ unknown; числовой расчёт не допускается |
| Q-DOCKER-STORAGE | Изоляция writer, WAL/backup/restore, native bridge, полный footprint | Native default остаётся; Docker профиль не допускается для профиля |
| Q-CI-MAC | Clean runner без профиля/секретов, сборка macOS/тестов, бесплатные лимиты | Hosted/native acceptance разделить; никаких небезопасных запусков fork PR на личном Mac |

## Проверка документации 20 ноября 2026 года

- Apple Personal Team требует переустановки после истечения семидневного provisioning. Не выводим из этого доступность всех HealthKit/FamilyControls entitlements. [Apple account](https://developer.apple.com/help/account/basics/about-your-developer-account).
- HealthKit framework на macOS не даёт доступ к данным здоровья согласно ранее проверенному owning source. Сбор — условный iPhone bridge. [Health availability](https://developer.apple.com/documentation/healthkit/hkhealthstore/ishealthdataavailable()).
- В официальном каталоге есть T-Invest API, но это не свидетельство доступа к потребительским операциям карты. На текущей проверке developer portal не загрузился; полный read-only banking API не подтверждён. [T-Invest](https://www.tbank.ru/invest/open-api/).
- SQLite runtime должен включать исправление WAL-reset: ≥3.51.3. [SQLite WAL](https://www.sqlite.org/wal.html).

Это документальное исследование, не опыт на аккаунтах/устройствах. Credentials в документацию не запрашиваются и не сохраняются. Недоступность сайта не доказывает отсутствие API; gate остаётся unqualified с явным условием проверки.

## Контракт импорта

Batch содержит provider/capability, стабильный batch ID, samples с внешними IDs, source timestamps/units и cursor. Chunk≤1000 элементов, schema version. Checkpoint коммитится только со всеми признанными элементами chunk или с явно учтённым terminal reject; transient ошибка не перепрыгивается. Повтор batch безопасен. Старый snapshot не откатывает более новую source revision. Смена прав/подписи отмечается capability status и не создаёт отрицательный факт о поведении владельца.

Для Health дубликаты от нескольких устройств связываются через source IDs/interval/type; overlapping aggregate не суммируется со своими исходными samples. Для Screen Time нельзя считать часы браузера доказанными часами работы. Пока semantic classifier не согласован, показываем категории источника и происхождение.
