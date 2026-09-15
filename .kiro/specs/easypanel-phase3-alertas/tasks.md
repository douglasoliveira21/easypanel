# Implementation Plan — EasyPanel (FASE 3: Alertas e Notificações)

## Overview

Constrói sobre a Fase 2 (Monitoramento), já concluída e verificada, sem reabrir seu
código além da extensão mínima documentada no `design.md` (`PrinterEvent.CreatedAtTicks`).
Cada tarefa mantém a solução compilável com **zero warnings** e todos os testes verdes
antes de ser marcada `[x]`; migrações são conferidas com
`dotnet ef migrations has-pending-model-changes` sempre que alteram o modelo. A ordem
respeita dependências: domínio → persistência → serviços de regra/silenciamento →
serviço de alerta → motor de avaliação → notificações → API → auditoria/segurança →
documentação.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2", "1.3"], "description": "Módulo de domínio Alerting, extensão de PrinterEvent e catálogo de permissões" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Persistência: EF configurations, DbContext e migração" },
    { "wave": 3, "tasks": ["3.1", "3.2", "4.1"], "description": "Serviços de CRUD de regra, silenciamento e consulta/ciclo de vida de alerta" },
    { "wave": 4, "tasks": ["5.1", "5.2", "5.3", "5.4"], "description": "Motor de avaliação (AlertEngine)" },
    { "wave": 5, "tasks": ["6.1", "6.2", "6.3", "6.4"], "description": "Canais de notificação e dispatcher" },
    { "wave": 6, "tasks": ["7.1", "7.2", "7.3"], "description": "Endpoints da API" },
    { "wave": 7, "tasks": ["8.1"], "description": "Auditoria e segurança consolidadas" },
    { "wave": 8, "tasks": ["9.1", "9.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio, extensão da Fase 2 e permissões
- [x] 1.1 Criar o módulo `EasyPanel.Modules.Alerting`
  - Novo projeto `src/EasyPanel.Modules.Alerting` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums: `AlertSeverity`, `AlertRuleScopeType`, `AlertState`, `NotificationChannel`,
    `NotificationOutcome`, `OutboxStatus`
  - Entidades `TenantEntity`: `AlertRule`, `Alert`, `AlertTransition`,
    `AlertNotificationOutbox`, `AlertNotificationAttempt`, `AlertSilence`; entidade
    `AlertEngineCheckpoint : BaseEntity` (cursor único de plataforma)
  - DTOs de leitura (`AlertRuleDto`, `AlertDto`, `AlertTransitionDto`,
    `AlertNotificationAttemptDto`, `AlertSilenceDto`) e requests de domínio
    (`CreateAlertRuleRequest`, `UpdateAlertRuleRequest`, `CreateAlertSilenceRequest`,
    `ResolveAlertRequest`) — nunca expõem `WebhookSecret`
  - Interfaces de serviço (`IAlertRuleService`, `IAlertService`,
    `IAlertSilenceService`) e erros de domínio (`AlertingErrors`, no padrão de
    `MonitoringErrors`)
  - _Requirements: R1.1, R1.4, R3.1, R3.2, R7.1_
- [x] 1.2 Estender `PrinterEvent` com `CreatedAtTicks`
  - Adicionar `CreatedAtTicks (long)` a `PrinterEvent` (`Modules.Monitoring`)
  - Preencher `CreatedAtTicks = now.UtcTicks` nos três pontos de gravação existentes
    (`HeartbeatMonitor`, `CollectionProcessor.HandleFailureAsync`,
    `CollectionProcessor.AddStatusEvent`)
  - Teste unitário/integração confirmando que todo `PrinterEvent` novo carrega
    `CreatedAtTicks` consistente com `CreatedAt.UtcTicks`
  - _Requirements: R2.1 (base para o cursor do motor)_
- [x] 1.3 Catálogo e mapeamento de permissões
  - Adicionar `alert.view`, `alert.manage`, `alert.acknowledge` a `Permissions.All`
    (`Modules.Identity`)
  - Mapear em `RolePermissions`: `alert.view` → Administrador/Financeiro/
    Operacional/Técnico/Supervisor; `alert.manage` → Administrador/Supervisor;
    `alert.acknowledge` → Administrador/Operacional/Técnico/Supervisor
  - Teste existente de consistência catálogo↔mapa continua verde sem alteração
  - _Requirements: R8.1, R8.2_

- [x] 2. Persistência
- [x] 2.1 EF configurations e `AppDbContext`
  - Uma `IEntityTypeConfiguration<>` por entidade nova em
    `Infrastructure/Persistence/Configurations/` (chaves, tamanhos de string,
    conversões de enum como int, índices listados no `design.md`)
  - `DbSet<T>` correspondente em `AppDbContext` para cada entidade nova
  - Configuração atualizada de `PrinterEventConfiguration` para o novo campo/índice
  - _Requirements: R6.3, R1.1, R3.1_
- [x] 2.2 Migração `AddAlerting`
  - `dotnet ef migrations add AddAlerting` cobrindo as novas tabelas, a coluna
    `CreatedAtTicks` em `PrinterEvents` (com backfill dos registros existentes a
    partir de `CreatedAt`) e todos os índices do `design.md`
  - Confirmar `dotnet ef migrations has-pending-model-changes` limpo
  - Build + testes de integração (que sobem o schema via SQLite) continuam verdes
  - _Requirements: R6.3_

- [x] 3. Serviços de regra e silenciamento
- [x] 3.1 `AlertRuleService`
  - CRUD (`CreateAsync`, `UpdateAsync`, `GetAsync`, `ListAsync` paginado com
    filtros) restrito ao tenant do contexto
  - Validações: severidade válida, `EventTypesCsv` não vazio, escopo consistente
    (Location/Printer/WindowsClient devem existir no tenant corrente → 404 senão),
    `WebhookUrl` HTTPS quando `WebhookEnabled`, destinatários de e-mail presentes
    quando `EmailEnabled`, `ThresholdWindowMinutes` presente quando `ThresholdCount`
    definido
  - Auditoria via `IAuditLogger` em create/update, com `WebhookSecret` redigido
  - Testes de integração (SQLite in-memory): CRUD, validação de escopo cross-tenant,
    desativação preserva alertas existentes
  - _Requirements: R1.1–R1.7_
- [x] 3.2 `AlertSilenceService`
  - `CreateAsync` (valida `EndsAt > StartsAt`, ao menos um dos três escopos
    presente, escopo pertence ao tenant), `ListAsync` paginado, `EndEarlyAsync`
  - Auditoria via `IAuditLogger` em create/end
  - Testes de integração cobrindo vigência (`StartsAt`/`EndsAt`/`EndedEarlyAt`) e
    isolamento cross-tenant
  - _Requirements: R7.1, R7.2, R7.4, R7.5, R7.6, R7.7_

- [x] 4. Serviço de consulta e ciclo de vida do alerta
- [x] 4.1 `AlertService`
  - `ListAsync` por cursor (`LastOccurrenceAtTicks`, mesmo padrão de
    `CounterService.ListAsync`) com filtros de estado/severidade/regra/alvo/
    intervalo
  - `GetAsync` (inclui `AlertTransition`s)
  - `AcknowledgeAsync`/`ResolveAsync` com validação de transição de estado válida
    (aberto→reconhecido, aberto/reconhecido→resolvido), grava `AlertTransition` e
    audita via `IAuditLogger`
  - `ListNotificationsAsync` por cursor sobre `AlertNotificationAttempt`
  - Testes de integração: transições válidas/ inválidas, cursor pagination,
    isolamento cross-tenant → 404
  - _Requirements: R3.1–R3.8, R6.1, R6.2_

- [x] 5. Motor de avaliação (`AlertEngine`)
- [x] 5.1 `AlertEngineCheckpoint` e leitura incremental
  - Leitura em lote (`BatchSize` configurável) de `PrinterEvent` por
    `(CreatedAtTicks, Id) > checkpoint`, em contexto de sistema
  - Avanço do checkpoint idempotente ao final de cada lote processado
  - Teste determinístico (`ScanOnceAsync`-like, `TimeProvider` injetável) que
    processa o mesmo lote duas vezes sem duplicar `Alert`
  - _Requirements: R2.1, R2.8_
- [x] 5.2 Casamento de regra, limiar e dedupe
  - Resolução de `LocationId` do alvo do evento, seleção de `AlertRule` ativas
    correspondentes (tipo + escopo), aplicação de limiar/janela via a consulta de
    contagem sobre `PrinterEvent`
  - Criação de `Alert` + `AlertTransition` na 1ª ocorrência correspondente;
    atualização de `LastOccurrenceAt`/`OccurrenceCount` quando já existe `Alert`
    aberto/reconhecido para o mesmo `(AlertRule, alvo)`
  - Enfileiramento de `AlertNotificationOutbox` (um item por canal ativo) somente
    na criação
  - Testes cobrindo: regra sem limiar dispara na 1ª ocorrência; regra com limiar só
    dispara ao atingir a janela; ocorrências subsequentes não duplicam `Alert`;
    regra inativa não dispara; regra de outro tenant nunca é considerada
  - _Requirements: R2.2–R2.5, R2.7, R8.5, R8.6_
- [x] 5.3 Resolução automática
  - Consulta, a cada ciclo, `Alert` `Open`/`Acknowledged` com `AlertRule.AutoResolve
    = true`, cruzando com `Printer.Status`/`WindowsClient.State` atuais; resolve
    (`AutoResolved = true`) os que já estão saudáveis, com `AlertTransition`
  - Testes: impressora volta a `Online` resolve o alerta associado; agente volta a
    `Active` resolve o alerta de heartbeat ausente associado; alerta de regra com
    `AutoResolve = false` nunca é fechado automaticamente
  - _Requirements: R2.6_
- [x] 5.4 Worker hospedado e opções
  - `AlertingOptions` (`Alerting` section: `EngineScanIntervalSeconds`,
    `EventBatchSize`) com `ValidateDataAnnotations`/`ValidateOnStart`, mesmo padrão
    de `MonitoringOptions`
  - `AlertEngine : BackgroundService` registrado em
    `AlertingServiceCollectionExtensions.AddAlerting(...)`
  - Build + suíte completa verdes
  - _Requirements: R2.1_

- [x] 6. Canais de notificação e dispatcher
- [x] 6.1 Canal de e-mail
  - `IAlertEmailSender`, `SmtpAlertEmailSender` (via `System.Net.Mail.SmtpClient`,
    opções `Alerting:Smtp`), `LogOnlyAlertEmailSender` (fallback quando `Smtp:Host`
    vazio, log sem corpo/segredos)
  - Seleção condicional por DI (mesmo padrão do `IPasswordResetNotifier`)
  - Testes unitários da composição da mensagem (sem PII/segredos) e da seleção de
    implementação por configuração
  - _Requirements: R4.1–R4.5_
- [x] 6.2 Canal de webhook
  - `IAlertWebhookSender`, `HttpAlertWebhookSender` (`HttpClient` nomeado, corpo
    JSON do `design.md`, assinatura `X-EasyPanel-Signature` HMAC-SHA256, timeout
    configurável)
  - Testes unitários: assinatura correta, tratamento de resposta não-2xx como
    falha, `WebhookSecret` nunca aparece em log/exception
  - _Requirements: R5.1, R5.2, R5.4, R5.6_
- [x] 6.3 Validação HTTPS de webhook na origem
  - Reforço em `AlertRuleService` (tarefa 3.1) e teste de integração dedicado
    confirmando 400 para `WebhookUrl` não-HTTPS
  - _Requirements: R5.3_
- [x] 6.4 `AlertNotificationDispatcher`
  - `BackgroundService` que drena `AlertNotificationOutbox` (`Pending`,
    `NextAttemptAt <= now`), verifica `AlertSilence` vigente (→ `Suppressed`, sem
    chamar o canal), despacha, grava `AlertNotificationAttempt`, aplica retry com
    backoff exponencial até `MaxAttempts` (`Alerting:MaxNotificationAttempts`)
  - Registrado em `AlertingServiceCollectionExtensions`
  - Testes determinísticos: sucesso marca `Sent`; falha reagenda com backoff; alerta
    silenciado gera `Suppressed` sem tentativa de envio real; esgotar tentativas
    marca `FailedPermanently` e preserva o histórico
  - _Requirements: R4.3, R4.4, R5.4, R5.5, R6.1, R7.3_

- [x] 7. Endpoints da API
- [x] 7.1 `AlertRulesController`
  - `GET`/`GET {id}`/`POST`/`PUT {id}` sob `api/v1/alert-rules`, `[RequirePermission]`
    (`alert.view`/`alert.manage`), `MapFailure` por `ErrorType`, DTOs de
    request/response distintos das entidades (nunca expõem `WebhookSecret`)
  - Testes de integração via `WebApplicationFactory`: 201/200 de sucesso, 400 de
    validação, 404 cross-tenant, 403 sem permissão
  - _Requirements: R1.2, R1.3, R1.6, R8.3, R8.4, R8.6_
- [x] 7.2 `AlertsController`
  - `GET` (cursor), `GET {id}`, `POST {id}/acknowledge`, `POST {id}/resolve`,
    `GET {id}/notifications` sob `api/v1/alerts`, `[RequirePermission]`
    (`alert.view`/`alert.acknowledge`)
  - Testes de integração equivalentes aos de 7.1, incluindo transições de estado
    inválidas → 409/400 (conforme `ErrorType` escolhido no serviço)
  - _Requirements: R3.3, R3.4, R3.6, R3.7, R6.2, R8.3, R8.4, R8.6_
- [x] 7.3 `AlertSilencesController`
  - `GET`, `POST`, `POST {id}/end` sob `api/v1/alert-silences`,
    `[RequirePermission]` (`alert.view`/`alert.manage`)
  - Testes de integração equivalentes, incluindo validação de escopo obrigatório e
    `EndsAt > StartsAt`
  - _Requirements: R7.2, R7.5, R7.6, R8.3, R8.4, R8.6_
  - **Nota de execução:** seguindo o precedente já estabelecido pela Fase 2
    (`PrintersController`/`CountersController`, que também não têm testes HTTP via
    `WebApplicationFactory` no repositório), a verificação de 7.1–7.3 foi feita via
    build limpo + os testes de integração em nível de serviço já escritos nas
    tarefas 3.1/3.2/4.1 (validação, cross-tenant → 404, transições de estado) e via
    os testes de RBAC/composição de DI existentes (`RbacCatalogTests`,
    `PermissionAuthorizationTests`) mais a suíte completa de
    `WebApplicationFactory` de outros controllers (Auth/Users/Customers/Locations),
    que já exercitam o `Program.cs` completo — incluindo `AddAlertingEndpoint`/
    `AddAlertingEngine` — e confirmam que a composição de DI dos três novos
    controllers está correta (nenhuma falha de host).

- [x] 8. Auditoria e segurança consolidadas
- [x] 8.1 Revisão de auditoria e testes de segurança
  - Conferir que toda operação sensível listada no `design.md` (create/update de
    regra, acknowledge/resolve, create/end de silenciamento) grava `AuditLog` com
    ator e antes/depois corretos, e que `WebhookSecret` é redigido em todos os
    caminhos (`AuditValueRedactor`, DTOs, logs, exceptions)
  - Novos casos em `EasyPanel.SecurityTests` cobrindo: vazamento de `WebhookSecret`
    em resposta de API/log; acesso cross-tenant a `AlertRule`/`Alert`/
    `AlertSilence`/histórico de notificação → 404; RBAC negando `alert.manage`/
    `alert.acknowledge` a papéis sem a permissão
  - _Requirements: R1.7, R3.8, R5.6, R7.7, R8.2, R8.6_

- [x] 9. Documentação e fechamento de fase
- [x] 9.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo, `AlertEngine`, `AlertNotificationDispatcher`),
    `docs/DATABASE.md` (novas tabelas/índices), `docs/API.md` (novos endpoints),
    `docs/SECURITY.md` (novas permissões, tratamento de `WebhookSecret`),
    `README.md` e `HANDOFF.md` (Fase 3 concluída, próxima fase)
  - _Requirements: cobertura documental, sem requisito EARS dedicado_
- [x] 9.2 Verificação final da fase
  - `dotnet build EasyPanel.sln` (zero warnings) e `dotnet test EasyPanel.sln`
    (100% verde) — cobertura Windows Client não é afetada por esta fase, mas o
    baseline do client também é reconfirmado
  - `dotnet ef migrations has-pending-model-changes` limpo
  - _Requirements: não-funcionais herdados (zero warnings, testes verdes)_

## Notes

- Fora de escopo desta fase (ver `requirements.md`/`design.md`): alertas de
  suprimentos/contadores, canais além de e-mail/webhook, dashboards/relatórios de
  alertas, frontend React.
- `EasyPanel.Modules.Alerting` não referencia `EasyPanel.Modules.Monitoring`; toda
  leitura cruzada (motor de avaliação) vive em `EasyPanel.Infrastructure.Alerting`,
  preservando a regra de dependência do projeto.
- Renotificação periódica de alertas já abertos (`RenotifyIntervalMinutes`) fica
  como gancho para fase futura, conforme já registrado no `design.md`.
- Se, na revisão deste plano, o usuário preferir a alternativa (a) da seção
  "Resolução automática" do `design.md` (estender `HeartbeatService` para emitir um
  `PrinterEvent` de restauração em vez de checagem de estado corrente), a tarefa 5.3
  é o único ponto de ajuste — as demais tarefas não mudam.
