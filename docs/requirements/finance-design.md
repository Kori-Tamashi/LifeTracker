# Финансовый учёт baseline 1.0

Статус: проектное решение, 20 ноября 2026 года. Налоговые правила законодательства не реализуются и не выводятся ИИ; приложение учитывает исходные брокерские налоговые события/отчёты. Автоматический адаптер — обязательный qualification gate.

## Учет и точность

Счёт имеет тип own_liquid/savings/deposit/credit/brokerage и валюту. Операция — неизменяемая версия факта источника со статусом pending/posted/reversed, source ID, booked/occurred time. Каждая признанная операция раскладывается на проводки с signed integer minor units и единым масштабом валюты; суммы не хранятся float. Валютная конверсия — явная пара сумм с FX rate/date/source, не смешение валют в одной сумме.

Для posted-транзакции сумма проводок по каждой валюте равна нулю: для внешнего дохода/расхода существует категориальный контрсчёт. Commit ledger проходит application guard и отложенную проверку всех проводок в транзакции; неполный ledger journal не участвует в отчёте. Статус признания draft/posted предотвращает показ частично записанного события. `reversed` journal остаётся признанным вместе с отдельной компенсирующей проводкой: отчёт читает posted/reversed, иначе исключение оригинала и включение reversal дали бы неверный итог.

Покупка: expense+уменьшение liquid либо увеличение credit liability. Погашение: liquid→credit liability, не повторный expense. Процент/комиссия: отдельный expense. Перевод между собственными счетами — transfer; возврат — reversal/negative expense исходной категории, не доход. При невозможности сопоставить pending/posted или transfer обе стороны помечаются unresolved; финансовая сводка incomplete до разрешения адаптером/проверяемым алгоритмом, не тихое двойное включение. Пользователь может исправить категорию, это не ручная замена автоматического получения операций.

## План и резервы

MonthPlan содержит expected incomes (date/amount/source), obligations (due/amount), spending envelopes, protected savings, version. Процентные статьи рассчитываются от всего планового дохода месяца; fixed amounts сначала, проценты от P, свободная сумма — остаток. Сумма allocations ≤P; при превышении план invalid и сохраняется только draft. Округление до minor unit — largest remainder, tie-break line ID. Proposals не заменяют approved plan.

Expected income после due date + 1 календарный день grace, если не сопоставлен, создаёт underreceived issue и proposal. Доход может покрыть несколько ожиданий и наоборот, через explicit allocation links; один фактический minor unit не относится к двум ожиданиям. Неожиданный доход — available cash, но изменение лимитов требует принятия нового плана.

`own_liquid` — сумма доступных собственных средств выбранных liquid accounts с исключением holds. Credit balance/limit, брокерские активы и locked deposits не входят. Если часть активов в другой валюте без FX, показываются раздельно и RUB total=null.

`free_now = own_liquid - active_unpaid_obligation_reserves - protected_savings_reserves - other_active_reserves`.

Резервы — записи с remaining amount и lifecycle; при оплате obligation reserve погашается одновременно с признанием expense, чтобы не вычесть второй раз. Если деньги физически уже на исключённом savings account, соответствующий резерв не вычитается из own_liquid: у reserve есть backing account и исключительность включения. Накопления показываются отдельно: выделенные резервы + savings balances без двойного учёта backing links.

`plan_remainder = approved_consumption_limit - posted_net_consumption` по категориям. `daily_guide = floor(max(0,min(free_now,remaining_flexible_envelopes))/days_including_today)` в minor units; отрицательный free_now показывается как дефицит, ориентир 0, не скрытый положительный бюджет. Ожидаемые будущие доходы не расходуются заранее.

## Закрытие месяца

После 09:00 первого числа следующего месяца создать closing candidate. Если источники не обновились за 5h активной работы, есть unresolved или incomplete FX — provisional, автоматического распределения нет. В остальных случаях residual cash, не покрытый ранее savings/obligation reserves и не требуемый открытыми обязательствами, относится в учётную копилку. Реальных переводов нет. Closing ID уникален по month/plan revision; late operation создаёт restatement и компенсирующий reserve adjustment. Дефицит остаётся отдельной записью и proposal покрытия из savings, не автоматическое списание. Пересмотр закрытого месяца сохраняет старый closing record.

## Инвестиции

Хранить инструменты/валюты, сделки, цены/количества с явной decimal scale, комиссии, купоны/дивиденды, cash flows, corporate actions, налоговые события и valuation snapshots. Источник брокерских cost basis и налогооблагаемой базы приоритетен; комиссия не теряется в цене без metadata. Корпоративное действие меняет позиции только по explicit событию, не по догадке LLM. Сверка количества: opening + trades + actions = closing source position; mismatch → отчёт incomplete.

Показатели:
- Market value — quantity × quoted price с валютой и timestamp; missing quote → unknown, не 0.
- Cash income — купоны/дивиденды, показывать gross/withheld tax/net раздельно.
- Realized P&L — net proceeds − allocated cost − explicit fees при доступном cost basis; при его отсутствии unknown. В baseline используем брокерский cost basis; локальный FIFO не выдаётся за налоговый метод.
- Unrealized P&L — valuation − remaining cost, если оба известны.
- TWR — произведение `(1+r_subperiod)` минус 1; границы на каждом внешнем cash flow, `r=(V_end_before_flow−V_start_after_previous_flow)/V_start_after_previous_flow`. Если точные valuations недоступны, TWR unavailable; допускается Modified Dietz с явной пометкой estimate: `(V_end−V_start−sum(CF))/(V_start+sum(weight*CF))`, знаменатель >0.
- MWR/XIRR — корни `sum(CF_i/(1+r)^year_fraction_i)=0`, с конечной valuation как приток; ACT/365. Поиск знакопеременных интервалов в диапазоне r∈[−0.9999,100], bracket/bisection tol1e−7 max200. Нет или несколько корней → ambiguous/unavailable, не произвольный ответ. Все параметры и sign convention видны в explain calculation.
- Taxes — удержанное/возвращённое, broker-reported base, tax period и валюта; предварительное обязательство только если источник предоставил значение. Собственная юридическая интерпретация/подача декларации вне scope. Это полный журнал доступных налоговых данных, не гарантированный расчёт всех обязательств владельца.

Каждый CalculationRun хранит method/version, input revision IDs и coverage (complete/partial/unavailable). Сравнение портфеля и категории бюджета не превращает покупку актива в потребительский расход. Основной бюджет и инвестиционный отчёт согласованы по движениям, но имеют разные показатели.

## Проверки

Баланс проводок по валюте, pending→posted один раз, transfer не доход, кредитка purchase/payoff без дубля, возврат в исходную категорию, недополучение после grace, reserve paid без двойного вычета, month restatement без второй копилки, FX unknown, trade/corporate action reconciliation, XIRR multiple/no roots, stale quotes и отсутствие cost basis. Наборы fixtures синтетические; цифры не являются финансовой рекомендацией.
