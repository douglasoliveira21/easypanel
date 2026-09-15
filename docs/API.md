# API — EasyPanel (Fases 1–4)

Base: `/api/v1`. Documentação interativa via **Swagger** (esquema Bearer). Todas
as respostas de erro seguem **RFC 7807 (ProblemDetails)**, com `traceId` igual ao
`X-Correlation-Id` da requisição.

## Autenticação

Envie o token de acesso no header `Authorization: Bearer <jwt>`. O JWT dura ≤ 15
min; use o refresh token para renovar.

| Método | Rota | Auth | Descrição |
|--------|------|------|-----------|
| POST | `/api/v1/auth/login` | anônimo | Autentica; retorna par de tokens ou desafio MFA |
| POST | `/api/v1/auth/refresh` | refresh token | Rotaciona e emite novo par |
| POST | `/api/v1/auth/logout` | anônimo (token no corpo) | Revoga o refresh token da sessão |
| POST | `/api/v1/auth/mfa/verify` | ticket MFA | Conclui o segundo fator |
| POST | `/api/v1/auth/forgot-password` | anônimo | Inicia recuperação (resposta não reveladora) |
| POST | `/api/v1/auth/reset-password` | token de reset | Redefine a senha |
| POST | `/api/v1/auth/change-password` | autenticado | Altera a própria senha |

## Usuários (permissão `user.manage`)

| Método | Rota |
|--------|------|
| GET | `/api/v1/users` |
| POST | `/api/v1/users` |
| PUT | `/api/v1/users/{id}` |
| POST | `/api/v1/users/{id}/deactivate` |
| POST | `/api/v1/users/{id}/reactivate` |

`POST`/`PUT` aceitam `customerId` (Fase 10 — Portal do Cliente): obrigatório
quando `roles` contém `Cliente` (deve existir no tenant; nenhum outro papel
pode ser combinado com `Cliente`), ignorado/forçado a nulo caso contrário.

## Clientes

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/customers` | `customer.view` |
| GET | `/api/v1/customers/{id}` | `customer.view` |
| POST | `/api/v1/customers` | `customer.create` |
| PUT | `/api/v1/customers/{id}` | `customer.edit` |
| PATCH | `/api/v1/customers/{id}/status` | `customer.edit` |

## Locais

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/customers/{customerId}/locations` | `location.view` |
| GET | `/api/v1/locations/{id}` | `location.view` |
| POST | `/api/v1/locations` | `location.create` |
| PUT | `/api/v1/locations/{id}` | `location.edit` |
| PATCH | `/api/v1/locations/{id}/status` | `location.edit` |

## Auditoria (permissão `audit.view`)

| Método | Rota |
|--------|------|
| GET | `/api/v1/audit-logs` |

## Monitoramento — Fase 2

### Impressoras

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/printers` | `printer.view` |
| GET | `/api/v1/printers/{id}` | `printer.view` |
| POST | `/api/v1/printers` | `printer.create` |
| PUT | `/api/v1/printers/{id}` | `printer.edit` |
| POST | `/api/v1/printers/{id}/move` | `printer.move` |

Movimentação (`/move`) aceita `operation` (Install/Transfer/Collect/Disable/Reactivate)
e `toLocationId?`; cada operação registra uma entrada append-only no histórico.

### Contadores

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/printers/{printerId}/counters` | `counter.view` |
| POST | `/api/v1/printers/{printerId}/counters/adjust` | `counter.adjust` |

Histórico via **cursor pagination** (`?cursor=&pageSize=`, ordem decrescente por
tempo). Leituras normais são não-decrescentes (leitura inferior → 409); o ajuste
administrativo pode reduzir o valor mediante justificativa e é auditado.

### Agente Windows (autenticação própria — audience `easypanel:client`)

| Método | Rota | Auth | Descrição |
|--------|------|------|-----------|
| POST | `/api/v1/client/register` | chave de provisionamento | Registra o agente; emite credenciais próprias |
| POST | `/api/v1/client/token` | client_id + secret | Emite par de tokens do agente |
| POST | `/api/v1/client/token/refresh` | refresh do agente | Rotaciona o token do agente |
| POST | `/api/v1/client/heartbeat` | ClientBearer | Reporta estado/versão (R6) |
| POST | `/api/v1/client/collect` | ClientBearer | Envia coleta idempotente (`IdempotencyKey`) |
| POST | `/api/v1/client/upload` | ClientBearer | Alias idempotente de coleta |
| GET | `/api/v1/client/config` | ClientBearer | Configuração vigente do agente |
| GET | `/api/v1/client/update` | ClientBearer | Metadados da versão de atualização (404 se ausente) |

Endpoints de agente autenticam pelo esquema `ClientBearer` (audience dedicado): um
JWT de usuário não é aceito e vice-versa. Tenant/local são resolvidos sempre da
identidade do agente. Ingestão retorna 202 com `{ collectionId, duplicate }`; o
reenvio da mesma `IdempotencyKey` não duplica dados.

## Alertas e Notificações — Fase 3

### Regras de alerta

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/alert-rules` | `alert.view` |
| GET | `/api/v1/alert-rules/{id}` | `alert.view` |
| POST | `/api/v1/alert-rules` | `alert.manage` |
| PUT | `/api/v1/alert-rules/{id}` | `alert.manage` |

Corpo de criação/edição inclui `eventTypes` (valores inteiros de
`PrinterEventType`), `scopeType` (Tenant=0/Location=1/Printer=2/WindowsClient=3) +
o id de escopo correspondente, `severity`, `thresholdCount`/`thresholdWindowMinutes`
(ambos ou nenhum), `autoResolve`, e os canais (`emailEnabled`+`emailRecipients`,
`webhookEnabled`+`webhookUrl` HTTPS+`webhookSecret`). `webhookSecret` nunca é
retornado em nenhuma resposta.

### Alertas

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/alerts` | `alert.view` |
| GET | `/api/v1/alerts/{id}` | `alert.view` |
| POST | `/api/v1/alerts/{id}/acknowledge` | `alert.acknowledge` |
| POST | `/api/v1/alerts/{id}/resolve` | `alert.acknowledge` |
| GET | `/api/v1/alerts/{id}/notifications` | `alert.view` |

Listagem e histórico de notificação via **cursor pagination**
(`?cursor=&pageSize=`, ordem decrescente por ocorrência/tentativa). Filtros:
`state`, `severity`, `alertRuleId`, `printerId`, `windowsClientId`, `from`, `to`.
Reconhecer exige estado `Open`; resolver exige `Open` ou `Acknowledged` — fora
disso, 409. `GET /{id}` inclui o histórico de transições de estado.

### Silenciamentos

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/alert-silences` | `alert.view` |
| POST | `/api/v1/alert-silences` | `alert.manage` |
| POST | `/api/v1/alert-silences/{id}/end` | `alert.manage` |

Escopo (`alertRuleId?`/`printerId?`/`windowsClientId?`) exige ao menos um campo
preenchido; `endsAt` deve ser posterior a `startsAt`. Um Alerta gerado durante um
silenciamento vigente continua sendo registrado — apenas a notificação não é
despachada. Encerrar um silenciamento já encerrado → 409.

## Suprimentos — Fase 4

### Níveis e histórico (por impressora)

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/printers/{printerId}/supplies` | `supply.view` |
| GET | `/api/v1/printers/{printerId}/supplies/history` | `supply.view` |

Níveis atuais retornam a última leitura de cada rótulo detectado, com
`forecastDepletionAt` (previsão de troca por regressão linear simples; `null`
quando não calculável). Histórico via **cursor pagination**
(`?cursor=&pageSize=`), filtro opcional `?label=`.

### Limiares de suprimento

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/supply-thresholds` | `supply.view` |
| POST | `/api/v1/supply-thresholds` | `supply.manage` |
| DELETE | `/api/v1/supply-thresholds/{id}` | `supply.manage` |

`POST` é um **upsert** por `(printerId, label)` — `printerId`/`label` nulos
representam os padrões em cascata (por Tenant geral, ou por Tenant+rótulo).
`thresholdPercent` fora de 0–100 → 400.

### Ingestão de coleta (extensão — Fase 4)

`POST /api/v1/client/collect`/`upload` (Fase 2) aceita agora um campo opcional
`supplies: [{ label, percent }]` na mesma submissão, junto de `counters`.
`GET /api/v1/client/config` (Fase 2) retorna também `monitoredPrinters`
(impressoras já registradas e monitoráveis do Local do agente).

## Estoque — Fase 5

### Itens de estoque

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/inventory-items` | `estoque.view` |
| GET | `/api/v1/inventory-items/{id}` | `estoque.view` |
| POST | `/api/v1/inventory-items` | `estoque.manage` |
| PUT | `/api/v1/inventory-items/{id}` | `estoque.manage` |

Listagem paginada (`?page=&pageSize=`), com busca opcional por nome/SKU
(`?search=`) e filtro por `?isActive=`. `PUT` inclui ativação/desativação
(`isActive`); desativar preserva o histórico de movimentações.

### Movimentações

| Método | Rota | Permissão |
|--------|------|-----------|
| POST | `/api/v1/inventory-movements` | `estoque.manage` |
| GET | `/api/v1/inventory-items/{itemId}/movements` | `estoque.view` |
| GET | `/api/v1/printers/{printerId}/inventory-movements` | `estoque.view` |

`POST` registra uma movimentação (`type`: Entrada/Saída/Ajuste) e atualiza o
saldo na mesma operação. Saída sem saldo suficiente → 409. Ajuste exige
`reason` e `adjustmentDirection` (Increase/Decrease) → 400 sem eles; Ajuste com
`Decrease` nunca leva o saldo abaixo de zero (trava em zero). Item inativo ou
local/impressora de outro tenant → 400. Histórico via **cursor pagination**
(`?cursor=&pageSize=`), com filtro opcional `?locationId=` na consulta por
item.

### Saldo e estoque mínimo

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/locations/{locationId}/inventory` | `estoque.view` |
| POST | `/api/v1/inventory-minimums` | `estoque.manage` |
| GET | `/api/v1/inventory-minimums/below` | `estoque.view` |

Saldo por local retorna o saldo corrente de cada item já movimentado nesse
local. `POST` de mínimo é um **upsert** por `(itemId, locationId)`. A listagem
de itens abaixo do mínimo é paginada (`?page=&pageSize=`).

## Chamados/Helpdesk e SLA — Fase 6

### Chamados

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/tickets` | `chamado.view` |
| GET | `/api/v1/tickets/{id}` | `chamado.view` |
| POST | `/api/v1/tickets` | `chamado.manage` |
| POST | `/api/v1/tickets/{id}/status` | `chamado.manage` |
| POST | `/api/v1/tickets/{id}/assign` | `chamado.manage` |
| POST | `/api/v1/tickets/{id}/comments` | `chamado.manage` |
| GET | `/api/v1/tickets/{id}/interactions` | `chamado.view` |
| GET | `/api/v1/tickets/sla-breached` | `chamado.view` |

Listagem paginada (`?page=&pageSize=`), com filtros opcionais `?status=`,
`?priority=`, `?customerId=`, `?locationId=`, `?printerId=`,
`?assignedToUserId=`. `POST /tickets` calcula os prazos de SLA na abertura
(política do tenant para a prioridade informada, ou o padrão de plataforma).
`POST .../status` valida a transição contra a máquina de estados do chamado
(400 se inválida). `POST .../assign` aceita `assignedToUserId: null` para
desatribuir. Histórico de interações via **cursor pagination**
(`?cursor=&pageSize=`). `sla-breached` retorna os chamados com SLA violado
ou já vencido, paginados (`?page=&pageSize=`).

### Anexos

| Método | Rota | Permissão |
|--------|------|-----------|
| POST | `/api/v1/tickets/{id}/attachments` | `chamado.manage` |
| GET | `/api/v1/tickets/{id}/attachments` | `chamado.view` |
| GET | `/api/v1/tickets/{id}/attachments/{attachmentId}` | `chamado.view` |

`POST` é `multipart/form-data` (campo `file`). Tamanho acima do máximo
configurado ou tipo MIME fora da allowlist → 400, sem gravar no storage. O
`GET` de download retorna o conteúdo do arquivo com o `Content-Type`
original.

### Política de SLA

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/sla-policies` | `chamado.view` |
| POST | `/api/v1/sla-policies` | `sla.manage` |

`POST` é um **upsert** por prioridade. Prazos não-positivos, ou resolução
menor que a primeira resposta, → 400.

## Contratos — Fase 7

### Contratos

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/contracts` | `contrato.view` |
| GET | `/api/v1/contracts/{id}` | `contrato.view` |
| POST | `/api/v1/contracts` | `contrato.manage` |
| PUT | `/api/v1/contracts/{id}` | `contrato.manage` |
| POST | `/api/v1/contracts/{id}/status` | `contrato.manage` |
| GET | `/api/v1/printers/{printerId}/applicable-contract` | `contrato.view` |

Listagem paginada (`?page=&pageSize=`), com filtros opcionais
`?customerId=`, `?status=`. `POST /contracts` exige `Cliente` válido do
tenant e `EndDate >= StartDate` quando informado; status inicial sempre
"Rascunho". `PUT` edita apenas `Number`/`EndDate`/`Observations` —
`CustomerId`/`StartDate` são imutáveis. `POST .../status` valida a
transição contra a máquina de estados do contrato (400 se inválida).
`applicable-contract` resolve o contrato aplicável a uma impressora numa
data (`?referenceDate=`, default agora), na ordem Impressora → Local →
Cliente inteiro, considerando só contratos `Ativo` e vigentes — **204** sem
corpo quando nenhum contrato se aplica (não é erro).

### Escopo (Local/Impressora)

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/contracts/{id}/locations` | `contrato.view` |
| POST | `/api/v1/contracts/{id}/locations/{locationId}` | `contrato.manage` |
| DELETE | `/api/v1/contracts/{id}/locations/{locationId}` | `contrato.manage` |
| GET | `/api/v1/contracts/{id}/printers` | `contrato.view` |
| POST | `/api/v1/contracts/{id}/printers/{printerId}` | `contrato.manage` |
| DELETE | `/api/v1/contracts/{id}/printers/{printerId}` | `contrato.manage` |

Um contrato sem nenhum Local/Impressora vinculado cobre todo o Cliente.
`POST` valida que o Local/Impressora pertence ao mesmo Cliente do contrato
(400 caso contrário) e que não há sobreposição de vigência com outro
contrato não-encerrado do mesmo Cliente já vinculado ao mesmo Local/
Impressora (**409** caso contrário).

### Franquias

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/contracts/{id}/franchises` | `contrato.view` |
| POST | `/api/v1/contracts/{id}/franchises` | `contrato.manage` |

`POST` é um **upsert** por tipo de contador (`counterType`). Quantidade
incluída ou preço de excedente negativos → 400.

## Fechamento e Faturamento — Fase 8

### Fechamento mensal

| Método | Rota | Permissão |
|--------|------|-----------|
| POST | `/api/v1/billing-closings` | `fechamento.manage` |
| GET | `/api/v1/billing-closings` | `fechamento.view` |

`POST` executa o fechamento do período informado (`{year, month}`):
consolida o consumo de cada Impressora a partir do histórico de
`PrinterCounter` (leitura de fim menos leitura de início do período),
resolve o Contrato aplicável (mesma cascata da Fase 7) e gera uma Fatura
por Contrato com excedente. Período corrente/futuro → 400; período já
fechado anteriormente → 409 (não refaz). `GET` lista o histórico de
fechamentos executados, paginado (`?page=&pageSize=`).

### Faturas

| Método | Rota | Permissão |
|--------|------|-----------|
| GET | `/api/v1/invoices` | `fechamento.view` |
| GET | `/api/v1/invoices/{id}` | `fechamento.view` |
| POST | `/api/v1/invoices/{id}/status` | `fechamento.manage` |

Listagem paginada (`?page=&pageSize=`), com filtros opcionais
`?customerId=`, `?contractId=`, `?status=`, `?year=&month=` (mês só é
aplicado quando o ano também é informado). `GET /{id}` retorna a Fatura com
seus Itens de excedente. `POST .../status` transiciona
Rascunho→{Emitida,Cancelada}, Emitida→Cancelada; Cancelada é terminal (400
se violado). A Fatura e seus Itens são imutáveis fora do `Status` a partir
do momento em que saem de Rascunho.

## Relatórios e Dashboards — Fase 9

Todos os endpoints exigem `relatorio.view`. Nenhuma operação é auditada
(consultas de leitura).

### Painel de visão geral

| Método | Rota |
|--------|------|
| GET | `/api/v1/reports/dashboard` |

Retorna contagens de estado corrente: Impressoras por status, Alertas por
state (todos) e por severidade (só não-resolvidos), Chamados por status,
itens de Estoque abaixo do mínimo, Faturas por status e o valor total
pendente (Rascunho+Emitida). Todas as chaves de cada enum aparecem, mesmo
com contagem zero.

### Relatórios agregados por período

| Método | Rota |
|--------|------|
| GET | `/api/v1/reports/print-consumption?startDate=&endDate=&customerId=&locationId=` |
| GET | `/api/v1/reports/billing?startDate=&endDate=&status=` |
| GET | `/api/v1/reports/sla?startDate=&endDate=` |

`startDate`/`endDate` são obrigatórios (ISO 8601). `endDate < startDate` ou
intervalo maior que 366 dias → 400. Consumo é calculado como leitura de fim
menos leitura de início do intervalo (mesma técnica da Fase 8), agrupado
por Cliente/Local. Faturamento inclui Faturas cujo período se sobrepõe ao
intervalo, agrupadas por Cliente. SLA agrega os Chamados abertos no
intervalo por cumprimento de primeira resposta/resolução.

### Exportação CSV

| Método | Rota |
|--------|------|
| GET | `/api/v1/reports/print-consumption/export` |
| GET | `/api/v1/reports/billing/export` |
| GET | `/api/v1/reports/sla/export` |

Mesmos parâmetros dos endpoints de consulta correspondentes. Resposta
`text/csv; charset=utf-8`, com cabeçalho de coluna na primeira linha.

## Portal do Cliente — Fase 10

Área self-service restrita ao papel `Cliente`, cujas requisições carregam a
claim `customer_id` no token (segundo nível de isolamento, além do tenant).
Todo endpoint é somente-leitura; recurso de outro Cliente do mesmo tenant ou
de outro tenant → 404 uniforme. Nenhuma operação é auditada (consultas de
leitura).

### Parque e contadores (`portal.parque.view`)

| Método | Rota |
|--------|------|
| GET | `/api/v1/portal/printers?cursor=&pageSize=` |
| GET | `/api/v1/portal/printers/{id}/counters?cursor=&pageSize=` |

Lista as Impressoras do próprio Cliente e o histórico de contadores de uma
delas, por cursor pagination.

### Chamados (`portal.chamado.view`)

| Método | Rota |
|--------|------|
| GET | `/api/v1/portal/tickets?cursor=&pageSize=` |
| GET | `/api/v1/portal/tickets/{id}` |

Lista os Chamados do próprio Cliente e detalha um deles (com comentários e
anexos). Apenas interações do tipo comentário são expostas.

### Faturas (`portal.fatura.view`)

| Método | Rota |
|--------|------|
| GET | `/api/v1/portal/invoices?cursor=&pageSize=` |
| GET | `/api/v1/portal/invoices/{id}` |

Lista as Faturas emitidas/canceladas do próprio Cliente (Faturas em
Rascunho nunca são retornadas, inclusive por acesso direto ao id) e
detalha uma delas, com os itens de excedente.

## Health

| Método | Rota | Auth |
|--------|------|------|
| GET | `/health/live` | anônimo |
| GET | `/health/ready` | anônimo |

## Convenções transversais

- **Paginação:** `?page=&pageSize=` (PageSize limitado a 100). Resposta com
  `items`, `page`, `pageSize`, `totalCount`.
- **Códigos de erro:** 400 (validação), 401 (não autenticado/token inválido),
  403 (sem permissão), 404 (não encontrado / cross-tenant), 409 (conflito, ex.:
  CNPJ/email duplicado no tenant), 429 (rate limit; header `Retry-After`), 500.
- **Isolamento:** recursos de outro tenant retornam 404 (não revela existência).
- **Rate limiting:** por usuário/tenant/IP e por endpoint.
