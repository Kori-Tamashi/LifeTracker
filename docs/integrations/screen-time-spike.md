# D-001: Screen Time qualification

20 ноября 2026 года. Документальный пакет D-001.1 выполнен; device/provisioning эксперимент D-001.2 не выполнен. Gate Q-APPLE-ACTIVITY остаётся **unqualified**, writer/importer не включается. Это не доказательство отсутствия любых будущих API.

| Путь | Документально установленное поведение | Пригодность для baseline |
| --- | --- | --- |
| FamilyActivityData + DeviceActivityData.activityData | Программный экспорт activity; approvedWithDataAccess и отдельный app-and-website-usage entitlement; customer installations ограничены устройством и Apple Account в ЕС | Условный кандидат; нельзя считать универсальной интеграцией или обещать доступ вне поддерживаемых условий |
| Apple-provided development provisioning profile | Apple разрешает разработку/тестирование data-access API во всех регионах с предоставленным Apple профилем | Наличие специального профиля и доступность бесплатного способа установки не подтверждены; обычный Personal Team сам по себе не доказательство |
| DeviceActivityReport extension | Система даёт данные расширению для отрисовки SwiftUI report; sandbox запрещает network requests и перенос sensitive content за address space расширения | Встроенный report не является разрешённым общим экспортом в БД/ИИ/бота; обход через shared files не принимается |
| macOS ScreenTime / STWebpageController | Reporting web usage и реакция на блокировку URL для собственного web view | Не API чтения общей истории приложений Mac/iPhone |
| Собственный native tracker / private databases / UI scraping | Не проверялись как источник baseline | Не подменяют согласованную автоматическую Apple aggregate/category интеграцию |

## Следующий эксперимент и предел

Prerequisites: Xcode/SDK с соответствующими API; минимальный отдельный iPhone test target; Apple provisioning profile с необходимым entitlement; разрешение пользователя. Проверить authorization status, возможность получить реальный aggregate/category batch, межустройственную семантику и стабильные source timestamps/IDs. Не извлекать и не публиковать персональную историю в CI/репозиторий; evidence — обезличенный capability verdict и synthetic контракт.

Текущий запуск не создал Apple developer account, не менял регион, не устанавливал профиль и не запрашивал системные права. Разработческий профиль Apple не считается доступным без подтверждения. До выполнения prerequisites D-001.2 помечается как awaiting prerequisite, а не pass. Дальнейшая независимая работа D-004/005 продолжается; R1 исходного объёма остаётся закрыт до pass или явного решения владельца о scope. Изменения требований этим исследованием не сделаны.

## Первичные источники

- [FamilyActivityData: region, authorization and entitlement](https://developer.apple.com/documentation/familycontrols/familyactivitydata).
- [approvedWithDataAccess: специальные условия development provisioning](https://developer.apple.com/documentation/familycontrols/authorizationstatus/approvedwithdataaccess).
- [activityData export](https://developer.apple.com/documentation/deviceactivity/deviceactivitydata/activitydata(filteredby:using:)).
- [App and Website Usage entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.family-controls.app-and-website-usage).
- [DeviceActivityReport sandbox](https://developer.apple.com/documentation/deviceactivity/deviceactivityreport).
- [macOS Screen Time framework](https://developer.apple.com/documentation/screentime).

Вывод о непригодности report view для общего импорта — инженерная оценка ограничений, явно описанных Apple; приложение на целевом устройстве в рамках этого пакета не тестировалось.
