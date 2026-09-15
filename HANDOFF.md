# HANDOFF — Continuação do EasyPanel (para outra IA/dev)

Este documento habilita **outra IA (ou desenvolvedor)** a continuar o trabalho sem
contexto prévio. Leia-o por completo antes de começar. O produto é o **EasyPanel**,
plataforma SaaS multi-tenant de outsourcing de impressão em **.NET 10 / ASP.NET Core**
(monólito modular). Idioma do projeto: **pt-BR** (código, comentários XML-doc, docs e
respostas ao usuário).

---

## 1. Estado atual (o que já está pronto)

| Fase | Escopo | Estado |
|------|--------|--------|
| **Fase 1 — Fundação** | Infra (Docker/Postgres/Redis/MinIO/Caddy), multi-tenancy, Identity/JWT/RBAC, auditoria, usuários, clientes, locais, observabilidade | **Concluída e verificada** |
| **Fase 2 — Monitoramento** | Auth do agente, registro/heartbeat, ingestão idempotente + pipeline, impressoras, contadores, config/update, Agente Windows (SNMP/fila/auto-update) | **Concluída e verificada** |
| **Fase 3 — Alertas e Notificações** | Regras de alerta por tenant, motor de avaliação (`AlertEngine`) sobre `PrinterEvent`, ciclo de vida do Alerta, notificação por e-mail/webhook com retry, histórico de notificação, silenciamento | **Concluída e verificada** |
| **Fase 4 — Suprimentos** | Histórico de nível de suprimento, limiar em cascata reusando o `AlertEngine` da Fase 3, previsão de troca, e orquestração real do Agente Windows (cliente HTTP, SNMP real, `Net_Monitoring_Service`/`Communication_Service`) | **Concluída e verificada** |
| **Fase 5 — Estoque** | Catálogo de itens, movimentação (Entrada/Saída/Ajuste) com saldo materializado por Local atualizado transacionalmente, estoque mínimo por (Item, Local) com listagem de itens abaixo do mínimo | **Concluída e verificada** |
| **Fase 6 — Chamados/Helpdesk e SLA** | Abertura de chamados, ciclo de vida com histórico de interações, política de SLA por (tenant, prioridade) com cálculo de prazos e indicador de violação, anexos (primeiro uso funcional do MinIO via `IFileStorage`/`MinioFileStorage`) | **Concluída e verificada** |
| **Fase 7 — Contratos** | Cadastro de contrato por Cliente com vigência/ciclo de vida, escopo opcional por Local/Impressora, franquia por tipo de contador (quantidade incluída + preço de excedente), resolução em cascata do contrato aplicável a uma impressora — base para a Fase 8 | **Concluída e verificada** |
| **Fase 8 — Fechamento e Faturamento** | Fechamento mensal (consolida consumo por impressora a partir de `PrinterCounter`, resolve contrato via `ResolveApplicableAsync`, calcula excedente contra `ContractFranchise`), geração de fatura por contrato, ciclo de vida da fatura (Rascunho/Emitida/Cancelada) | **Concluída e verificada** |
| **Fase 9 — Relatórios e Dashboards** | Painel de visão geral (contagens de estado corrente ao vivo), relatórios agregados por período (consumo de impressão, faturamento, SLA), exportação CSV — sem cache, sem nenhuma entidade persistida nova | **Concluída e verificada** |
| **Fase 10 — Portal do Cliente** | Segundo nível de isolamento por Cliente (`ICustomerContext`, claim `customer_id`), vínculo usuário↔Cliente (`ApplicationUser.CustomerId`), portal somente-leitura (parque/contadores, chamados, faturas emitidas/canceladas) restrito ao papel `Cliente` | **Concluída e verificada — roteiro atual completo** |

**Specs existentes** (em `.kiro/specs/`):
- `easypanel-print-outsourcing-platform/` — Fase 1 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase2-monitoring/` — Fase 2 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase3-alertas/` — Fase 3 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase4-suprimentos/` — Fase 4 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase5-estoque/` — Fase 5 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase6-chamados/` — Fase 6 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase7-contratos/` — Fase 7 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase8-fechamento/` — Fase 8 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase9-relatorios/` — Fase 9 (requirements/design/tasks, todos `[x]`)
- `easypanel-phase10-portal/` — Fase 10 (requirements/design/tasks, todos `[x]`)

**Cobertura de testes atual (baseline verde):**
- Backend: 151 unit + 65 security + 375 integration
- Agente Windows: 49 unit
- **Zero warnings**; migrações sem *pending model changes*.

**Decisões de implementação da Fase 10 (contexto útil):**
- **Segundo nível de isolamento, não uma tabela de associação**:
  `ApplicationUser.CustomerId` (coluna nula, única migração desta fase) —
  um usuário vincula-se a no máximo um `Customer`; um `Customer` pode ter
  vários usuários. `ICustomerContext`/`CustomerContext`
  (`Modules.Tenancy`) seguem exatamente o padrão scoped de
  `ITenantContext`/`TenantContext`, resolvidos pelo mesmo
  `TenantResolutionMiddleware` a partir de uma nova claim `customer_id`
  (presente só para usuários `Cliente`).
- **Exclusividade de papel**: `UserService.CreateAsync`/`UpdateAsync`
  exige `CustomerId` válido no tenant quando `Roles` contém `Cliente`, e
  recusa (400) combiná-lo com qualquer outro papel — evita ambiguidade de
  escopo (um usuário nunca é "meio administrativo, meio Cliente").
- **Novo módulo `Modules.Portal`, mesmo padrão da Fase 9**: sem nenhuma
  entidade persistida, só DTOs de leitura e interfaces
  (`IPortalFleetService`/`IPortalTicketService`/`IPortalInvoiceService`);
  toda leitura cruzada (`Printer`, `Location`, `PrinterCounter`, `Ticket`,
  `TicketInteraction`, `TicketAttachment`, `Invoice`, `InvoiceLineItem`)
  acontece em `Infrastructure.Portal`, com filtro duplo (tenant + Cliente).
- **Sexto/sétimo/oitavo enums espelhados**: `PortalPrinterStatus`,
  `PortalTicketStatus`, `PortalInvoiceStatus` — mesmo padrão das Fases
  7/8/9.
- **Somente leitura nesta fase**: nenhuma criação/interação pelo portal
  (abertura de chamado, pagamento de fatura, autoatendimento de conta
  ficam para fases futuras).
- **Faturas em Rascunho nunca aparecem no portal**, nem por acesso direto
  ao id — filtradas em toda consulta de `PortalInvoiceService`.
- **404 uniforme para recurso de outro Cliente/tenant**: mesmo padrão de
  não-enumeração de `UserService.FindInTenantAsync`, aplicado também ao
  segundo nível de isolamento.
- **Reaproveita `POST`/`PUT /api/v1/users`** para criar/vincular o usuário
  Cliente (campo `customerId` adicional) — sem endpoint dedicado nem fluxo
  de convite.
- **Sem auditoria de leitura**: mesmo padrão da Fase 9; só o vínculo
  usuário↔Cliente é auditado, como parte de `UserCreate`/`UserUpdate` já
  existentes.
- **Testado via HTTP real** (`PortalIsolationTests`,
  `EasyPanel.SecurityTests`): isolamento cruzado de Cliente e de tenant,
  negação RBAC de `portal.*.view` a papéis administrativos, negação de
  permissões de negócio a um Usuário-Cliente, caminho positivo.

**Decisões de implementação da Fase 9 (contexto útil):**
- Novo módulo `Modules.Reporting`, mas **sem nenhuma entidade persistida** —
  primeira fase que não gera migração. Só DTOs de agregação e interfaces;
  toda leitura cruzada (`Printer`, `PrinterCounter`, `Alert`, `Ticket`,
  `Invoice`, `IInventoryMovementService.ListBelowMinimumAsync`) acontece em
  `Infrastructure.Reporting`.
- **Sem cache Redis, decisão explícita**: seria o primeiro uso real de
  cache na plataforma (Redis hoje só tem health check desde a Fase 1); o
  roteiro dizia "considerar", não "implementar" — optei por não introduzir
  cache até haver um problema de performance real observado, mantendo o
  painel/relatórios sempre calculados ao vivo.
- **Consumo de impressão reimplementado, não compartilhado com a Fase 8**:
  `PrintConsumptionReportService.CalculateConsumptionAsync` duplica a mesma
  técnica de `BillingClosingService` (leitura de fim menos leitura de
  início do intervalo) — decisão deliberada de não extrair um utilitário
  compartilhado para apenas dois usos, e de não acoplar `Modules.Reporting`
  a `Modules.Billing`.
- **Quinto e sexto enums espelhados**: `ReportingCounterType` (espelha
  `Monitoring.CounterType`) e `ReportingInvoiceStatus` (espelha
  `Billing.InvoiceStatus`) — mesmo padrão já usado nas Fases 7/8.
- **CSV formatado na camada `Api`, não no domínio**: `ReportCsvWriter`
  (`EasyPanel.Api/Controllers/Reporting/`) recebe cabeçalhos + linhas já
  formatadas; os endpoints `.../export` chamam o mesmo serviço de domínio
  do endpoint de consulta correspondente, sem duplicar regra de negócio.
- **Sem auditoria**: nenhuma operação desta fase grava no `AuditLog` —
  decisão confirmada no `requirements.md`, mesmo padrão de qualquer
  listagem `GET` já existente na plataforma.
- **RBAC mais restritivo que Contratos**: só `Administrador`/`Financeiro`/
  `Supervisor` têm `relatorio.view`; `Operacional`/`Tecnico`/`Estoque` não
  têm acesso a relatórios (diferente de Contratos, onde Operacional/
  Supervisor têm `contrato.view`).

**Decisões de implementação da Fase 8 (contexto útil):**
- Novo módulo `Modules.Billing`: não referencia `Modules.Monitoring`/
  `Modules.Contracts` — Impressora e Contrato são validados apenas por
  `Guid` em `Infrastructure.Billing`
  (`BillingClosingService`/`InvoiceService`), que é quem chama
  `IContractService.ResolveApplicableAsync` (Fase 7) e lê `PrinterCounter`
  (Fase 2) diretamente.
- **Consumo do período = leitura de fim − leitura de início, nunca soma de
  deltas**: confirmado que `PrinterCounter.Value` é cumulativo/odômetro e
  não-decrescente (exceto ajuste administrativo). Quando não há leitura
  anterior ao início do período, usa-se a primeira leitura **dentro** do
  período — evita faturar o valor acumulado histórico total na primeira
  fatura de uma impressora nova. Consumo negativo (impressora substituída
  sem ajuste administrativo) é tratado como zero, sem interromper o
  fechamento das demais impressoras.
- **Lição da Fase 7 aplicada**: as colunas `*Ticks`
  (`PeriodStartTicks`/`PeriodEndTicks`/`ExecutedAtTicks`/`GeneratedAtTicks`)
  foram incluídas nas entidades **antes** de gerar a migração `AddBilling`
  — migração saiu limpa na primeira tentativa, sem precisar reverter o
  snapshot como aconteceu na Fase 7.
- **`BillingClosing` com constraint única `(TenantId, Year, Month)`**: o
  refechamento é impedido na própria constraint de banco, não só numa
  checagem em serviço — mais forte contra condição de corrida.
- **Fatura só é gerada com excedente > 0**: nenhum `InvoiceLineItem` de
  valor zero é persistido; um Contrato sem nenhum item com excedente no
  período simplesmente não gera Fatura.
- **`BillingCounterType` é o terceiro enum espelhado** (depois de
  `Monitoring.CounterType` e `Contracts.ContractCounterType`) com os mesmos
  valores inteiros — `Infrastructure.Billing` faz o cast entre os três.
- **Descoberta de teste**: FK constraints do SQLite **são** aplicadas nos
  testes deste projeto (diferente do que se poderia supor de SQLite "solto
  por padrão") — seeds de teste que inserem `Invoice`/`InvoiceLineItem`
  diretamente via EF precisam de `Contract`/`Customer`/`Location`/`Printer`
  reais na mesma transação, senão `SQLite Error 19: FOREIGN KEY constraint
  failed`.
- **Escopo consciente e conservador**: nenhuma emissão fiscal, PDF,
  cobrança/pagamento, fechamento automático agendado, ou reabertura de
  período já fechado — tudo fora de escopo (ver `requirements.md`/
  `design.md` da Fase 8).

**Decisões de implementação da Fase 7 (contexto útil):**
- Novo módulo `Modules.Contracts` (mesmo padrão de isolamento por `Guid` das
  Fases 3/5/6): não referencia `Modules.Customers`/`Modules.Monitoring` —
  Cliente, Local e Impressora são validados apenas por `Guid` em
  `Infrastructure.Contracts`
  (`ContractService`/`ContractScopeService`/`ContractFranchiseService`).
- **SLA por Contrato não veio nesta fase**: o gancho futuro sinalizado desde
  a Fase 6 (vincular `SlaPolicy` a um `Contract`) permanece fora de escopo —
  a Fase 7 entregou apenas a base de Contratos, sem tocar em
  `Modules.Ticketing`.
- **Resolução em cascata (R4)**: `ContractService.ResolveApplicableAsync`
  prioriza Impressora → Local → Cliente inteiro, considerando só Contratos
  `Ativo` e vigentes na data de referência. Determinística porque
  `ContractScopeService` valida ausência de sobreposição de vigência no
  mesmo Local/Impressora entre contratos não-encerrados do mesmo Cliente
  (409 ao tentar) — sem essa validação, a resolução poderia encontrar mais
  de um contrato no mesmo nível de especificidade.
- **Primeiro campo monetário da plataforma**:
  `ContractFranchise.ExcessUnitPrice` (`decimal(18,4)`) — sem precedente
  anterior no código. Moeda fixa `"BRL"` (campo `Currency`, já modelado para
  multi-moeda futura, mas sem uso real ainda).
- **`ContractCounterType` é um enum espelhado, não uma referência**: mesmos
  valores inteiros de `Modules.Monitoring.CounterType`, mas definido
  separadamente em `Modules.Contracts` para não criar uma referência de
  módulo cruzada. Quem sabe que os dois correspondem é
  `Infrastructure.Contracts` (e, na Fase 8, onde quer que a consolidação de
  contador cruze os dois módulos).
- **Datas de vigência com colunas `*Ticks`**: `StartDateTicks`/
  `EndDateTicks` (long) acompanham `StartDate`/`EndDate` — necessário porque
  a resolução de R4 compara um intervalo de vigência contra uma data de
  referência, e SQLite (usado nos testes) não traduz comparação sobre
  `DateTimeOffset`. **Armadilha que se repetiu nesta fase**: a migração
  `AddContracts` foi gerada inicialmente sem essas colunas ticks (antes de eu
  perceber a necessidade durante a escrita do serviço); tive que reverter a
  migração e o `AppDbContextModelSnapshot.cs` manualmente (editando o
  snapshot para remover os blocos de `Contract`/`ContractFranchise`/
  `ContractLocation`/`ContractPrinter` antes de regenerar), já que
  `dotnet ef migrations remove` exige conectividade com o Postgres real
  (indisponível neste ambiente) até para descartar uma migração nunca
  aplicada. Vale a pena verificar a necessidade de colunas `*Ticks` **antes**
  de gerar a migração inicial de uma fase, não depois.
- **Escopo consciente e conservador**: a Fase 7 é explicitamente "base para
  a Fase 8" — nenhum cálculo de consolidação de contador, cálculo de
  excedente real ou geração de fatura acontece aqui; nenhum preço em
  camadas/volume (franquia de preço único de excedente); nenhum
  encerramento automático por vigência expirada (indicador `isExpired`
  calculado na leitura, sem transição automática de `Status`).

**Decisões de implementação da Fase 6 (contexto útil):**
- Novo módulo `Modules.Ticketing` (mesmo padrão de isolamento por `Guid` das
  Fases 3/5): não referencia `Modules.Customers`/`Modules.Monitoring`/
  `Modules.Identity` — Cliente, Local, Impressora e usuários são validados
  apenas por `Guid` em `Infrastructure.Ticketing`
  (`TicketService`/`SlaPolicyService`/`TicketAttachmentService`).
  `ApplicationUser` não é `TenantEntity`, então a validação do responsável
  atribuído usa comparação explícita de `TenantId` (mesmo padrão do
  `UserService` da Fase 1).
- **SLA sem Contrato**: como `Modules.Contracts` (Fase 7) ainda não existe, a
  política de SLA é por (tenant, prioridade) — não por contrato, ao contrário
  do que o roteiro original sugeria. Prazos calculados **na abertura** do
  chamado; mudar a política depois não recalcula chamados já abertos.
- **Máquina de estados do chamado**: transições validadas contra uma tabela
  fixa (`TicketService.AllowedTransitions`); qualquer transição fora dela →
  400, sem efeito parcial.
- **Primeiro uso funcional do MinIO**: até a Fase 6, o storage só tinha um
  health check (`StorageHealthCheck`), nenhum código gravava/lia objetos.
  Esta fase entrega `IFileStorage` (abstração em `Shared.Kernel`, sem SDK) e
  `MinioFileStorage` (SDK oficial `Minio`, versão pinada e verificada sem
  vulnerabilidade conhecida) — upload/download via stream pelo backend, sem
  presigned URL. `StorageOptions` ganhou `AccessKey`/`SecretKey`/`Bucket`
  (já provisionados no `docker-compose.yml`, antes não vinculados).
- **Lacuna de cobertura conhecida e assumida**: este ambiente de
  desenvolvimento/CI não tem um MinIO acessível (sem Docker disponível). A
  cobertura funcional de `TicketAttachmentService` usa um `IFileStorage` fake
  em memória (mesmo em `TicketingIsolationTests`, via override de DI na
  `MultiTenantIsolationWebApplicationFactory`) — o `MinioFileStorage` real
  não foi exercitado contra um MinIO de verdade nesta sessão. Recomenda-se
  uma verificação manual (ou um teste de integração explicitamente marcado
  como "requer MinIO") num ambiente com `docker compose up minio` antes de ir
  para produção.
- **Detalhe de rota**: `GET /api/v1/tickets/sla-breached` convive com
  `GET /api/v1/tickets/{id:guid}` porque o segundo tem uma restrição de rota
  `:guid` — "sla-breached" nunca casa com esse padrão, então não há
  ambiguidade de roteamento.

**Decisões de implementação da Fase 5 (contexto útil):**
- Novo módulo `Modules.Inventory` (não uma extensão de módulo existente, ao
  contrário da Fase 4): não referencia `Modules.Customers` nem
  `Modules.Monitoring` — Local e Impressora são validados apenas por `Guid` em
  `Infrastructure.Inventory` (`InventoryItemService`/`InventoryMovementService`),
  seguindo o mesmo padrão de isolamento que `Modules.Alerting` já usa desde a
  Fase 3.
- `InventoryMovement.Quantity` é **sempre positivo** — o sinal do efeito no
  saldo vem do `Type` (Entrada soma, Saída subtrai) e, para Ajuste, de
  `AdjustmentDirection` (Increase soma, Decrease subtrai). Ajuste exige
  `Reason` e `AdjustmentDirection`; Ajuste-Decrease nunca leva o saldo abaixo
  de zero (`Math.Max(0, saldo - quantidade)`), ao contrário de Saída, que
  retorna 409 quando o saldo é insuficiente.
- `InventoryBalance` é materializado e atualizado **na mesma transação** que
  o `InventoryMovement` (um único `SaveChangesAsync`), evitando o custo de
  recalcular o saldo por soma do histórico a cada consulta.
- Escopo explicitamente fora da Fase 5 (justificado em `requirements.md`/
  `design.md`): alertas automáticos de estoque baixo (reuso do `AlertEngine`),
  pedido de compra/reposição automático, e baixa automática de estoque a
  partir de leitura SNMP de suprimento — `InventoryItem` não tem vínculo
  direto com `Printer`/`WindowsClient` como `PrinterEvent` tem.
- `ListBelowMinimumAsync` busca todos os mínimos e saldos do tenant e computa
  a lista de itens abaixo do mínimo **em memória**, paginando depois — decisão
  deliberada para evitar a complexidade de portabilidade de um LEFT JOIN entre
  SQLite (testes) e PostgreSQL (produção).
- `GET /api/v1/locations/{locationId}/inventory` e
  `GetBalanceByLocationAsync` retornam 404 quando o `locationId` pertence a
  outro tenant (o próprio local não é encontrado pelo filtro global), não uma
  lista vazia — mesmo comportamento vale para movimentação contra item de
  outro tenant (o item não é encontrado antes mesmo de validar local/
  impressora).

**Decisões de implementação da Fase 4 (contexto útil):**
- A Fase 4 começou como "suprimentos" no backend, mas revelou três lacunas
  pré-existentes da Fase 2 que precisaram ser fechadas para o recurso funcionar de
  ponta a ponta (todas confirmadas com o usuário antes de implementar): (1) o
  Agente Windows não tinha nenhum ciclo de coleta rodando (`Guardian` supervisionava
  uma lista vazia); (2) não havia implementação real de `IBackendClient` (só a
  interface); (3) nenhum driver populava `DeviceReading.SupplyLevels`, e não havia
  implementação real de `ISnmpCollector` (só a interface, com um fake nos testes).
  As três foram implementadas nesta fase — ver `design.md` e `requirements.md` da
  Fase 4 para o raciocínio completo.
- **Suprimento reusa o `AlertEngine` da Fase 3 sem alterá-lo**: o
  `CollectionProcessor` grava `PrinterEventType.SupplyLow` (novo valor do enum já
  existente) na transição de cruzamento do limiar; o motor de alertas já consome
  qualquer `PrinterEventType` por tenant/escopo, então nenhuma linha de
  `Modules.Alerting`/`Infrastructure.Alerting` mudou.
- **Limiar em cascata**: `SupplyThreshold(TenantId, PrinterId?, Label?)` — mais
  específico vence: (Impressora,Rótulo) → (Tenant,Rótulo) → (Tenant geral) →
  padrão de plataforma (`MonitoringOptions.DefaultSupplyThresholdPercent`).
- **SNMP real**: pacote `Lextm.SharpSnmpLib` (fixado, sem vulnerabilidade
  conhecida) para GET v1/v2c por UDP; cada OID é consultado individualmente (não
  em lote) para que um OID especulativo ausente (ex.: unidade de suprimento
  inexistente) não derrube a leitura inteira — importante porque SNMPv1 falha a
  PDU inteira quando qualquer OID não existe.
- **Leitura de suprimento genérica**: `ManufacturerDriverBase.Interpret` lê a
  Printer-MIB padrão (`prtMarkerSuppliesLevel`/`MaxCapacity`, índices fixos 1–6,
  sem exigir SNMP walk) — todos os 9 drivers ganham suporte automaticamente, sem
  código específico por fabricante.
- **Cliente HTTP do agente (`HttpBackendClient`)**: token de acesso/renovação
  geridos em memória com `SemaphoreSlim` (compartilhado como singleton entre
  `Net_Monitoring_Service` e `Communication_Service` via `IHttpClientFactory` +
  registro manual, não o típico `AddHttpClient<TClient,TImplementation>` — que
  criaria uma instância nova, sem cache de token, a cada resolução). `POST
  /client/register` e `POST /client/heartbeat` **não** são chamados por ele (fora
  do escopo aprovado; `ClientId`/`ClientSecret` são assumidos já provisionados).
- **Envelope de ingestão**: `Collection.CollectedData` passou de um array solto de
  contadores para `{ Counters, Supplies }`; a desserialização detecta o formato
  legado (array puro) e trata como `Supplies = []`, então dados gravados antes da
  Fase 4 continuam sendo processados sem erro.
- `Net_Monitoring_Service`/`Communication_Service` começam "não saudáveis" (nenhum
  laço interno rodando); é a primeira varredura do `Guardian` que os inicia via
  `RestartAsync` — o mesmo método usado para reiniciá-los depois.

**Decisões de implementação da Fase 3 (contexto útil):**
- `EasyPanel.Modules.Alerting` não referencia `EasyPanel.Modules.Monitoring`;
  impressoras/agentes são referenciados apenas por `Guid`. O único componente que
  cruza os dois módulos é o `AlertEngine` (em `EasyPanel.Infrastructure.Alerting`).
- Resolução automática de Alerta verifica o **estado corrente** de
  `Printer.Status`/`WindowsClient.State` a cada ciclo, em vez de depender de um
  evento específico de recuperação — a Fase 2 não emite `PrinterEvent` quando um
  agente volta de heartbeat ausente (só restaura o estado). Ver `design.md` da
  Fase 3, seção "Resolução automática — desvio de desenho".
- Notificação é despachada apenas na **abertura** de um novo Alerta, não a cada
  ocorrência subsequente agregada — evita spam. `RenotifyIntervalMinutes` fica
  como gancho para fase futura.
- Canal de e-mail usa `System.Net.Mail.SmtpClient` (biblioteca padrão do .NET,
  sem pacote novo) quando `Alerting:Smtp:Host` está configurado; log-only
  (mesmo padrão do `IPasswordResetNotifier` da Fase 1) quando não está.
- `PrinterEvent` ganhou a coluna `CreatedAtTicks` (portável) nesta fase, para o
  cursor global incremental do `AlertEngine` — mesma razão de
  `PrinterCounter.TimestampTicks` na Fase 2 (armadilha SQLite de ordenação/
  comparação sobre `DateTimeOffset`).
- Nenhum teste HTTP via `WebApplicationFactory` foi criado para
  `AlertRulesController`/`AlertsController`/`AlertSilencesController`, seguindo o
  precedente já estabelecido pela Fase 2 (`PrintersController`/
  `CountersController` também não têm) — a cobertura vem dos testes de
  integração em nível de serviço (`AlertRuleServiceTests`,
  `AlertSilenceServiceTests`, `AlertServiceTests`, `AlertEngineTests`,
  `AlertNotificationDispatcherTests`) e da suíte de segurança
  (`AlertingIsolationTests`, via HTTP real).

---

## 2. Como buildar e testar (comandos)

Sistema: **Windows / cmd ou PowerShell**. Separador de comandos no PowerShell é `;`
(não `&&`).

```powershell
# Backend (solução principal)
dotnet build EasyPanel.sln
dotnet test EasyPanel.sln

# Agente Windows (solução-satélite, independente)
dotnet build src\windows-client\EasyPanel.WindowsClient.slnx
dotnet test  src\windows-client\EasyPanel.WindowsClient.slnx

# Migrações EF (a partir da raiz)
dotnet ef migrations add <Nome> --project src\EasyPanel.Infrastructure --startup-project src\EasyPanel.Infrastructure --output-dir Persistence\Migrations
dotnet ef migrations has-pending-model-changes --project src\EasyPanel.Infrastructure --startup-project src\EasyPanel.Infrastructure
```

> **Nota SQLite/ORDER BY:** o provider SQLite dos testes **não** traduz `ORDER BY` nem
> comparação sobre `DateTimeOffset`. Ordene/compare por colunas portáveis (string, long
> como *ticks*) ou faça a comparação em memória. Ver `HeartbeatMonitor` e
> `PrinterCounter.TimestampTicks` como exemplos.

---

## 3. Regras inegociáveis (o usuário exige)

1. **Uma fase por vez.** NÃO gerar tarefas de fases sem requisitos/design próprios.
   Cada fase segue o fluxo **requisitos → design → tarefas → implementação**.
2. **Zero warnings.** `TreatWarningsAsErrors=true`. Build deve ficar limpo.
3. **Vulnerabilidades de dependência:** SEMPRE corrigir via *pin* de versão corrigida —
   NUNCA suprimir com `WarningsNotAsErrors`. Verificar com
   `dotnet list <proj> package --vulnerable --include-transitive`.
4. **Verificação independente** após cada tarefa: build (zero warnings) + testes verdes
   antes de marcar `[x]`. Confirmar migrações sem *pending changes* quando houver migração.
5. **pt-BR** em código, comentários XML-doc, documentação e respostas.
6. **Sem dados fake:** seeds limitam-se a papéis/permissões e credenciais necessárias.
7. **Execução autônoma:** ao implementar, não pausar entre tarefas pedindo confirmação;
   marcar os checkboxes no `tasks.md` diretamente (`- [ ]` → `- [x]`).

---

## 4. Arquitetura e convenções a reusar

### Estrutura de solução
```
EasyPanel.sln
├── src/
│   ├── EasyPanel.Api/                 # Host Web API, controllers, middlewares, DI (Program.cs)
│   ├── EasyPanel.Modules.Identity/    # Auth, usuários, RBAC (Permissions, RolePermissions, Roles)
│   ├── EasyPanel.Modules.Tenancy/     # Tenant, ITenantContext, TenantResolutionMiddleware
│   ├── EasyPanel.Modules.Customers/   # Customer + Location
│   ├── EasyPanel.Modules.Auditing/    # AuditLog, IAuditLogger, AuditEntry
│   ├── EasyPanel.Modules.Monitoring/  # Fase 2: agente, impressoras, contadores, coletas
│   ├── EasyPanel.Shared.Kernel/       # BaseEntity/TenantEntity, Result/Error, PagedResult/PageRequest
│   ├── EasyPanel.Infrastructure/      # AppDbContext, EF config, migrations, serviços, workers
│   └── windows-client/                # Fase 2: Agente Windows (EasyPanel.WindowsClient.slnx)
├── tests/ (UnitTests, IntegrationTests, SecurityTests)
└── docs/  (ARCHITECTURE, DATABASE, API, SECURITY, DEPLOYMENT, CLIENT)
```

### Regra de dependência
`Api → Modules.* → Shared.Kernel`; `Infrastructure` implementa as abstrações e é
referenciada pela Api apenas para DI. Nenhum módulo de negócio referencia outro por
implementação.

### Padrões obrigatórios (copiar dos existentes)
- **Entidades de negócio** herdam de `TenantEntity` (isolamento automático por
  `TenantId` via query filter global + interceptor de `SaveChanges`). Cross-tenant → 404.
- **Result pattern:** serviços retornam `Result`/`Result<T>` com `Error` (mapeado a HTTP
  por `ErrorType`: Validation→400, NotFound→404, Conflict→409, Forbidden→403).
- **Paginação:** `PagedResult`/`PageRequest` (PageSize ≤ 100). Para grandes volumes,
  cursor pagination (ver `CounterService`).
- **DTOs distintos das entidades** (nunca expor hash/stamps).
- **Controllers:** `[RequirePermission(Permissions.X)]` + `MapFailure(error)` switch por
  `ErrorType`. Ver `CustomersController`/`PrintersController`.
- **Auditoria:** eventos sensíveis via `IAuditLogger.LogAsync(AuditEntry)`, com redaction.
- **Novas permissões:** adicionar em `Permissions.cs` (catálogo `All`) e mapear em
  `RolePermissions.cs`. Há teste que valida que toda permissão mapeada existe no catálogo.
- **EF config:** uma classe `IEntityTypeConfiguration<>` por entidade em
  `Infrastructure/Persistence/Configurations/`, DbSet no `AppDbContext`, migração dedicada.
- **Testes de integração:** `AppDbContext` sobre **SQLite in-memory**; para semear entre
  tenants, use um `ITenantContext` mutável (SetSuperAdmin para semear, SetTenant para
  exercitar). Ver `PrinterServiceTests`, `CounterServiceTests`, `Phase2IsolationTests`.
- **Workers de plataforma** (cross-tenant): usar contexto de sistema Super Admin
  (`ISystemDbContextFactory`/`SystemTenantContext`). Ver `HeartbeatMonitor`,
  `CollectionProcessingWorker`.

### CNPJs válidos para testes
`11.222.333/0001-81`, `04.252.011/0001-10`, `34.028.316/0001-03`, `45.723.174/0001-10`.

---

## 5. Fluxo para iniciar uma nova fase (obrigatório)

Para **cada** fase nova (3 em diante):

1. **Criar a spec da fase** em `.kiro/specs/easypanel-phaseN-<nome>/`:
   - `requirements.md` — requisitos em formato **EARS** (WHEN/IF/WHERE/WHILE + SHALL),
     agrupados por user story, com critérios de aceitação testáveis.
   - `design.md` — arquitetura, componentes, modelo de dados, endpoints, correctness
     properties (para PBT), e itens explicitamente fora de escopo.
   - `tasks.md` — plano incremental (ver formato na seção 7).
   - `.config.kiro` — copiar o formato de uma fase existente (specType/workflowType).
2. **Validar requisitos** e obter aprovação do usuário antes do design; design antes das
   tarefas. (No fluxo Kiro, isso ocorre via o orquestrador de specs.)
3. **Implementar tarefa a tarefa**, sempre com build+testes verdes antes de marcar `[x]`.
4. **Atualizar a documentação** (`docs/*.md` + `README.md`) ao final da fase.

> As fases abaixo (seção 6) são um **roteiro de produto**, NÃO tarefas prontas para
> implementar. Extraia requisitos reais de cada fase com o usuário antes de codar.

---

## 6. Roteiro de fases restantes (10) — alto nível

Baseado no escopo do produto sinalizado na Fase 1/README. **Cada uma exige spec própria.**
**Todas as 10 fases do roteiro atual estão concluídas** — ver seção 9 para o que
vem depois (frontend, novo roteiro, ou extensões de fases existentes).

> **Fases 3, 4, 5, 6, 7, 8, 9 e 10 concluídas.** Fase 3 — Alertas e Notificações: regras por
> tenant (`AlertRule`), motor de avaliação (`AlertEngine`) consumindo
> `PrinterEvent`, ciclo de vida do Alerta, notificação por e-mail/webhook com
> retry, histórico e silenciamento. Fase 4 — Suprimentos: histórico de nível
> (`SupplyReading`), limiar em cascata que reusa o `AlertEngine` sem alterá-lo
> (`PrinterEventType.SupplyLow`), previsão de troca, e a orquestração real do
> Agente Windows (antes inexistente: cliente HTTP, SNMP real, ciclo de coleta).
> Fase 5 — Estoque: catálogo (`InventoryItem`), movimentação
> (`InventoryMovement`, Entrada/Saída/Ajuste), saldo materializado por Local
> (`InventoryBalance`) atualizado transacionalmente, estoque mínimo por
> (Item, Local) (`InventoryMinimum`) com listagem de itens abaixo do mínimo.
> Fase 6 — Chamados/Helpdesk e SLA: abertura de chamados (`Ticket`), ciclo de
> vida com histórico de interações (`TicketInteraction`), política de SLA por
> (tenant, prioridade) (`SlaPolicy`) com cálculo de prazos e indicador de
> violação, e anexos via MinIO (`TicketAttachment`/`IFileStorage`/
> `MinioFileStorage`) — primeiro uso funcional do storage da plataforma.
> Fase 7 — Contratos: cadastro por Cliente (`Contract`) com vigência/ciclo de
> vida, escopo opcional por Local/Impressora (`ContractLocation`/
> `ContractPrinter`), franquia por tipo de contador (`ContractFranchise`,
> quantidade incluída + preço único de excedente) e resolução em cascata do
> contrato aplicável a uma impressora (Impressora → Local → Cliente inteiro).
> Fase 8 — Fechamento e Faturamento: fechamento mensal (`BillingClosing`,
> constraint única por período) que consolida consumo por impressora a
> partir de `PrinterCounter`, resolve o contrato via
> `ResolveApplicableAsync` (Fase 7, sem alterá-la) e gera `Invoice`/
> `InvoiceLineItem` com excedente contra a `ContractFranchise` — sem
> emissão fiscal, PDF ou cobrança ainda.
> Fase 9 — Relatórios e Dashboards: painel de visão geral (contagens de
> estado corrente, sem persistência própria), três relatórios agregados por
> período (consumo de impressão, faturamento, SLA) e exportação CSV — tudo
> calculado ao vivo, sem cache Redis (decisão explícita, ver "Decisões de
> implementação da Fase 9").
> Fase 10 — Portal do Cliente: segundo nível de isolamento por Cliente
> (`ICustomerContext`/`CustomerContext`, claim `customer_id`, mesmo padrão
> scoped de `ITenantContext`), vínculo usuário↔Cliente
> (`ApplicationUser.CustomerId`, única migração da fase), e um portal
> somente-leitura (`Modules.Portal`, sem entidade persistida própria) com
> parque/contadores, chamados e faturas emitidas/canceladas (nunca
> rascunhos) restritos ao próprio Cliente — ver "Decisões de implementação
> da Fase 10".
> Ver `.kiro/specs/easypanel-phase3-alertas/`, `.kiro/specs/easypanel-phase4-suprimentos/`,
> `.kiro/specs/easypanel-phase5-estoque/`, `.kiro/specs/easypanel-phase6-chamados/`,
> `.kiro/specs/easypanel-phase7-contratos/`, `.kiro/specs/easypanel-phase8-fechamento/`,
> `.kiro/specs/easypanel-phase9-relatorios/`, `.kiro/specs/easypanel-phase10-portal/`
> e a seção 1 acima.

> **Roteiro atual completo.** As 10 fases acima cobrem o escopo sinalizado
> desde a Fase 1. Próximos passos possíveis (não iniciados, exigem
> alinhamento com o usuário): extensões das fases existentes (portal
> interativo — abertura de chamado/pagamento pelo Cliente; múltiplos
> Clientes por usuário; emissão fiscal/PDF de fatura; exportação em Excel/
> PDF; cache Redis se houver problema real de performance), ou um novo
> roteiro de fases. Frontend React (SPA) é sinalizado no produto mas ainda
> não implementado; alinhar com o usuário quando/como introduzir
> (provavelmente transversal, não uma fase de backend).

---

## 7. Formato do `tasks.md` (copiar exatamente)

Cada fase usa um plano incremental com "waves" (grupos paralelizáveis) e tarefas com
requisitos referenciados. Modelo mínimo:

```markdown
# Implementation Plan — EasyPanel (FASE N: <Nome>)

## Overview
<parágrafo curto: constrói sobre a Fase anterior; cada tarefa mantém o projeto
compilável e testável antes de avançar; ordem respeita dependências>

## Task Dependency Graph
​```json
{ "waves": [ { "wave": 1, "tasks": ["1.1"], "description": "..." } ] }
​```

## Tasks
- [ ] 1. <Grupo>
- [ ] 1.1 <Tarefa acionável>
  - <sub-passo de implementação>
  - <sub-passo de teste>
  - _Requirements: RX.Y, RX.Z_

## Notes
<decisões, itens fora de escopo, ganchos para fases futuras>
```

Regras do plano: cada tarefa é só de codificação, referencia requisitos específicos,
mantém o build compilável, inclui seus próprios testes, e nenhuma fase avança quebrada.

---

## 8. Decisões de implementação da Fase 2 (contexto útil)

- **Fila de coletas** implementada como worker de *polling* sobre PostgreSQL
  (`CollectionProcessingWorker` + `ICollectionProcessor`), não Hangfire, para manter um
  único deployable. A abstração `ICollectionProcessor` permite trocar por Hangfire/Redis
  sem alterar o pipeline. Idempotência: índice único `Collection(TenantId, IdempotencyKey)`
  + backstop `IngestionDedup`.
- **Auth do agente:** esquema `ClientBearer` (audience `easypanel:client`), token de
  cliente carrega `sub=clientId` → o rate limiting existente já particiona por agente e
  endpoint automaticamente.
- **Cursor de contadores:** `PrinterCounter.TimestampTicks` (long) para ordenação/cursor
  portável (SQLite não ordena por DateTimeOffset).
- **Agente Windows:** núcleo em `net10.0` (multi-plataforma, testável); só o host usa
  hosting de Windows Service. SNMP v3 previsto por extensibilidade (v1/v2c implementados).

---

## 9. Primeiros passos recomendados para quem assumir

1. Rodar os dois `dotnet test` (seção 2) e confirmar o baseline verde.
2. Ler `docs/ARCHITECTURE.md`, `docs/DATABASE.md`, `docs/API.md`, `docs/SECURITY.md`,
   `docs/CLIENT.md` e as specs das Fases 1 a 10.
3. **O roteiro atual (10 fases) está completo.** Decidir com o usuário o
   próximo passo: uma extensão de fase existente (ver "Roteiro atual
   completo" na seção 6), um novo roteiro de fases, ou o frontend — e
   criar a spec correspondente (requisitos → design → tarefas) antes de
   qualquer código.
4. Implementar tarefa a tarefa, mantendo build/testes verdes e marcando os checkboxes.
