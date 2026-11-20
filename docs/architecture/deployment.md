# Развёртывание и жизненный цикл baseline 1.0

## Профили

**Native — default:** LifeTracker.app в «Программах»; LaunchAgent/локальный supervisor запускает core `.NET 10 arm64` и управляемые inference workers; UI — клиент. Runtime root выбирается при первом запуске как папка проекта, содержит `local/` и `var/`. Путь не зависит от cwd. Бинарники внутри app bundle либо versioned runtime install, не в личной базе. macOS 26+ Apple Silicon, приложение собирается локально; подписанное/notarized распространение вне личной машины не обещано без отдельной квалификации и возможной стоимости.

**Docker — optional:** core в linux/arm64 контейнере, UI/Apple agent/inference на host. До Q-DOCKER-STORAGE не включать для реального профиля. Volume не разделяется с host SQLite writer; поддержка локальной структуры данных обеспечивается документированным export/backup mapping. No public port by default: publish core на 127.0.0.1. Inference host endpoint scoped local network/credential и доступен только core; не использовать 0.0.0.0 без сетевого ограничения. Docker Desktop/model store/image footprint входит в квалификационный отчёт.

Не автоматизируем установку платной подписки, настройку сна, открытие интернет-портов или изменение защиты macOS. Режимы не запускают два core с одним профилем.

## Старт

1. Прочитать install manifest и runtime root; проверить разрешения/диск, API compatibility и native SQLite floor.
2. Взять эксклюзивный root lock; при активном экземпляре подключить UI к нему, а не стартовать второй.
3. Проверить schema/миграцию/recovery, загрузить persisted capabilities/jobs/quota.
4. Слушать `127.0.0.1:17841` (изменяемый port в install manifest), запустить scheduler/Telegram при настройке.
5. Lazy inference: модели загружаются на разговор/фото; text/STT можно keep-warm до 5 минут после активности; крупные одновременные jobs сериализуются. При батарее без интерактивного запроса фоновый анализ откладывается до idle/питания, события не теряются.
6. UI запрашивает character/state/capabilities, показывает degraded status каждой зависимости отдельно. Отсутствие модели не блокирует ручные оценки.

## Stop и сон

Закрытие окна — UI hide/exit без stop core. Полная команда Stop доступна только локальному desktop supervisor, не голосовой модели, боту или Health bridge. Core прекращает приём новых jobs, сохраняет checkpoint, отменяет inference через cancellation token, дожидается commit до 10 секунд; истекающий lease/fence обеспечивает восстановление при принудительном завершении. Supervisor завершает только процессы из своего install manifest/PID ownership.

Sleep допускает suspension. Wake: проверить clock/root lock/lease, вернуть зависшие jobs, пересчитать TTL состояния, сбросить только memory caches, не persisted квоты; удалить expired notification intent и объединить актуальные. Telegram updates старше server retention не восстанавливаются обещанием локальной очереди.

## Обновление

Автообновление с внешнего сервера не требуется. Релиз — versioned bundle+manifest, local install. До изменения схемы — backup и предмиграционная копия. Для R1 API major1/schema1; несовместимый UI блокируется с update action. Failed healthcheck после установки → previous bundle + snapshot recovery; не заставлять старое приложение открывать новую схему.

## Операционные статусы

`starting`, `ready`, `degraded`, `recovering`, `stopping`, `stopped`. Ready не означает enabled всех интеграций. Логи содержат correlation/error codes, latency и размеры, без текстов/аудио/ключей; ротация 5×5 МиБ, retention 7 дней. Экспорт diagnostics — только обезличенные версии/статусы; raw profile data не включать.

## Бесплатный iPhone bridge

Проект — SwiftUI/HealthKit, local pairing; автоматический сбор возможен только после Q-APPLE-HEALTH. Apple документирует повторное provisioning Personal Team каждые семь дней; это не условие бесконечной автономной установки. Срок/entitlements/фон проверяются до обещания готовой интеграции. [Apple Personal Team](https://developer.apple.com/help/account/basics/about-your-developer-account).

## Текущий срез реализации D-007.1

Native supervisor реализован в desktop-процессе (NativeRuntime); бизнес-ядро остаётся отдельным дочерним dotnet Process. Пакет содержит framework-dependent Core и native SQLite текущей архитектуры; установленный .NET 10 / ASP.NET Core runtime обязателен. Core не зависит от пути checkout при запуске установленного `.app`. Путь профиля/порт хранятся в локальных системных preferences; пользовательские данные и резервные копии остаются в выбранном runtime-root. Это ограниченное исключение для macOS control metadata, не перенос профиля в облако или bundle.

Управляемый start проверяет readiness через authenticated State API/rule version, а при уже работающем совместимом host выполняет attach без владения процессом. Полная остановка внешнего host недоступна. Single-writer обеспечивают storage root lock и desktop journal lock; восстановление orphan ownership после падения desktop не реализовано. При неожиданном завершении собственного host показывается failed, а не бесконечный автоматический restart.

Menu bar/close-window/SMAppService включены в код; actual UI/login approval gate открыт до проверки на целевом Mac. Обновление установщика не меняет Login Items самостоятельно. Sleep/wake, lease reconciliation, notification coalescing и shutdown checkpoint для будущих фоновых workers пока не реализованы и не считаются пройденными по Process tests.

### D-007.2.1 — соединение после пробуждения

В develop добавлена revalidation выбранного профиля/порта после workspace wake: recovering → ready либо degraded. Проверка не порождает новый процесс и не восстанавливает владение orphan/external host. Stop инвалидирует generation и не позволяет позднему ответу вернуть ready. UI сохраняет workspace/редактор во время краткого разрыва, обновляет серверный snapshot после wake; cached balance не выдаётся за текущий до revalidation. Таймеры запуска/остановки считают активное время.

Это connection slice, а не закрытие всех sleep/wake gates: jobs, fences, квоты, expired notifications, crash/orphan recovery и реальный target-device sleep/UI test остаются открытыми.
