# Banco de dados — EasyPanel (Fases 1–4)

Provider: **PostgreSQL** (Npgsql + EF Core). As migrações são aplicadas na
inicialização pelo `MigrationHostedService` (com advisory lock para serializar
entre instâncias).

## Convenções

- Chaves primárias `Guid`, atribuídas pela aplicação (`ValueGenerated.Never`).
- Timestamps `DateTimeOffset` em UTC (conversão centralizada).
- Enums persistidos como `int`.
- Toda entidade de negócio isolada por tenant herda de `TenantEntity` (coluna
  `TenantId` obrigatória) e recebe query filter global + validação de escrita.

## Entidades (Fase 1)

### Tenant (raiz de isolamento)
`Id`, `Name`, `Slug` (único global), `IsActive`, `CreatedAt`.

### Identity (ASP.NET Identity)
- **ApplicationUser** (`AspNetUsers`): campos padrão do Identity + `TenantId`
  (anulável — nulo apenas para Super Admin), `CustomerId` (anulável — não nulo
  apenas para usuários do papel `Cliente`, Fase 10, vínculo usuário↔Cliente do
  Portal do Cliente), `IsActive`, `MfaEnabled`, `CreatedAt`.
- **ApplicationRole** (`AspNetRoles`): papéis da plataforma.
- Tabelas de junção padrão do Identity (UserRoles, UserClaims, etc.).
- **RefreshToken**: `UserId`, `TokenHash` (SHA-256; valor bruto nunca persistido),
  `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`.

### Customer (`Customers`) — TenantEntity
`RazaoSocial`, `NomeFantasia?`, `Cnpj` (normalizado, 14 dígitos), `InscricaoEstadual?`,
`Telefone?`, `Email?`, `Endereco?`, `Cidade?`, `Estado?`, `Cep?`, `Observacoes?`,
`Status` (Ativo/Inativo/Bloqueado).

### Location (`Locations`) — TenantEntity
`CustomerId` (FK → Customers, `Restrict`), `Nome`, `Endereco?`, `Responsavel?`,
`Telefone?`, `Email?`, `Observacoes?`, `Status` (Ativo/Inativo). Um Customer possui
múltiplos Locais.

### AuditLog (`AuditLogs`) — BaseEntity (TenantId anulável)
`ActorUserId?`, `TenantId?`, `Action`, `ResourceType`, `ResourceId?`, `OldValues?`,
`NewValues?` (JSON redigido), `Ip?`, `UserAgent?`, `Result` (Success/Denied/Failure),
`OccurredAt`. Append-only por convenção de negócio.

## Entidades (Fase 2 — Monitoramento)

Todas herdam de `TenantEntity` (isoladas por tenant), salvo indicação.

- **WindowsClient** (`WindowsClients`): `CustomerId`, `LocationId` (FKs `Restrict`),
  `UniqueId`, `Hostname`, `AgentVersion`, `State` (Registered/Active/HeartbeatMissing/Disabled),
  `LastHeartbeatAt?`, `LastCollectionAt?`, `SecretHash` (segredo do agente hasheado).
- **Printer** (`Printers`): `CustomerId`, `LocationId` (FKs `Restrict`), `Fabricante?`,
  `Modelo?`, `NumeroSerie?`, `Patrimonio?`, `Ip?`, `Mac?`, `Hostname?`, `Protocolo?`,
  `Porta?`, `Status`, `MonitoringEnabled`, `InstalledAt?`, `Observacoes?`.
- **PrinterCounter** (`PrinterCounters`): `PrinterId`, `Timestamp`, `TimestampTicks`
  (long, chave de cursor portável), `CounterType`, `CounterTypeLabel?`, `Value`,
  `Source`, `WindowsClientId?`, `CollectionId?`, `IsAdministrativeAdjustment`. Append-only.
- **PrinterEvent** (`PrinterEvents`): `PrinterId?`, `WindowsClientId?`, `Type`
  (StatusChanged/Movement/CollectionFailure/HeartbeatMissing), `Detail?`, `OccurredAt`,
  `CreatedAtTicks` (long, adicionado na Fase 3 — cursor portável do `AlertEngine`).
  Append-only.
- **Collection** (`Collections`): `IdempotencyKey`, `WindowsClientId`, `PrinterId?`,
  `StartedAt`, `FinishedAt?`, `Result`, `Errors?`, `CollectedData?` (JSON), `AttemptCount`.
- **PrinterMovement** (`PrinterMovements`): `PrinterId`, `Operation`, `FromLocationId?`,
  `ToLocationId?`, `ActorUserId?`, `OccurredAt`. Append-only.
- **IngestionDedup** (`IngestionDedups`): `IdempotencyKey`, `CollectionId` (backstop de idempotência).
- **ClientRefreshToken** (`ClientRefreshTokens`): `WindowsClientId`, `TokenHash`,
  `ExpiresAt`, `RevokedAt?`, `ReplacedByTokenHash?` (refresh opaco rotativo do agente).
- **LocationProvisioningKey** (`LocationProvisioningKeys`): `CustomerId`, `LocationId`,
  `KeyHash`, `ExpiresAt?`, `RevokedAt?`, `Description?` (credencial de registro do Local).

## Entidades (Fase 3 — Alertas e Notificações)

Todas herdam de `TenantEntity` (isoladas por tenant), salvo indicação.

- **AlertRule** (`AlertRules`): `Name`, `Description?`, `IsActive`, `EventTypesCsv`
  (CSV de `PrinterEventType`), `ScopeType` (Tenant/Location/Printer/WindowsClient),
  `ScopeLocationId?`/`ScopePrinterId?`/`ScopeWindowsClientId?` (FKs `Restrict`),
  `Severity`, `ThresholdCount?`, `ThresholdWindowMinutes?`, `AutoResolve`,
  `EmailEnabled`, `EmailRecipientsCsv?`, `WebhookEnabled`, `WebhookUrl?`,
  `WebhookSecret?` (nunca exposto em DTO/log/auditoria).
- **Alert** (`Alerts`): `AlertRuleId` (FK `Restrict`), `Severity` (cópia no
  momento da criação), `State` (Open/Acknowledged/Resolved), `PrinterId?`,
  `WindowsClientId?` (FKs `Restrict`), `FirstOccurrenceAt`/`FirstOccurrenceAtTicks`,
  `LastOccurrenceAt`/`LastOccurrenceAtTicks`, `OccurrenceCount`, `AcknowledgedAt?`,
  `AcknowledgedByUserId?`, `ResolvedAt?`, `ResolvedByUserId?` (nulo quando
  `AutoResolved`), `AutoResolved`, `ResolutionNote?`.
- **AlertTransition** (`AlertTransitions`): `AlertId` (FK `Restrict`), `FromState`,
  `ToState`, `ActorUserId?` (nulo = sistema), `OccurredAt`, `Note?`. Append-only.
- **AlertNotificationOutbox** (`AlertNotificationOutbox`): `AlertId` (FK
  `Restrict`), `Channel` (Email/Webhook), `Status` (Pending/Sent/Suppressed/
  FailedPermanently), `AttemptCount`, `NextAttemptAt`. Drenada pelo
  `AlertNotificationDispatcher`.
- **AlertNotificationAttempt** (`AlertNotificationAttempts`): `AlertId` (FK
  `Restrict`), `Channel`, `Outcome` (Success/Failure/Suppressed), `AttemptNumber`,
  `HttpStatusCode?`, `ErrorSummary?`, `AttemptedAt`/`AttemptedAtTicks`. Append-only.
- **AlertSilence** (`AlertSilences`): `AlertRuleId?`/`PrinterId?`/
  `WindowsClientId?` (FKs `Restrict`; ao menos um obrigatório), `StartsAt`,
  `EndsAt`, `CreatedByUserId`, `Reason?`, `EndedEarlyAt?`, `EndedEarlyByUserId?`.
- **AlertEngineCheckpoint** (`AlertEngineCheckpoints`) — **BaseEntity** (não
  `TenantEntity`, mesmo padrão de `AuditLog`): `LastProcessedEventTicks`,
  `LastProcessedEventId`. Linha única — cursor global de plataforma do
  `AlertEngine`.

## Entidades (Fase 4 — Suprimentos)

Ambas em `Modules.Monitoring` (extensão do módulo da Fase 2, não um módulo novo);
`TenantEntity`.

- **SupplyReading** (`SupplyReadings`): `PrinterId` (FK `Restrict`), `Label`,
  `Percent` (0–100), `Timestamp`/`TimestampTicks`, `WindowsClientId?`,
  `CollectionId?`. Append-only.
- **SupplyThreshold** (`SupplyThresholds`): `PrinterId?` (FK `Restrict`; nulo =
  padrão do Tenant), `Label?` (nulo = aplica a qualquer rótulo), `ThresholdPercent`
  (0–100). No máximo uma linha por `(TenantId, PrinterId, Label)`, garantido em
  código (upsert do `SupplyService`), não por constraint de banco.

## Entidades (Fase 5 — Estoque)

Todas em `Modules.Inventory` (módulo novo); `TenantEntity`. Local e Impressora
são referenciados apenas por `Guid` (sem FK de módulo cruzado no domínio — a
validação de existência/tenant acontece em `Infrastructure.Inventory`).

- **InventoryItem** (`InventoryItems`): `Name`, `Sku?`, `Unit` (padrão
  "unidade"), `SupplyLabel?`, `IsActive` (padrão `true`), `Observations?`.
- **InventoryMovement** (`InventoryMovements`): `ItemId` (FK `Restrict`),
  `LocationId` (FK `Restrict`), `Type` (Entrada/Saída/Ajuste),
  `AdjustmentDirection?` (Increase/Decrease, só relevante em Ajuste),
  `Quantity` (inteiro positivo), `PrinterId?` (FK `Restrict`), `Reason?`,
  `ActorUserId?`, `OccurredAt`/`OccurredAtTicks`. Append-only.
- **InventoryBalance** (`InventoryBalances`): `ItemId`, `LocationId`,
  `Quantity` (materializado, nunca negativo). Atualizado atomicamente com cada
  `InventoryMovement`, na mesma transação.
- **InventoryMinimum** (`InventoryMinimums`): `ItemId`, `LocationId`,
  `MinimumQuantity`. Uma linha por `(TenantId, ItemId, LocationId)` (upsert).

## Entidades (Fase 6 — Chamados/Helpdesk e SLA)

Todas em `Modules.Ticketing` (módulo novo); `TenantEntity`. Cliente, Local,
Impressora e usuários são referenciados apenas por `Guid` (sem FK de módulo
cruzado no domínio — a validação de existência/tenant acontece em
`Infrastructure.Ticketing`).

- **Ticket** (`Tickets`): `Title`, `Description?`, `CustomerId` (FK
  `Restrict`), `LocationId?`/`PrinterId?` (FK `Restrict`), `Priority`,
  `Status`, `RequestedByUserId`, `AssignedToUserId?`,
  `FirstResponseDueAt`/`FirstResponseDueAtTicks`,
  `FirstResponseAt?`/`FirstResponseAtTicks?`, `FirstResponseCompliance`,
  `ResolutionDueAt`/`ResolutionDueAtTicks`,
  `ResolvedAt?`/`ResolvedAtTicks?`, `ResolutionCompliance`,
  `CreatedAtTicks`.
- **TicketInteraction** (`TicketInteractions`): `TicketId` (FK `Restrict`),
  `Type` (Comentário/MudançaStatus/Atribuição), `ActorUserId`, `Comment?`,
  `FromStatus?`/`ToStatus?`, `AssignedToUserId?`,
  `OccurredAt`/`OccurredAtTicks`. Append-only.
- **SlaPolicy** (`SlaPolicies`): `Priority`, `FirstResponseMinutes`,
  `ResolutionMinutes`. Uma linha por `(TenantId, Priority)` (upsert).
- **TicketAttachment** (`TicketAttachments`): `TicketId` (FK `Restrict`),
  `FileName`, `ContentType`, `SizeBytes`, `StorageKey`,
  `UploadedByUserId`, `UploadedAt`/`UploadedAtTicks`. Persistido somente
  depois que o objeto foi gravado com sucesso no MinIO.

## Entidades (Fase 7 — Contratos)

Todas em `Modules.Contracts` (módulo novo); `TenantEntity`. Cliente, Local e
Impressora são referenciados apenas por `Guid` (sem FK de módulo cruzado no
domínio — a validação de existência/tenant acontece em
`Infrastructure.Contracts`).

- **Contract** (`Contracts`): `Number`, `CustomerId` (FK `Restrict`,
  imutável), `StartDate`/`StartDateTicks`, `EndDate?`/`EndDateTicks?`,
  `Status`, `Observations?`, `CreatedAtTicks`.
- **ContractLocation** (`ContractLocations`): `ContractId` (FK `Restrict`),
  `LocationId` (FK `Restrict`). Escopo opcional — uma linha por
  `(TenantId, ContractId, LocationId)`.
- **ContractPrinter** (`ContractPrinters`): `ContractId` (FK `Restrict`),
  `PrinterId` (FK `Restrict`). Escopo opcional — uma linha por
  `(TenantId, ContractId, PrinterId)`.
- **ContractFranchise** (`ContractFranchises`): `ContractId` (FK
  `Restrict`), `CounterType`, `CounterTypeLabel?`, `IncludedQuantity`,
  `ExcessUnitPrice` (`decimal(18,4)`, primeiro campo monetário da
  plataforma), `Currency` (fixo `"BRL"`). Uma linha por
  `(TenantId, ContractId, CounterType)` (upsert).

## Entidades (Fase 8 — Fechamento e Faturamento)

Todas em `Modules.Billing` (módulo novo); `TenantEntity`. Impressora e
Contrato são referenciados apenas por `Guid` (sem FK de módulo cruzado no
domínio — a validação/leitura cruzada acontece em `Infrastructure.Billing`).

- **BillingClosing** (`BillingClosings`): `Year`, `Month`,
  `PeriodStart`/`PeriodStartTicks`, `PeriodEnd`/`PeriodEndTicks`,
  `ExecutedAt`/`ExecutedAtTicks`, `ExecutedByUserId?`, `InvoiceCount`.
  Somente-adição; uma linha por `(TenantId, Year, Month)`.
- **Invoice** (`Invoices`): `ContractId`, `CustomerId` (copiado do Contrato
  no momento da geração), `PeriodStart`/`PeriodStartTicks`,
  `PeriodEnd`/`PeriodEndTicks`, `Status`, `TotalAmount` (`decimal(18,2)`),
  `Currency` (fixo `"BRL"`), `GeneratedAt`/`GeneratedAtTicks`, `IssuedAt?`,
  `CancelledAt?`.
- **InvoiceLineItem** (`InvoiceLineItems`): `InvoiceId`, `PrinterId`,
  `CounterType`, `CounterTypeLabel?`, `ConsumedQuantity`,
  `IncludedQuantity`, `ExcessQuantity`, `UnitPrice` (`decimal(18,4)`),
  `LineAmount` (`decimal(18,2)`). Só criado quando `ExcessQuantity > 0`;
  imutável depois que a Fatura sai de `Rascunho`.

## Índices

| Tabela | Índice | Motivo |
|--------|--------|--------|
| Tenant | `UNIQUE (Slug)` | identificador público |
| AspNetUsers | `UNIQUE (TenantId, NormalizedEmail)` filtrado, `(TenantId)`, `(TenantId, CustomerId)` | email único por tenant, isolamento, apoio ao vínculo usuário↔Cliente (Fase 10) |
| RefreshToken | `UNIQUE (TokenHash)`, `(UserId)`, `(ExpiresAt)` | validação e limpeza |
| Customer | `UNIQUE (TenantId, Cnpj)`, `(TenantId, Status)`, `(TenantId, RazaoSocial)`, `(TenantId)` | unicidade, filtros/ordenação, isolamento |
| Location | `(TenantId, CustomerId)`, `(TenantId, Status)`, `(TenantId)` | listagem por cliente, filtros, isolamento |
| AuditLog | `(TenantId, OccurredAt DESC)`, `(TenantId, ResourceType)` | consulta paginada por período |
| WindowsClient | `UNIQUE (TenantId, UniqueId)`, `(TenantId, LocationId)`, `(TenantId, State)` | unicidade do agente, listagem, heartbeat |
| Printer | `(TenantId, CustomerId)`, `(TenantId, LocationId)`, `(TenantId, Status)`, `(TenantId, NumeroSerie)` | listagem/filtros do parque |
| PrinterCounter | `(TenantId, PrinterId, CounterType, TimestampTicks)` | cursor + não-decréscimo |
| PrinterEvent | `(TenantId, Type, OccurredAt)`, `(TenantId, PrinterId, OccurredAt)`, `(CreatedAtTicks)` | consumo por tipo/impressora; cursor global do `AlertEngine` |
| Collection | `UNIQUE (TenantId, IdempotencyKey)`, `(TenantId, WindowsClientId, StartedAt)`, `(TenantId, PrinterId, StartedAt)` | idempotência, histórico |
| PrinterMovement | `(TenantId, PrinterId, OccurredAt)` | histórico por impressora |
| IngestionDedup | `UNIQUE (TenantId, IdempotencyKey)` | backstop de idempotência |
| ClientRefreshToken | `UNIQUE (TokenHash)`, `(TenantId, WindowsClientId)` | validação/rotação |
| LocationProvisioningKey | `UNIQUE (KeyHash)`, `(TenantId, LocationId)` | validação de registro |
| AlertRule | `(TenantId, IsActive)`, `(TenantId, ScopeType)`, `(TenantId)` | seleção pelo `AlertEngine`, listagem |
| Alert | `(TenantId, State, LastOccurrenceAtTicks)`, `(TenantId, AlertRuleId, PrinterId, WindowsClientId, State)` | cursor de listagem; dedupe de Alerta aberto |
| AlertTransition | `(TenantId, AlertId, OccurredAt)` | histórico por Alerta |
| AlertNotificationOutbox | `(Status, NextAttemptAt)` | fila do `AlertNotificationDispatcher` |
| AlertNotificationAttempt | `(TenantId, AlertId, AttemptedAtTicks)` | histórico por cursor |
| AlertSilence | `(TenantId, AlertRuleId)`, `(TenantId, PrinterId)`, `(TenantId, WindowsClientId)` | verificação de vigência no despacho |
| SupplyReading | `(TenantId, PrinterId, Label, TimestampTicks)` | níveis atuais/histórico por cursor |
| SupplyThreshold | `(TenantId, PrinterId, Label)` | resolução em cascata do limiar |
| InventoryItem | `(TenantId, Name)`, `(TenantId, Sku)`, `(TenantId)` | busca por nome/SKU, isolamento |
| InventoryMovement | `(TenantId, ItemId, LocationId, OccurredAtTicks)`, `(TenantId, PrinterId, OccurredAtTicks)` | histórico por cursor (item/local, impressora) |
| InventoryBalance | `UNIQUE (TenantId, ItemId, LocationId)`, `(TenantId, LocationId)` | saldo materializado, consulta por local |
| InventoryMinimum | `UNIQUE (TenantId, ItemId, LocationId)` | upsert de mínimo por item/local |
| Ticket | `(TenantId, Status, CreatedAtTicks)`, `(TenantId, CustomerId)`, `(TenantId, AssignedToUserId)` | listagem/filtro por status/cliente/responsável |
| TicketInteraction | `(TenantId, TicketId, OccurredAtTicks)` | histórico por cursor |
| SlaPolicy | `UNIQUE (TenantId, Priority)` | upsert por prioridade |
| TicketAttachment | `(TenantId, TicketId)` | listagem de anexos por chamado |
| Contract | `(TenantId, CustomerId, Status)` | listagem/filtro por cliente/status; base da resolução em cascata (R4) |
| ContractLocation | `UNIQUE (TenantId, ContractId, LocationId)`, `(TenantId, LocationId)` | escopo único, verificação de sobreposição/resolução reversa |
| ContractPrinter | `UNIQUE (TenantId, ContractId, PrinterId)`, `(TenantId, PrinterId)` | escopo único, verificação de sobreposição/resolução reversa |
| ContractFranchise | `UNIQUE (TenantId, ContractId, CounterType)` | upsert por tipo de contador |
| BillingClosing | `UNIQUE (TenantId, Year, Month)` | impede refechamento silencioso |
| Invoice | `(TenantId, CustomerId)`, `(TenantId, ContractId)`, `(TenantId, Status)`, `(TenantId, PeriodStartTicks, PeriodEndTicks)` | filtros de listagem |
| InvoiceLineItem | `(TenantId, InvoiceId)` | itens de uma fatura |

## Migrações

Localizadas em `src/EasyPanel.Infrastructure/Persistence/Migrations/`. Criar novas:

```bash
dotnet ef migrations add <Nome> \
  --project src/EasyPanel.Infrastructure \
  --startup-project src/EasyPanel.Infrastructure
```

O projeto Infrastructure possui `AppDbContextFactory` (design-time), então o
comando não exige o host da API nem uma conexão viva.

## Unicidade de email por tenant

O `UserName` do Identity é qualificado por tenant (`{tenantId}:{email}`) para
mapear a unicidade global de username do Identity à unicidade de email **por
tenant** (o mesmo email pode existir em tenants diferentes). O login resolve por
email; o formato interno do UserName não afeta a autenticação.
