# Данные Apple: предварительная проверка

Дата: 19 ноября 2026 года. Это проверка документации, не эксперимент на устройствах.

## Требование пользователя

Использовать уже собранные Apple данные здоровья и агрегированного экранного времени Mac/iPhone, включая категории Apple. Не подменять это требование собственным трекером без согласования.

## Найденные ограничения

- Apple указывает, что на macOS приложение не может читать и записывать данные HealthKit: [isHealthDataAvailable](https://developer.apple.com/documentation/healthkit/hkhealthstore/ishealthdataavailable()). Наличие интерфейса, зеркалирования или стороннего приложения на Mac не подтверждает доступ к базе здоровья.
- Для Screen Time используются специальные разрешения и расширения: [Screen Time frameworks](https://developer.apple.com/documentation/ScreenTimeAPIDocumentation), [Family Controls entitlement](https://developer.apple.com/documentation/familycontrols/requesting-the-family-controls-entitlement).
- Документированный метод экспорта активности имеет региональные ограничения EU и требует отдельного entitlement: [activityData(filteredBy:using:)](https://developer.apple.com/documentation/deviceactivity/deviceactivitydata/activitydata(filteredby:using:)). Нельзя пока обещать импорт общего отчёта Apple в произвольный локальный процесс.

## Что проверить

Проверять версии ОС, региональные ограничения, доступность API и разрешений, необходимость iPhone-компонента, бесплатную установку и фоновую передачу. Конкретный регион аккаунта и данные устройств относятся к локальной конфигурации и не публикуются. Доступность интеграций не подтверждена.
