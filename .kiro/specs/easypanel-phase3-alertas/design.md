# Design Document — EasyPanel (FASE 3: Alertas e Notificações)

## Decisões tomadas para as questões em aberto do `requirements.md`

O usuário aprovou os requisitos ("sim") sem detalhar as 3 perguntas; assumo os
seguintes padrões-default, todos revisáveis nesta revisão de design:

1. **Provedor de e-mail**: o comentário de `LogOnlyPasswordResetNotifier` (Fase 1) já
   registra que "a entrega efetiva por email será implementada quando o canal de
   notificação existir" — esta é essa fase. Decisão: implementar um `IAlertEmailSender`
   real via `System.Net.Mail.SmtpClient` (biblioteca padrão do .NET, sem novo pacote
   NuGet, sem risco de vulnerabilidade de dependência), configurado por
   `Alerting:Smtp` (Host, Port, EnableSsl, User, Password, FromAddress). Quando
   `Smtp:Host` está vazio (padrão em dev/testes), o sistema usa uma implementação
   `LogOnlyAlertEmailSender` que apenas loga o envio (mesmo padrão da Fase 1),
   preservando "zero warnings"/"sem dependência nova" e mantendo o ambiente de
   testes determinístico. Trocar por outro provedor (ex.: SES/SendGrid via API) no
   futuro é apenas nova implementação de `IAlertEmailSender`, sem tocar no motor de
   alertas.
2. **Escopo da AlertRule**: mantido como já escrito no `requirements.md` aprovado —
   Tenant inteiro, Local, Impressora ou Agente (Windows_Client) específico.
3. **Resolução automática (R2.6)**: campo `AutoResolve` (bool) por `AlertRule`,
   padrão `true` na criação. Ver seção "Resolução automática — desvio de desenho"
   abaixo para como ela é detectada (não é puramente orientada a `PrinterEvent`).

## Overview

A Fase 3 adiciona um novo módulo de negócio, `EasyPanel.Modules.Alerting`, com as
entidades `AlertRule`, `Alert`, `AlertTransition`, `AlertNotificationAttempt`,
`AlertNotificationOutbox` e `AlertSilence` — todas `TenantEntity`, seguindo o mesmo
padrão de isolamento das entidades da Fase 2. O módulo referencia impressoras e
agentes **apenas por `Guid`** (sem navegação de EF entre módulos), do mesmo modo que
`PrinterCounter.PrinterId` referencia `Printer` — preservando a regra de dependência
`Api → Modules.* → Shared.Kernel` sem que módulos de negócio se referenciem entre si.

O `Motor_de_Alertas` (`AlertEngine`, um `BackgroundService` em
`EasyPanel.Infrastructure.Alerting`) é o único componente que enxerga tanto
`EasyPanel.Modules.Monitoring` (para ler `PrinterEvent`, `Printer`, `WindowsClient`)
quanto `EasyPanel.Modules.Alerting` — exatamente como `CollectionProcessor` já cruza
várias entidades de monitoramento hoje. Ele roda em contexto de sistema
(`ISystemDbContextFactory`), como `HeartbeatMonitor`.

Um segundo worker, `AlertNotificationDispatcher`, drena a tabela `AlertNotificationOutbox`
e despacha e-mails/webhooks com retry e backoff, registrando cada tentativa em
`AlertNotificationAttempt` — o mesmo padrão fila+worker do pipeline de ingestão da
Fase 2 (`CollectionProcessingWorker`/`ICollectionProcessor`).

Nenhuma tarefa desta fase reabre ou reescreve funcionalidade da Fase 2; a única
extensão ao código já entregue é uma coluna adicional em `PrinterEvent`
(`CreatedAtTicks`, portável, mesmo motivo de `PrinterCounter.TimestampTicks`) para
permitir um cursor de leitura incremental e ordenado sem depender da tradução de
`DateTimeOffset` pelo provider (armadilha SQLite já documentada no projeto).

## Arquitetura

```
PrinterEvent (Fase 2, +CreatedAtTicks)
        │
        ▼
  AlertEngine (BackgroundService, contexto de sistema)
        │  varre PrinterEvent por lote global (cursor único), agrupado por tenant
        │  casa contra AlertRule ativas do tenant do evento
        │  aplica limiar (threshold/janela) quando configurado
        │  cria/atualiza Alert + AlertTransition
        │  enfileira AlertNotificationOutbox (uma linha por canal ativo da regra)
        │
        │  (mesmo tick) varre Alert abertos/reconhecidos com AutoResolve=true
        │  e resolve automaticamente quando Printer.Status/WindowsClient.State
        │  atual já está saudável
        ▼
  AlertNotificationOutbox
        │
        ▼
  AlertNotificationDispatcher (BackgroundService)
        │  respeita AlertSilence vigente no momento do envio (Suppressed, não Failure)
        │  despacha via IAlertEmailSender / IAlertWebhookSender
        │  grava AlertNotificationAttempt (histórico somente-adição)
        │  retry com backoff exponencial até MaxAttempts
        ▼
  E-mail (SMTP) / Webhook de saída (HMAC-SHA256)
```

### Resolução automática — desvio de desenho em relação à redação literal de R2.6

R2.6 fala em "quando um `PrinterEvent` de mudança de status indica retorno à
normalidade". Na prática, hoje (Fase 2):

- `CollectionProcessor.HandleSuccessAsync` **já** grava um `PrinterEvent`
  `StatusChanged` com `Detail = "online"` quando uma impressora volta a responder —
  este evento pode ser consumido diretamente.
- `HeartbeatService` (Fase 2) restaura `WindowsClient.State` para `Active` quando o
  agente volta a mandar heartbeat, mas **não grava nenhum `PrinterEvent`** nesse
  retorno — não há gancho de evento para o motor de alertas consumir no caso do
  agente.

Duas opções: (a) estender minimamente `HeartbeatService` para também gravar um
`PrinterEvent` de restauração; ou (b) o `AlertEngine`, a cada ciclo, verificar
diretamente o estado corrente (`Printer.Status` / `WindowsClient.State`) dos alvos de
`Alert` abertos com `AutoResolve = true`, resolvendo quando o estado atual já é
saudável — sem exigir um evento específico de "voltou ao normal".

**Decisão: opção (b).** É mais robusta (funciona mesmo que um evento de recuperação
seja perdido/atrasado), não exige tocar em código já testado e aprovado da Fase 2, e
cobre uniformemente impressora e agente com a mesma lógica. O comportamento observável
para o usuário é o mesmo descrito em R2.6; a mecânica interna é checagem de estado
corrente em vez de exclusivamente orientada a evento. Caso o usuário prefira a opção
(a) por auditabilidade do "momento exato" da recuperação, é uma troca pequena nesta
mesma fase — sinalizar na aprovação deste design.

## Modelo de dados

Novo módulo `EasyPanel.Modules.Alerting` (mesma forma dos demais: entidades POCO +
enums + DTOs + interfaces de serviço; `EasyPanel.Infrastructure` implementa).

### Enums

```csharp
public enum AlertSeverity { Informativa = 0, Atencao = 1, Critica = 2 }

public enum AlertRuleScopeType { Tenant = 0, Location = 1, Printer = 2, WindowsClient = 3 }

public enum AlertState { Open = 0, Acknowledged = 1, Resolved = 2 }

public enum NotificationChannel { Email = 0, Webhook = 1 }

public enum NotificationOutcome { Success = 0, Failure = 1, Suppressed = 2 }

public enum OutboxStatus { Pending = 0, Sent = 1, Suppressed = 2, FailedPermanently = 3 }
```

### `AlertRule : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| Name | string (≤200) | obrigatório |
| Description | string? (≤1000) | |
| IsActive | bool | default true |
| EventTypesCsv | string | `PrinterEventType` serializados como CSV de int (ex.: `"2,3"`); exposto como `IReadOnlyList<PrinterEventType>` no DTO/request |
| ScopeType | AlertRuleScopeType | |
| ScopeLocationId | Guid? | obrigatório sse ScopeType=Location |
| ScopePrinterId | Guid? | obrigatório sse ScopeType=Printer |
| ScopeWindowsClientId | Guid? | obrigatório sse ScopeType=WindowsClient |
| Severity | AlertSeverity | |
| ThresholdCount | int? | null = dispara na 1ª ocorrência |
| ThresholdWindowMinutes | int? | obrigatório sse ThresholdCount != null |
| AutoResolve | bool | default true |
| EmailEnabled | bool | |
| EmailRecipientsCsv | string? | obrigatório sse EmailEnabled |
| WebhookEnabled | bool | |
| WebhookUrl | string? | obrigatório e HTTPS sse WebhookEnabled (R5.3) |
| WebhookSecret | string? | nunca exposto em DTO de leitura |

Validação de escopo cross-tenant (R1.3): ao criar/editar, `ScopeLocationId`/
`ScopePrinterId`/`ScopeWindowsClientId` devem existir e pertencer ao tenant corrente
(a query já é auto-escopada pelo filtro global) — senão 404.

### `Alert : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| AlertRuleId | Guid | |
| Severity | AlertSeverity | cópia da regra no momento da criação (a regra pode mudar depois) |
| State | AlertState | |
| PrinterId | Guid? | |
| WindowsClientId | Guid? | |
| FirstOccurrenceAt / FirstOccurrenceAtTicks | DateTimeOffset / long | |
| LastOccurrenceAt / LastOccurrenceAtTicks | DateTimeOffset / long | cursor de listagem |
| OccurrenceCount | int | |
| AcknowledgedAt | DateTimeOffset? | |
| AcknowledgedByUserId | Guid? | |
| ResolvedAt | DateTimeOffset? | |
| ResolvedByUserId | Guid? | null quando `AutoResolved = true` |
| AutoResolved | bool | |
| ResolutionNote | string? | |

Índice único parcial conceitual (aplicado em código, não em constraint de BD por
portabilidade EF): no máximo um `Alert` em `Open`/`Acknowledged` por
`(TenantId, AlertRuleId, PrinterId, WindowsClientId)` — é isto que R2.5 exige
("associar o novo PrinterEvent ao Alert existente"). Verificado por consulta antes do
insert, dentro da mesma transação do `AlertEngine`.

### `AlertTransition : TenantEntity` (somente-adição)

`AlertId`, `FromState`, `ToState`, `ActorUserId?` (null = sistema), `OccurredAt`,
`Note?`.

### `AlertNotificationOutbox : TenantEntity`

`AlertId`, `Channel`, `Status (OutboxStatus)`, `AttemptCount`, `NextAttemptAt`.
Criada pelo `AlertEngine` somente quando um **novo** `Alert` é aberto (não a cada
ocorrência subsequente — decisão de design para não gerar spam de notificação a cada
evento repetido; ver seção "Notas/Fora de escopo").

### `AlertNotificationAttempt : TenantEntity` (somente-adição)

`AlertId`, `Channel`, `Outcome (NotificationOutcome)`, `AttemptNumber`,
`HttpStatusCode?` (webhook), `ErrorSummary?` (sem segredos), `AttemptedAt` /
`AttemptedAtTicks` (cursor).

### `AlertSilence : TenantEntity`

`AlertRuleId?`, `PrinterId?`, `WindowsClientId?` (ao menos um dos três obrigatório),
`StartsAt`, `EndsAt` (> StartsAt), `CreatedByUserId`, `Reason?`, `EndedEarlyAt?`,
`EndedEarlyByUserId?`. "Vigente" = `now` entre `StartsAt` e (`EndedEarlyAt` ??
`EndsAt`).

### Extensão à Fase 2

`PrinterEvent` (módulo `Monitoring`, já existente) ganha `CreatedAtTicks` (long,
= `CreatedAt.UtcTicks`), preenchido em toda gravação existente
(`HeartbeatMonitor`, `CollectionProcessor`). Índice
`IX_PrinterEvents_CreatedAtTicks` para suportar o cursor global do `AlertEngine`.

### Cursor global do motor

`AlertEngineCheckpoint : BaseEntity` (não `TenantEntity` — é um cursor de
plataforma, cross-tenant, mesmo raciocínio de `AuditLog.TenantId` anulável): linha
única, `LastProcessedEventCreatedAtTicks (long)`, `LastProcessedEventId (Guid)`.
Atualizado ao final de cada lote processado com sucesso.

## Motor de avaliação (`AlertEngine`)

`BackgroundService`, intervalo configurável (`Alerting:EngineScanIntervalSeconds`,
default 30s, mesmo padrão de `MonitoringOptions`). A cada ciclo:

1. **Lote de eventos**: busca até `BatchSize` (default 500) `PrinterEvent` com
   `(CreatedAtTicks, Id) > (checkpoint.Ticks, checkpoint.Id)`, ordenados por
   `CreatedAtTicks, Id` — consulta cross-tenant em contexto de sistema.
2. Para cada evento, resolve `LocationId` do alvo (via `Printer.LocationId` ou
   `WindowsClient.LocationId`, com pequeno cache em memória por ciclo) e busca as
   `AlertRule` ativas do mesmo `TenantId` cujo `EventTypesCsv` contenha o tipo do
   evento e cujo escopo corresponda (Tenant sempre casa; Location/Printer/
   WindowsClient exigem igualdade de id).
3. Para cada regra correspondente: se `ThresholdCount` definido, conta
   `PrinterEvent` do(s) mesmo(s) tipo(s)/alvo dentro de `ThresholdWindowMinutes`
   (usa o índice já existente `IX_PrinterEvents_TenantId_Type_OccurredAt` da Fase 2);
   dispara somente ao atingir o limiar. Sem limiar, dispara na primeira ocorrência.
4. Ao disparar: se já existe `Alert` `Open`/`Acknowledged` para
   `(AlertRuleId, PrinterId, WindowsClientId)`, apenas atualiza
   `LastOccurrenceAt`/`OccurrenceCount` (R2.5, sem nova notificação). Senão, cria o
   `Alert`, grava `AlertTransition` (None→Open, ator nulo) e, para cada canal ativo
   da regra, uma linha em `AlertNotificationOutbox`.
5. Atualiza o checkpoint global ao fim do lote (uma transação por lote, idempotente:
   reprocessar o mesmo lote após uma falha no meio apenas re-atualiza contadores ou
   reencontra o `Alert` já existente, nunca duplica).
6. **Resolução automática**: consulta `Alert` em `Open`/`Acknowledged` cuja
   `AlertRule.AutoResolve = true`, junta com `Printer.Status`/`WindowsClient.State`
   atuais; resolve (`AutoResolved = true`, `ResolvedByUserId = null`) os que já estão
   saudáveis (`PrinterStatus.Online` / `WindowsClientState.Active`), com
   `AlertTransition` correspondente.

Todo o processamento de um evento/regra permanece dentro do `TenantId` do próprio
evento — nunca combina dados de tenants distintos numa mesma avaliação (R8.5).

## Notificações

### `IAlertEmailSender` / `IAlertWebhookSender`

```csharp
public interface IAlertEmailSender
{
    Task<NotificationSendResult> SendAsync(AlertEmailMessage message, CancellationToken ct);
}

public interface IAlertWebhookSender
{
    Task<NotificationSendResult> SendAsync(AlertWebhookMessage message, CancellationToken ct);
}

public sealed record NotificationSendResult(bool Success, int? HttpStatusCode, string? ErrorSummary);
```

- `SmtpAlertEmailSender` (usa `System.Net.Mail.SmtpClient`) e
  `LogOnlyAlertEmailSender` (fallback quando `Smtp:Host` vazio) — seleção por DI
  condicional em `AlertingServiceCollectionExtensions`, mesmo padrão do
  `IPasswordResetNotifier` da Fase 1.
- `HttpAlertWebhookSender`: `POST` HTTPS com corpo JSON
  `{ alertId, tenantId, ruleId, severity, printerId?, windowsClientId?, occurredAt, detail }`,
  cabeçalho `X-EasyPanel-Signature: sha256=<hex(HMAC_SHA256(secret, body))>` (mesma
  ideia de assinatura usada no update assinado do agente, Fase 2) e timeout curto
  configurável. Sucesso = `2xx`.

### `AlertNotificationDispatcher` (BackgroundService)

Drena `AlertNotificationOutbox` com `Status = Pending` e `NextAttemptAt <= now`:

1. Verifica `AlertSilence` vigente para a regra/alvo do `Alert` — se vigente, marca
   o item `Suppressed` e grava `AlertNotificationAttempt` com
   `Outcome = Suppressed`, sem chamar o canal (R7.3).
2. Caso contrário, despacha via o `IAlertEmailSender`/`IAlertWebhookSender`
   correspondente ao `Channel` do item.
3. Grava `AlertNotificationAttempt` com o resultado.
4. Sucesso → `Status = Sent`. Falha → incrementa `AttemptCount`; se
   `< MaxAttempts` (default 5, configurável), agenda `NextAttemptAt` com backoff
   exponencial (ex.: `2^tentativa` minutos, teto configurável); ao esgotar,
   `Status = FailedPermanently` (fica no histórico para diagnóstico via R6).

Uma falha de envio nunca bloqueia a avaliação de outros alertas (R4.4/R5.5) — o
dispatcher e o `AlertEngine` são workers independentes.

## Endpoints da API

Convenção idêntica às Fases 1/2: `[ApiController]`, `[RequirePermission]`, DTOs
distintos das entidades, `MapFailure(Error)` por `ErrorType`, paginação
`PagedResult`/`PageRequest` para listagens simples e cursor (`string? cursor`,
`NextCursor`) para os volumes potencialmente grandes (`Alert`,
`AlertNotificationAttempt`).

### `AlertRulesController` — `api/v1/alert-rules`

- `GET` (lista paginada, filtros: `isActive`, `scopeType`, `eventType`) —
  `alert.view`
- `GET /{id}` — `alert.view`
- `POST` — `alert.manage`
- `PUT /{id}` — `alert.manage` (inclui ativar/desativar)

### `AlertsController` — `api/v1/alerts`

- `GET` (cursor pagination; filtros: `state`, `severity`, `alertRuleId`,
  `printerId`, `windowsClientId`, `from`, `to`) — `alert.view`
- `GET /{id}` (inclui histórico de `AlertTransition`) — `alert.view`
- `POST /{id}/acknowledge` — `alert.acknowledge`
- `POST /{id}/resolve` (corpo: `note?`) — `alert.acknowledge`
- `GET /{id}/notifications` (cursor pagination de `AlertNotificationAttempt`) —
  `alert.view`

### `AlertSilencesController` — `api/v1/alert-silences`

- `GET` (lista paginada) — `alert.view`
- `POST` — `alert.manage`
- `POST /{id}/end` (encerramento antecipado) — `alert.manage`

Todas as rotas herdam o comportamento cross-tenant → 404 já padronizado (consulta
sempre filtrada pelo `TenantId` do contexto autenticado via filtro global do EF).

## Permissões (RBAC)

Adiciona ao catálogo (`Permissions.cs`) e ao mapeamento (`RolePermissions.cs`):

- `alert.view` — Administrador, Financeiro, Operacional, Técnico, Supervisor
- `alert.manage` — Administrador, Supervisor
- `alert.acknowledge` — Administrador, Operacional, Técnico, Supervisor

(Mapeamento final de papéis é ajustável na implementação; segue o padrão já usado
para `printer.*`/`counter.*` na Fase 2 — visualização ampla, gestão/edição restrita a
papéis administrativos/supervisão.)

## Auditoria

Eventos sensíveis registrados via `IAuditLogger` (ação, ator, tenant, recurso,
antes/depois quando aplicável):

- `alertrule.create`, `alertrule.update` (R1.7)
- `alert.acknowledge`, `alert.resolve` (R3.8)
- `alertsilence.create`, `alertsilence.end` (R7.7)

`WebhookSecret` nunca aparece em `OldValues`/`NewValues` — redigido como as demais
credenciais (mesmo `AuditValueRedactor` da Fase 1).

## Migração e índices

Uma migração `AddAlerting`: novas tabelas (`AlertRules`, `Alerts`,
`AlertTransitions`, `AlertNotificationOutbox`, `AlertNotificationAttempts`,
`AlertSilences`, `AlertEngineCheckpoints`) + alteração em `PrinterEvents`
(`CreatedAtTicks`, backfill dos registros existentes a partir de `CreatedAt.UtcTicks`
na própria migração, e índice).

Índices (R6.3/R17.7 equivalente): `TenantId` em todas; `(TenantId, State,
LastOccurrenceAtTicks)` e `(TenantId, AlertRuleId, PrinterId, WindowsClientId,
State)` em `Alert`; `(TenantId, AttemptedAtTicks)` em
`AlertNotificationAttempt`; `(Status, NextAttemptAt)` em
`AlertNotificationOutbox`; `(TenantId, AlertRuleId)`/`(TenantId, PrinterId)`/
`(TenantId, WindowsClientId)` em `AlertSilence`; `CreatedAtTicks` global em
`PrinterEvent`.

## Correctness properties (para testes baseados em propriedade / cenários)

1. **Sem duplicação**: para qualquer sequência de N `PrinterEvent` correspondentes à
   mesma `(AlertRule, alvo)` dentro da janela de abertura, no máximo um `Alert`
   `Open`/`Acknowledged` existe simultaneamente.
2. **Isolamento**: nenhuma avaliação do `AlertEngine` cria/atualiza um `Alert`, ou lê
   uma `AlertRule`/`PrinterEvent`, cujo `TenantId` difira do `TenantId` do evento
   sendo avaliado.
3. **Progresso do checkpoint**: o cursor global nunca regride e nunca pula um
   `PrinterEvent` não processado (todo evento com `(ticks,id) > checkpoint` anterior
   é eventualmente avaliado).
4. **Silêncio não perde histórico**: um `Alert` gerado durante um `AlertSilence`
   vigente é persistido normalmente (R7.3) — apenas o outbox correspondente fica
   `Suppressed`, nunca `Failed`.
5. **Não-decréscimo de estado**: uma vez `Resolved`, um `Alert` só volta a `Open` via
   criação de um **novo** `Alert` (nunca é reaberto in-place) — preserva o histórico
   de `AlertTransition` como somente-adição verdadeiro.
6. **Sem segredo vazado**: `WebhookSecret` nunca aparece em nenhum DTO de resposta,
   log ou `AuditLog`.

## Notas / fora de escopo desta fase

- **Notificação apenas na abertura do Alert** (não a cada ocorrência subsequente
  agregada) — evita spam quando um alvo gera muitos `PrinterEvent` repetidos
  (ex.: falha de coleta recorrente). Se o usuário preferir renotificar
  periodicamente enquanto o alerta seguir aberto (ex.: a cada N horas), isso pode
  ser adicionado como um campo `RenotifyIntervalMinutes` opcional em fase futura sem
  quebrar o modelo atual.
- Alertas de suprimentos, contadores/consumo, canais adicionais e dashboards
  permanecem fora de escopo, conforme já declarado em `requirements.md`.
- `EasyPanel.Modules.Alerting` não referencia `EasyPanel.Modules.Monitoring`; toda
  leitura cruzada acontece em `EasyPanel.Infrastructure.Alerting`, preservando a
  regra de dependência do projeto.
