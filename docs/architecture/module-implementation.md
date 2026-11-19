# Модули и классы реализации baseline 1.0

## Сборки

`services/core/LifeTracker.slnx`:
- `src/LifeTracker.Kernel` — ID/time/Money/Result, без EF/HTTP/DI.
- `src/LifeTracker.Application.Abstractions` — Clock, UnitOfWork, command metadata, cancellation, event envelope.
- `src/Modules/<Name>/LifeTracker.<Name>.Domain` — агрегаты и чистые политики.
- `src/Modules/<Name>/LifeTracker.<Name>.Contracts` — опубликованные commands/query DTO и интерфейсы сценариев; primitives/kernel только.
- `src/Modules/<Name>/LifeTracker.<Name>.Application` — handlers/use cases и порты конкретного модуля.
- `src/Modules/<Name>/LifeTracker.<Name>.Infrastructure` — EF mapping/repository и адаптеры этого модуля.
- `src/LifeTracker.Infrastructure.Shared` — connection/UoW, schema migration, media store, job/outbox plumbing; не бизнес-правила.
- `src/LifeTracker.Host` — API endpoints, composition root, host services.
- `adapters/telegram/LifeTracker.Adapters.Telegram` — trusted channel adapter; подключается Host, не отдельная authoritative база.

Имена модулей: Profile, State, Character, Journal, Insights, Nutrition, Finance, Investments, Quests, Notifications. Operations — application/infrastructure subsystem общей БД, не универсальный DomainService. Пакеты будущих R2–R4 добавляются по этапам; папки заранее не создаются.

## Направление ссылок

Domain → Kernel. Contracts → Kernel (только если ValueObject является стабильной частью контракта, иначе primitives). Application → own Domain/Contracts + Application.Abstractions + явно разрешённые соседние Contracts. Infrastructure → own Application/Domain + Infrastructure.Shared. Host → published Application registration/Infrastructure; UI/Telegram → application contracts/transport generated DTO. Cycles запрещены. Infrastructure не используется Application через service locator.

Cross-module вызовы: Character.Application → State.Contracts (query current state); Journal.Application → State.Contracts (record extracted estimate) / Insights.Contracts (accepted evidence intent); Quests.Application → Character.Contracts (effect command); Finance.Application → Notifications.Contracts (intent). Длинные асинхронные зависимости — outbox, не круговой синхронный вызов. Investments получает financial source events через контракт, не изменяет бюджетную категорию.

## Основные классы / порты

| Модуль | Domain | Application | Infrastructure |
| --- | --- | --- | --- |
| State | Assessment, AssessmentRevision, Score, BalancePolicy, SourceSelectionPolicy | RecordAssessmentHandler, ReviseAssessmentHandler, CurrentStateQuery; IAssessmentRepository, IClock | SqliteAssessmentRepository, AssessmentMap |
| Character | ExperienceLedger, AttributeProjection, EffectReversalPolicy | GetCharacterHandler, ApplyEffectHandler; IGameEffectRepository | SqliteEffectRepository |
| Journal | Conversation, Episode, Person, ExtractionDecision | AcceptMessageHandler, ProcessVoiceHandler, DecideExtractionHandler, MergePersonHandler; ITranscriber, IExtractionGateway, IMediaStore | WhisperAdapter, MessageRepository, MediaFileStore |
| Insights | Interpretation, EvidenceBasis, ConfidencePolicy | RetrieveContextHandler, BuildInterpretationHandler, ReviseEvidenceHandler; IContextIndex, IInferenceGateway | Fts5ContextIndex, LlamaAdapter, DependencyRepository |
| Nutrition | Meal, Portion, NutrientRange | IdentifyMealHandler, RevisePortionHandler; IFoodCatalog, IVisionGateway | LocalFoodCatalog, VisionAdapter |
| Finance | Money, LedgerJournal, Reserve, MonthPlan, MonthClosing | IngestFinancialBatch, ProposePlan, ApprovePlan, CloseMonth; IFinancialSource | TBankReadAdapter (qualification), LedgerRepository |
| Investments | Position, CorporateAction, CalculationCoverage | ReconcilePortfolio, CalculateReturn, GetTaxLedger | BrokerEventRepository, PriceSourceAdapter |
| Quests | QuestOccurrence, LoadBudget, QuestAssignmentPolicy, CompletionPolicy | PlanQuestsHandler, RecordOutcomeHandler, WeeklyAdaptationHandler; IQuestCandidateGenerator | LlamaQuestAdapter, QuestRepository |
| Notifications | NotificationIntent, ReminderQuota, QuietHoursPolicy | PlanDeliveryHandler, DeliverHandler; IChannelDelivery | MacNotificationBridge, TelegramDeliveryAdapter |

Эти классы не обязаны иметь по одному интерфейсу. Интерфейс появляется на заменяемой внешней границе. Порт репозитория выражает сценарий/агрегат, не universal CRUD на каждую таблицу. Методы cancellation-aware, deadline передаётся в adapters. ORM entities и endpoint DTO не являются доменными сущностями.

## Тестовая структура

`tests/Architecture`, `tests/<Module>.Domain.Tests`, `tests/<Module>.Application.Tests`, `tests/Contracts`, `tests/Persistence.Integration`, `tests/Acceptance`. Swift package tests рядом с клиентом; XCUITest target отдельный. Product fake и production adapter проходят одинаковый behavioral contract. Модели ИИ тестируются через schema/corpus + отдельно actual runtime, не mock-only acceptance.

## SOLID guards

SRP: контроллер без формулы, handler без SDK-парсинга, repository без назначения награды. OCP: новый адаптер не меняет policy. LSP: общий port contract включает not-found, revision conflict, cancellation и transaction semantics. ISP: read/query и write ports разделять по реальным клиентам. DIP: import graph/assembly reference tests блокируют зависимости внутрь неправильной стороны; composition root — единственное точечное исключение.
