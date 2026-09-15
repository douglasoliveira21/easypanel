# Arquitetura — EasyPanel (Fases 1–9)

## Estilo

Monólito modular em .NET 10. Cada módulo de negócio expõe apenas contratos
públicos (interfaces + DTOs de domínio + entidades) e mantém regras encapsuladas.
A infraestrutura implementa as abstrações e concentra EF Core, Identity, tokens,
migrações e observabilidade. A separação prepara a futura extração para
microsserviços sem reescrita.

### Regra de dependência

```
Api  →  Modules.*  →  Shared.Kernel
                     ↑
        Infrastructure (implementa abstrações; referenciada pela Api só para DI)
```

- Nenhum módulo de negócio referencia outro por implementação.
- `Modules.Identity` referencia `Modules.Tenancy` apenas para reusar o nome
  canônico do papel Super Admin (dependência acíclica).

## Módulos

| Módulo | Responsabilidade |
|--------|------------------|
| `Shared.Kernel` | `BaseEntity`/`TenantEntity`, `Result`/`Result<T>`/`Error`, `PagedResult`/`PageRequest`, exceções de domínio |
| `Modules.Tenancy` | Entidade `Tenant`, `ITenantContext`, `TenantResolutionMiddleware`, constantes |
| `Modules.Identity` | `ApplicationUser`/`ApplicationRole`, tokens (`ITokenService`), auth (`IAuthService`), RBAC (permissões/papéis, `RequirePermission`), usuários (`IUserService`) |
| `Modules.Customers` | `Customer`/`Location`, validação de CNPJ, `ICustomerService`/`ILocationService` |
| `Modules.Auditing` | `AuditLog`, `IAuditLogger`, consulta e emissão de eventos |
| `Modules.Monitoring` (Fase 2) | `WindowsClient`/`Printer`/`PrinterCounter`/`PrinterEvent`/`Collection`/`PrinterMovement`, contratos de agente (auth/registro/heartbeat/ingestão), impressoras/contadores, config/update, `IPrinterDriver` |
| `Modules.Alerting` (Fase 3) | `AlertRule`/`Alert`/`AlertTransition`/`AlertNotificationOutbox`/`AlertNotificationAttempt`/`AlertSilence`/`AlertEngineCheckpoint`, contratos de regra/alerta/silenciamento (`IAlertRuleService`/`IAlertService`/`IAlertSilenceService`). Não referencia `Modules.Monitoring`: impressoras/agentes são referenciados apenas por `Guid` |
| `Modules.Monitoring` (Fase 4) | Estendido (não um módulo novo): `SupplyReading`/`SupplyThreshold`, `PrinterEventType.SupplyLow`, `ISupplyService`, `ClientConfig.MonitoredPrinters`, envelope `{Counters, Supplies}` no contrato de ingestão |
| `Modules.Inventory` (Fase 5) | `InventoryItem`/`InventoryMovement`/`InventoryBalance`/`InventoryMinimum`, contratos de item/movimentação/saldo/mínimo (`IInventoryItemService`/`IInventoryMovementService`). Não referencia `Modules.Customers`/`Modules.Monitoring`: local e impressora são referenciados apenas por `Guid` |
| `Modules.Ticketing` (Fase 6) | `Ticket`/`TicketInteraction`/`SlaPolicy`/`TicketAttachment`, contratos de chamado/SLA/anexo (`ITicketService`/`ISlaPolicyService`/`ITicketAttachmentService`). Não referencia `Modules.Customers`/`Modules.Monitoring`/`Modules.Identity`: cliente, local, impressora e usuário são referenciados apenas por `Guid` |
| `Shared.Kernel` (Fase 6) | Ganha `IFileStorage` (`Storage/`): abstração de armazenamento por chave, independente de provedor, sem dependência de SDK — implementada sobre o MinIO na Infrastructure |
| `Modules.Contracts` (Fase 7) | `Contract`/`ContractLocation`/`ContractPrinter`/`ContractFranchise`, contratos de contrato/escopo/franquia (`IContractService`/`IContractScopeService`/`IContractFranchiseService`). Não referencia `Modules.Customers`/`Modules.Monitoring`: cliente, local e impressora são referenciados apenas por `Guid`. Base para a Fase 8 (Fechamento e Faturamento) — nenhum cálculo de consolidação de contador acontece aqui |
| `Modules.Billing` (Fase 8) | `BillingClosing`/`Invoice`/`InvoiceLineItem`, contratos de fechamento/fatura (`IBillingClosingService`/`IInvoiceService`). Não referencia `Modules.Monitoring`/`Modules.Contracts`: impressora e contrato são referenciados apenas por `Guid`. Primeiro tipo monetário real usado em cálculo (`InvoiceLineItem.LineAmount`) |
| `Modules.Reporting` (Fase 9) | Nenhuma entidade — só DTOs de agregação e contratos de painel/relatório (`IDashboardService`/`IPrintConsumptionReportService`/`IBillingReportService`/`ISlaReportService`). Não referencia nenhum outro módulo de domínio |
| `Modules.Portal` (Fase 10) | Nenhuma entidade — só DTOs de leitura e contratos do Portal do Cliente (`IPortalFleetService`/`IPortalTicketService`/`IPortalInvoiceService`). Não referencia nenhum outro módulo de domínio |
| `Infrastructure` | `AppDbContext` (IdentityDbContext), configurações EF, migrações, implementações de serviços, health checks, seeders. Fase 2: autenticação do agente (`ClientBearer`), serviços de monitoramento e workers (`HeartbeatMonitor`, `CollectionProcessingWorker`). Fase 3: motor de avaliação (`AlertEngine`), despachante de notificação (`AlertNotificationDispatcher`) e canais (`IAlertEmailSender`/`IAlertWebhookSender`) — único componente que cruza `Modules.Monitoring` e `Modules.Alerting`. Fase 5: `InventoryItemService`/`InventoryMovementService` validam local/impressora do tenant cruzando `Modules.Inventory` com `Modules.Customers`/`Modules.Monitoring`. Fase 6: `MinioFileStorage` (primeira implementação funcional do storage, `Storage/`), `TicketService`/`TicketAttachmentService` cruzando `Modules.Ticketing` com `Modules.Customers`/`Modules.Monitoring`/`Modules.Identity`. Fase 7: `ContractService`/`ContractScopeService` cruzando `Modules.Contracts` com `Modules.Customers`/`Modules.Monitoring` para validar Cliente/Local/Impressora e resolver o contrato aplicável (R4). Fase 8: `BillingClosingService` cruzando `Modules.Billing` com `Modules.Monitoring` (`PrinterCounter`) e `Modules.Contracts` (`ResolveApplicableAsync`, `ContractFranchise`) para consolidar consumo e calcular excedente. Fase 9: `DashboardService`/`PrintConsumptionReportService`/`BillingReportService`/`SlaReportService` (`Reporting/`) cruzam `Modules.Reporting` com `Modules.Monitoring`, `Modules.Alerting`, `Modules.Ticketing`, `Modules.Inventory` e `Modules.Billing` — o cruzamento de mais módulos de qualquer serviço da plataforma até agora, todos ao vivo, sem cache. Fase 10: `PortalFleetService`/`PortalTicketService`/`PortalInvoiceService` (`Portal/`) cruzam `Modules.Portal` com `Modules.Monitoring`, `Modules.Ticketing` e `Modules.Billing`, todos restringidos por um segundo filtro explícito de `CustomerId` (via `ICustomerContext`), além do filtro global de tenant |
| `Api` | Composição de DI, controllers, middlewares (correlação, erros), rate limiting, observabilidade, Swagger |

## Isolamento multi-tenant

O `tenant_id` é sempre derivado do token autenticado (nunca do cliente):

1. `TenantResolutionMiddleware` lê o claim `tenant_id` (ou o header admin, apenas
   para Super Admin) e popula um `ITenantContext` scoped.
2. `AppDbContext` aplica um **query filter global** por `TenantId` a toda entidade
   `TenantEntity` (leituras auto-escopadas).
3. Um interceptor de `SaveChanges` **carimba** o `TenantId` em inserts e **rejeita**
   updates/deletes cross-tenant (`CrossTenantAccessException` → HTTP 404).

Entidades não tenant-scoped (`ApplicationUser`, `AuditLog`) filtram por tenant
explicitamente nos serviços, pois têm `TenantId` anulável por razões específicas.

## Pipeline de requisição

```
CorrelationId → ExceptionHandling → HTTPS → Authentication (JWT)
→ RateLimiter → Authorization (permissões) → TenantResolution → Controllers
```

- **Correlação (R11.3):** gera/propaga `X-Correlation-Id`, abre escopo de log.
- **Erros (R11.4):** exceções de domínio → ProblemDetails (400/403/404/409); não
  tratadas → 500 genérico (detalhe só no log).
- **Autenticação:** Bearer JWT; expirado/inválido → 401.
- **Rate limiting (R4.6/R4.7):** partição por usuário/tenant/IP + endpoint; 429 +
  `Retry-After` ao exceder.
- **Autorização (R5):** políticas dinâmicas `perm:<permission>`, avaliadas no backend.

## Padrões transversais

- **Result pattern:** serviços retornam `Result`/`Result<T>` para erros esperados;
  a API mapeia por `ErrorType` (e o middleware trata exceções que escapam).
- **DTOs:** contratos de API distintos das entidades (nunca expõem hash/stamps).
- **Paginação:** `PageRequest` com `PageSize` limitado a 100.
- **Auditoria append-only:** eventos sensíveis (login, mudanças de usuário, acesso
  cross-tenant recusado) via `IAuditLogger`, com redaction de segredos.

## Monitoramento e agente Windows (Fase 2)

- **Autenticação própria do agente:** esquema `ClientBearer` com audience dedicado
  (`easypanel:client`), reutilizando a chave de assinatura do JWT. Tenant/local são
  resolvidos exclusivamente da identidade do agente e propagados ao `ITenantContext`,
  reaproveitando o mesmo isolamento multi-tenant do backend.
- **Registro:** anônimo, validado por chave de provisionamento de Local (hasheada).
  Grava o `WindowsClient` via um contexto de sistema (Super Admin) porque não há
  requisição autenticada, mas o `TenantId` vem sempre da chave validada.
- **Ingestão idempotente:** `IdempotencyKey` única por tenant (índice único em
  `Collection` + backstop `IngestionDedup`). O endpoint retorna ack imediato (202);
  o processamento é assíncrono.
- **Pipeline de coletas:** `CollectionProcessingWorker` (polling sobre PostgreSQL)
  consome coletas e delega ao `ICollectionProcessor`, que persiste contadores
  (não-decrescentes), status e eventos de forma atômica e idempotente, com retry por
  `AttemptCount` sem descarte prematuro. A abstração permite trocar por Hangfire/Redis.
- **Heartbeat:** `HeartbeatMonitor` marca agentes sem heartbeat como
  `HeartbeatMissing` e gera `PrinterEvent` (fonte do motor de alertas da Fase 3).
- **Agente Windows (solução-satélite):** `src/windows-client/` — Worker Service
  independente (instância única por mutex, Guardian de auto-recovery, fila SQLite
  offline com reenvio por backoff, descoberta/SNMP com drivers por fabricante,
  auto-update assinado com verificação de hash/assinatura e rollback). Consome apenas
  a API HTTP; versionamento e build próprios (`EasyPanel.WindowsClient.slnx`).

## Alertas e Notificações (Fase 3)

- **Motor de avaliação (`AlertEngine`):** worker periódico que consome
  `PrinterEvent` de forma incremental via um cursor único de plataforma
  (`AlertEngineCheckpoint`, ordenado por `PrinterEvent.CreatedAtTicks` — coluna
  portável adicionada nesta fase), casa contra as `AlertRule` ativas do mesmo
  tenant do evento (tipo + escopo Tenant/Local/Impressora/Agente), aplica
  limiar/janela quando configurado e cria/atualiza `Alert`. Nunca combina dados de
  tenants distintos numa mesma avaliação. Notifica apenas na abertura de um novo
  Alerta (ocorrências subsequentes agregam `OccurrenceCount`, sem reenvio).
- **Resolução automática:** a cada ciclo, o `AlertEngine` verifica o estado
  corrente (`Printer.Status`/`WindowsClient.State`) dos alvos de Alertas
  abertos/reconhecidos cuja regra tem `AutoResolve = true`, resolvendo-os quando já
  saudáveis — checagem de estado corrente em vez de depender de um evento
  específico de recuperação (a Fase 2 não emite um ao agente voltar de heartbeat
  ausente, apenas restaura o estado).
- **Notificação assíncrona:** o `AlertEngine` enfileira `AlertNotificationOutbox`
  (um item por canal ativo da regra) somente na criação do Alerta. O
  `AlertNotificationDispatcher` drena a fila, verifica `AlertSilence` vigente no
  momento do envio (silenciado → `Suppressed`, sem chamar o canal), despacha via
  `IAlertEmailSender`/`IAlertWebhookSender` e registra cada tentativa em
  `AlertNotificationAttempt`, com retry por backoff exponencial até
  `MaxNotificationAttempts`.
- **Canal de e-mail:** `SmtpAlertEmailSender` (via `System.Net.Mail.SmtpClient`,
  sem dependência de terceiros) quando `Alerting:Smtp:Host` está configurado;
  `LogOnlyAlertEmailSender` como fallback (mesmo padrão do
  `IPasswordResetNotifier` da Fase 1) quando não está.
- **Canal de webhook:** `HttpAlertWebhookSender` via `HttpClient` nomeado; corpo
  JSON assinado por `X-EasyPanel-Signature: sha256=<HMAC-SHA256>` usando o
  segredo compartilhado da regra. A URL deve ser HTTPS (validado na criação da
  regra); o segredo nunca é exposto em DTOs, logs ou auditoria.
- **Silenciamento:** `AlertSilence` (por regra e/ou impressora/agente) suprime
  apenas o despacho de notificação durante sua vigência — o Alerta continua sendo
  registrado normalmente.

## Suprimentos e orquestração real do agente (Fase 4)

- **Backend — suprimento:** `SupplyReading` (somente-adição, mesmo padrão de
  `PrinterCounter`) e `SupplyThreshold` (limiar em cascata: Impressora+Rótulo →
  Tenant+Rótulo → Tenant geral → padrão de plataforma). O `CollectionProcessor`
  grava `PrinterEventType.SupplyLow` na transição de cruzamento do limiar — o
  `AlertEngine` da Fase 3 consome esse evento **sem nenhuma alteração**, já que
  casa por tipo de evento + escopo sem conhecer a origem.
- **Previsão de troca:** regressão linear simples (mínimos quadrados) sobre as
  últimas leituras de um (Impressora, rótulo); sem queda observada ou amostra
  insuficiente → previsão indisponível, sem erro.
- **Lacuna fechada do agente Windows:** até a Fase 4, o `Guardian` do agente
  supervisionava uma lista **vazia** de serviços internos — nenhum ciclo de coleta
  rodava de fato, e não havia implementação real de `IBackendClient` nem de
  `ISnmpCollector` (só as interfaces). A Fase 4 entrega as três peças fechando o
  ciclo de ponta a ponta:
  - `HttpBackendClient` (autenticação por client_id/secret, renovação de token,
    reautenticação em 401);
  - `SnmpCollector` sobre `Lextm.SharpSnmpLib` (SNMP v1/v2c real por UDP, consulta
    OID a OID para tolerar OIDs especulativos ausentes sem falhar a leitura
    inteira);
  - `Net_Monitoring_Service`/`Communication_Service` como `ISupervisedService`
    reais, supervisionados pelo `Guardian` (que também os inicia, via
    `RestartAsync`, na primeira varredura).
- **Leitura de suprimento genérica:** `ManufacturerDriverBase.Interpret` lê níveis
  de suprimento pela Printer-MIB padrão (RFC 3805), sem especificidade de
  fabricante — todos os 9 drivers ganham suporte automaticamente.

## Estoque (Fase 5)

- **Novo módulo `Modules.Inventory`:** catálogo (`InventoryItem`), movimentação
  append-only (`InventoryMovement`, tipo Entrada/Saída/Ajuste), saldo
  materializado por (Item, Local) (`InventoryBalance`) e estoque mínimo por
  (Item, Local) (`InventoryMinimum`). Local e Impressora são referenciados
  apenas por `Guid` — o módulo não referencia `Modules.Customers` nem
  `Modules.Monitoring`; toda validação cruzada acontece em
  `Infrastructure.Inventory`.
- **Saldo transacional:** `InventoryMovementService.RegisterAsync` grava o
  `InventoryMovement` e atualiza o `InventoryBalance` na mesma operação
  (`SaveChangesAsync` único). Entrada soma, Saída subtrai (409 se saldo
  insuficiente), Ajuste soma ou subtrai conforme `AdjustmentDirection`
  (exige `Reason`), sempre com piso em zero — o saldo nunca fica negativo.
- **Fora de escopo (decisão registrada em `requirements.md`/`design.md`):**
  alertas automáticos de estoque baixo (reuso do `AlertEngine`), pedido de
  compra/reposição e baixa automática a partir de leitura SNMP de suprimento —
  não há vínculo direto entre `InventoryItem` e `Printer`/`WindowsClient` como
  existe para `PrinterEvent`.

## Chamados/Helpdesk e SLA (Fase 6)

- **Novo módulo `Modules.Ticketing`:** `Ticket` (com prazos de SLA calculados na
  abertura), `TicketInteraction` (histórico append-only de comentário/mudança
  de status/atribuição), `SlaPolicy` (por tenant+prioridade) e
  `TicketAttachment`. Cliente, Local, Impressora e usuários (solicitante/
  atribuído/ator) são referenciados apenas por `Guid` — o módulo não
  referencia `Modules.Customers`/`Modules.Monitoring`/`Modules.Identity`;
  toda validação cruzada acontece em `Infrastructure.Ticketing`.
- **Máquina de estados do chamado:** transições de status validadas contra
  uma tabela fixa (`Aberto → {EmAndamento, Cancelado}`, etc.) — qualquer
  transição fora dela é recusada (400), nunca aplicada parcialmente.
- **SLA sem Contrato:** como `Modules.Contracts` ainda não existe (Fase 7),
  a política de SLA é por (tenant, prioridade), não por contrato. Os prazos
  são calculados **na abertura** do chamado e nunca recalculados
  retroativamente se a política mudar depois — mudar a política afeta apenas
  chamados abertos a partir daquele momento.
- **Primeiro uso funcional do storage:** até a Fase 6, o MinIO só era
  verificado por health check (`StorageHealthCheck`), sem nenhum código que
  de fato gravasse ou lesse um objeto. Esta fase entrega `IFileStorage`
  (abstração em `Shared.Kernel`, independente de SDK) e `MinioFileStorage`
  (implementação sobre o SDK oficial `Minio`, S3-compatible) para anexos de
  chamado — upload/download passam pelo backend como stream, sem presigned
  URL nesta fase. Um anexo só é persistido nos metadados depois que o objeto
  correspondente foi gravado com sucesso no storage (nunca fica órfão).

## Contratos (Fase 7)

- **Novo módulo `Modules.Contracts`:** `Contract` (vigência + ciclo de vida),
  escopo opcional por `ContractLocation`/`ContractPrinter` (vazio = cobre
  todo o Cliente) e `ContractFranchise` (quantidade incluída + preço único
  de excedente por tipo de contador). Base para a Fase 8 (Fechamento e
  Faturamento) — **nenhum cálculo de consolidação de contador nem geração
  de fatura** acontece aqui.
- **Resolução em cascata (R4):** `ResolveApplicableAsync` prioriza, nesta
  ordem, o Contrato `Ativo` e vigente vinculado diretamente à Impressora,
  depois ao Local da Impressora, depois o Contrato sem escopo (cobre todo o
  Cliente) — mesmo raciocínio do `SupplyThreshold` (Fase 4). Determinístico
  porque a adição de escopo valida ausência de sobreposição de vigência no
  mesmo Local/Impressora entre contratos não-encerrados do mesmo Cliente.
- **Primeiro tipo monetário da plataforma:** `ContractFranchise.ExcessUnitPrice`
  (`decimal(18,4)`) é o primeiro campo monetário em todo o código — sem
  precedente anterior. Moeda fixa `"BRL"` (campo `Currency`, já modelado
  para multi-moeda futura).
- **`ContractCounterType` espelha `Monitoring.CounterType`:** mesmos valores
  inteiros, mantido como um enum separado para não criar referência de
  módulo cruzada — só `Infrastructure.Contracts` (e, futuramente,
  `Infrastructure` da Fase 8) sabe que os dois correspondem à mesma
  semântica de contador.
- **Datas de vigência via colunas `*Ticks`:** `StartDateTicks`/
  `EndDateTicks` (long) acompanham `StartDate`/`EndDate` (`DateTimeOffset`)
  pelo mesmo motivo já documentado nas fases anteriores — SQLite não traduz
  comparação sobre `DateTimeOffset`, e a resolução de R4 depende de
  comparação de intervalo (`StartDateTicks <= referência <= EndDateTicks`).

## Fechamento e Faturamento (Fase 8)

- **Novo módulo `Modules.Billing`:** `BillingClosing` (registro somente-adição
  de execução, com constraint única `(TenantId, Ano, Mês)` que impede
  refechamento silencioso), `Invoice` (uma por Contrato com excedente > 0 no
  período), `InvoiceLineItem` (um por (Impressora, CounterType) com
  excedente > 0; itens de excedente zero nunca são persistidos).
- **Cálculo de consumo via odômetro, não soma de deltas:** `PrinterCounter.
  Value` é cumulativo e não-decrescente por (Impressora, CounterType) — o
  consumo do período é sempre `leitura de fim − leitura de início`, nunca a
  soma das leituras individuais. Quando não há leitura anterior ao início do
  período, usa-se a primeira leitura dentro do próprio período (evita
  faturar o valor acumulado histórico total na primeira fatura de uma
  impressora nova). Consumo negativo (ex.: impressora substituída sem
  ajuste administrativo) é tratado como zero, sem interromper o fechamento
  das demais impressoras.
- **Reuso total da Fase 7, sem alterá-la:** `BillingClosingService` chama
  `IContractService.ResolveApplicableAsync` (mesma cascata Impressora →
  Local → Cliente inteiro) e lê `ContractFranchise` diretamente — nenhuma
  linha de `Modules.Contracts`/`Infrastructure.Contracts` mudou nesta fase.
- **`BillingCounterType` é o terceiro enum espelhado:** mesmos valores de
  `Monitoring.CounterType`/`Contracts.ContractCounterType`, definido
  separadamente para `Modules.Billing` não referenciar nenhum dos dois
  módulos.
- **Fatura imutável após sair de Rascunho:** a máquina de estados
  (Rascunho → Emitida/Cancelada; Emitida → Cancelada; Cancelada terminal) é
  a única forma de alterar uma Fatura depois de gerada — não há endpoint de
  edição de itens.

## Relatórios e Dashboards (Fase 9)

- **Primeira fase sem nenhuma entidade persistida nova.** `Modules.Reporting`
  só tem DTOs de agregação e interfaces — nenhuma migração, nenhum índice
  novo (verificado que os índices das Fases 2/3/6/8 já cobrem os
  agrupamentos desta fase).
- **Sem cache Redis.** Redis, na plataforma, continua só com health check
  (Fase 1) — decisão explícita de não introduzir o primeiro uso real de
  cache nesta fase; todo painel/relatório calcula ao vivo a cada
  requisição. Revisitar apenas se um problema real de performance for
  observado.
- **Consumo reimplementado, não compartilhado com a Fase 8.**
  `PrintConsumptionReportService` reimplementa a mesma técnica de cálculo
  de `BillingClosingService.CalculateConsumptionAsync` (leitura de fim
  menos leitura de início do intervalo) — pequena duplicação deliberada em
  vez de extrair um utilitário compartilhado prematuramente para dois usos.
- **Quinto e sexto enums espelhados:** `ReportingCounterType` (espelha
  `Monitoring.CounterType`) e `ReportingInvoiceStatus` (espelha
  `Billing.InvoiceStatus`) — mesmo padrão de `ContractCounterType`/
  `BillingCounterType` das Fases 7/8, para `Modules.Reporting` não
  referenciar nenhum módulo de domínio.
- **Exportação CSV vive na camada `Api`, não no domínio.** Os serviços de
  `Infrastructure.Reporting` só retornam DTOs estruturados; a formatação
  CSV (`ReportCsvWriter`) acontece nos controllers dos endpoints
  `.../export`, que chamam o mesmo serviço de domínio do endpoint de
  consulta correspondente — nenhuma regra de negócio duplicada.
- **Nenhuma operação desta fase é auditada** — consultas de leitura, mesmo
  padrão de qualquer listagem `GET` já existente na plataforma.

## Portal do Cliente (Fase 10)

- **Segundo nível de isolamento, dentro do tenant.** Todas as fases
  anteriores isolam por `TenantId`; esta fase introduz `ICustomerContext`/
  `CustomerContext` (`Modules.Tenancy`, mesmo padrão scoped de
  `ITenantContext`/`TenantContext`), resolvido pelo mesmo
  `TenantResolutionMiddleware` a partir de uma nova claim `customer_id` no
  JWT — presente apenas para usuários do papel `Cliente`. Um Usuário-Cliente
  só enxerga os dados do `Customer` ao qual está vinculado, nunca os de
  outro Cliente do mesmo tenant.
- **Vínculo usuário↔Cliente é uma coluna, não uma tabela de associação.**
  `ApplicationUser.CustomerId` (nulo, exceto para usuários `Cliente`) —
  um usuário vincula-se a no máximo um `Customer`; um `Customer` pode ter
  vários usuários (ex.: recepção e financeiro do próprio Cliente).
  Validado em `UserService.CreateAsync`/`UpdateAsync`: `Cliente` exige
  `CustomerId` válido no tenant e não pode ser combinado com nenhum outro
  papel.
- **Terceira fase sem nenhuma entidade persistida nova** (depois da Fase
  9). `Modules.Portal` só tem DTOs de leitura e interfaces — a única
  migração desta fase é a coluna `ApplicationUser.CustomerId`.
- **Sexto, sétimo e oitavo enums espelhados:** `PortalPrinterStatus`,
  `PortalTicketStatus` e `PortalInvoiceStatus` — mesmo padrão de
  `ReportingCounterType`/`ReportingInvoiceStatus` da Fase 9, para
  `Modules.Portal` não referenciar nenhum módulo de domínio.
- **Somente leitura.** Nenhum verbo de mutação no portal nesta fase —
  abertura de chamado, pagamento de fatura e autoatendimento de conta
  ficam para fases futuras (ver `requirements.md`).
- **Faturas em Rascunho nunca são expostas ao portal** — apenas
  `Emitida`/`Cancelada`, mesma regra em toda consulta e no detalhe.
- **404 uniforme para recurso de outro Cliente/tenant** — mesmo padrão de
  não-enumeração já usado em `UserService.FindInTenantAsync`; um recurso
  de outro Cliente do mesmo tenant é indistinguível de um inexistente.
- **Reaproveita `POST`/`PUT /api/v1/users` para criar/vincular o usuário
  Cliente** — nenhum endpoint dedicado de "criar usuário do portal";
  apenas um campo `customerId` adicional no contrato existente.
- **Nenhuma operação de leitura desta fase é auditada** — mesmo padrão da
  Fase 9; apenas o vínculo usuário↔Cliente é auditado, como parte de
  `UserCreate`/`UserUpdate` (já existentes).

## Decisões e itens sinalizados (Fase 1)

- **RBAC em memória:** o mapeamento papel→permissões é estático em código; o cache
  distribuído no Redis é um ponto de extensão (fail-open documentado).
- **Rate limiting in-process:** limiter nativo do .NET; store distribuído no Redis
  é ponto de extensão para múltiplas instâncias.
- **MFA-ready:** o fluxo de segundo fator existe estruturalmente com um validador
  fail-closed; nenhum provedor TOTP real na Fase 1.
- **Provisionados, não exercitados:** SignalR, Hangfire e uso funcional do MinIO.
