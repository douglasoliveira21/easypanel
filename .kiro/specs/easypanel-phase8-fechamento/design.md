# Design Document — EasyPanel (FASE 8: Fechamento e Faturamento)

## Overview

A Fase 8 introduz um módulo de domínio novo, `EasyPanel.Modules.Billing`
(mesmo padrão arquitetural de `Modules.Contracts`/`Modules.Ticketing`):
entidades próprias, sem referenciar `Modules.Monitoring`/`Modules.Contracts`
— Impressora e Contrato são referenciados apenas por `Guid`. Toda a lógica
de consolidação cruza `Modules.Billing` com `Modules.Monitoring`
(`PrinterCounter`) e `Modules.Contracts` (`ContractService.
ResolveApplicableAsync`, `ContractFranchise`) em `EasyPanel.Infrastructure`,
mesmo padrão do `AlertEngine`/`TicketService`.

Escopo: fechamento mensal por tenant que consolida o consumo de cada
Impressora a partir do histórico append-only de `PrinterCounter`, resolve o
Contrato aplicável de cada Impressora, calcula o excedente contra a
`ContractFranchise`, e gera uma Fatura (rascunho) por Contrato com Itens de
Fatura. **Nenhuma emissão fiscal, PDF ou cobrança** — isso é fora de escopo
(ver `requirements.md`).

## Modelo de dados (`EasyPanel.Modules.Billing`)

### Enums

```csharp
public enum InvoiceStatus { Rascunho = 0, Emitida = 1, Cancelada = 2 }

// Espelha Modules.Monitoring.CounterType / Modules.Contracts.ContractCounterType
// (mesmos valores), mantido separado por não referenciar nenhum dos dois
// módulos — Infrastructure.Billing é quem sabe que os três correspondem à
// mesma semântica de contador.
public enum BillingCounterType { BlackAndWhite = 0, Color = 1, A3 = 2, A4 = 3, Scan = 4, Other = 99 }
```

### `BillingClosing : TenantEntity` (registro de execução, somente-adição)

| Campo | Tipo | Notas |
|---|---|---|
| Year | int | |
| Month | int | 1–12 |
| PeriodStart / PeriodStartTicks | DateTimeOffset / long | primeiro dia do mês, 00:00 UTC |
| PeriodEnd / PeriodEndTicks | DateTimeOffset / long | último dia do mês, 23:59:59 UTC |
| ExecutedAt / ExecutedAtTicks | DateTimeOffset / long | |
| ExecutedByUserId | Guid? | |
| InvoiceCount | int | quantidade de Faturas geradas nesta execução |

Índice único `(TenantId, Year, Month)` — impõe R1.3 (não refechar) na própria
constraint de banco, além da checagem em serviço.

### `Invoice : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| ContractId | Guid | |
| CustomerId | Guid | copiado do Contrato no momento da geração (histórico estável mesmo se o Contrato mudar depois) |
| PeriodStart / PeriodStartTicks | DateTimeOffset / long | |
| PeriodEnd / PeriodEndTicks | DateTimeOffset / long | |
| Status | InvoiceStatus | default `Rascunho` |
| TotalAmount | decimal(18,2) | soma dos Itens |
| Currency | string(3) | fixo `"BRL"` |
| GeneratedAt / GeneratedAtTicks | DateTimeOffset / long | |
| IssuedAt | DateTimeOffset? | preenchido na transição para `Emitida` |
| CancelledAt | DateTimeOffset? | preenchido na transição para `Cancelada` |

### `InvoiceLineItem : TenantEntity` (somente-adição, imutável após a Fatura sair de Rascunho)

| Campo | Tipo | Notas |
|---|---|---|
| InvoiceId | Guid | |
| PrinterId | Guid | |
| CounterType | BillingCounterType | |
| CounterTypeLabel | string? | copiado da Franquia quando `Other` |
| ConsumedQuantity | long | consumo do período (R2) |
| IncludedQuantity | long | copiado da Franquia no momento do fechamento |
| ExcessQuantity | long | `max(0, Consumido − Incluído)` |
| UnitPrice | decimal(18,4) | copiado da Franquia |
| LineAmount | decimal(18,2) | `ExcessQuantity × UnitPrice`, arredondado bancário (R3.5) |

Só é criado um item quando `ExcessQuantity > 0` (sem linhas de valor zero).

## Serviços de domínio

### `IBillingClosingService`

```csharp
Task<Result<BillingClosingDto>> ExecuteAsync(ExecuteBillingClosingRequest request, CancellationToken ct); // R1/R2/R3/R4
Task<Result<PagedResult<BillingClosingDto>>> ListAsync(PageRequest page, CancellationToken ct); // R6.3
```

**`ExecuteAsync(year, month)` — algoritmo:**
1. Deriva `PeriodStart`/`PeriodEnd` do (year, month) (R1.1). Se
   `now < PeriodEnd` → 400 (R1.2, período corrente/futuro).
2. Se já existe `BillingClosing` para `(TenantId, Year, Month)` → 409 (R1.3).
3. Para cada `Printer` do tenant:
   a. Para cada `BillingCounterType` com pelo menos uma `PrinterCounter` daquela
      Impressora com `TimestampTicks <= PeriodEndTicks`:
      - `endReading` = última leitura com `TimestampTicks <= PeriodEndTicks`.
      - `startReading` = última leitura com `TimestampTicks <= PeriodStartTicks`;
        se não houver, a primeira leitura com `TimestampTicks > PeriodStartTicks`
        dentro do período (R2.3).
      - Sem `startReading` (nenhuma leitura nem antes nem dentro do período) →
        ignora esse (Impressora, CounterType) (R2.5).
      - `consumption = max(0, endReading.Value − startReading.Value)` (R2.4).
   b. Resolve o Contrato via `IContractService.ResolveApplicableAsync(printerId,
      PeriodEnd)`. `null` → ignora a Impressora inteira (R3.2).
   c. Para cada (CounterType, consumption) calculado em (a), busca a
      `ContractFranchise` do Contrato para aquele CounterType. Ausente → ignora
      esse (Impressora, CounterType) (R3.4).
   d. `excess = max(0, consumption − franchise.IncludedQuantity)`. Se
      `excess > 0`, monta um `InvoiceLineItem` (R3.5, arredondamento
      `MidpointRounding.ToEven`, 2 casas).
4. Agrupa os itens montados por `ContractId`; para cada grupo com ≥1 item,
   cria uma `Invoice` (`Status = Rascunho`, `TotalAmount` = soma dos itens,
   `CustomerId` do Contrato) e persiste seus `InvoiceLineItem` (R4.1/R4.4).
   Contratos sem excedente no período não geram Fatura (R4.5).
5. Persiste o `BillingClosing` (com `InvoiceCount`) na mesma operação lógica
   (uma única transação de banco cobrindo passos 4–5, para não deixar
   faturas órfãs sem o registro de fechamento em caso de falha).
6. Audita `billingclosing.execute` (ator, tenant, período, quantidade de
   faturas geradas).

### `IInvoiceService`

```csharp
Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken ct); // com Itens (R6.2)
Task<Result<PagedResult<InvoiceDto>>> ListAsync(InvoiceQuery query, CancellationToken ct); // filtro Cliente/Contrato/período/status (R6.1)
Task<Result<InvoiceDto>> ChangeStatusAsync(Guid id, ChangeInvoiceStatusRequest request, CancellationToken ct); // R5
```

**Máquina de estados (`ChangeStatusAsync`):** `Rascunho → {Emitida,
Cancelada}`; `Emitida → {Cancelada}`; `Cancelada` é terminal (R5.4).
Transição para `Emitida` grava `IssuedAt`; para `Cancelada` grava
`CancelledAt`. Auditado `invoice.status_change` (ator, de/para). A Fatura e
seus Itens nunca são editados após sair de `Rascunho` (R5.2) — não há
método de atualização de itens, apenas leitura.

## Endpoints da API

Convenção idêntica às fases anteriores: `[RequirePermission]`, `MapFailure`,
`PagedResult`/paginação conforme volume.

- `POST /api/v1/billing-closings` (`fechamento.manage`) — corpo `{year,
  month}`; retorna o resumo do fechamento (R1–R4)
- `GET /api/v1/billing-closings` (`fechamento.view`, paginado) — histórico
  (R6.3)
- `GET /api/v1/invoices` (`fechamento.view`, paginado, filtros `?customerId=
  &contractId=&status=&year=&month=`)
- `GET /api/v1/invoices/{id}` (`fechamento.view`) — com Itens
- `POST /api/v1/invoices/{id}/status` (`fechamento.manage`)

## Permissões (RBAC)

`fechamento.view`, `fechamento.manage` — mapeamento confirmado no
`requirements.md`:
- `Financeiro`: ambas (dono natural do módulo).
- `Administrador`: ambas.
- `Operacional`/`Supervisor`/`Tecnico`/`Estoque`: nenhuma (diferente de
  Contratos — fechamento é estritamente financeiro).

## Auditoria

Via `IAuditLogger`: `billingclosing.execute` (ator, tenant, período,
quantidade de faturas), `invoice.status_change` (ator, de/para).

## Migração e índices

Uma migração `AddBilling`: `BillingClosings`, `Invoices`, `InvoiceLineItems`.

Índices: único `(TenantId, Year, Month)` em `BillingClosing`; `(TenantId,
CustomerId)`, `(TenantId, ContractId)`, `(TenantId, Status)`,
`(TenantId, PeriodStartTicks, PeriodEndTicks)` em `Invoice` (filtros de
R6.1); `(TenantId, InvoiceId)` em `InvoiceLineItem`.

## Correctness properties

1. **Isolamento multi-tenant**: nenhuma operação cria/lê/atualiza
   `BillingClosing`, `Invoice` ou `InvoiceLineItem` de um `TenantId`
   diferente do contexto autenticado.
2. **Sem refechamento silencioso**: nunca existe mais de um `BillingClosing`
   para o mesmo `(TenantId, Year, Month)` — a constraint única de banco
   garante isso mesmo sob concorrência.
3. **Consumo nunca negativo**: nenhum `InvoiceLineItem.ConsumedQuantity` é
   negativo (R2.4 piso em zero).
4. **Fatura sempre tem excedente**: toda `Invoice` gerada tem pelo menos um
   `InvoiceLineItem` com `ExcessQuantity > 0`; nenhum item de excedente zero
   é persistido.
5. **Imutabilidade pós-rascunho**: nenhum `InvoiceLineItem` é alterado após
   sua `Invoice` sair de `Rascunho`; a transição de status é a única escrita
   permitida em uma Fatura não-Rascunho.
6. **Transições de status restritas**: nenhuma `Invoice` alcança um
   `Status` por uma transição fora da máquina de estados definida.
7. **Rastreabilidade do Cliente**: `Invoice.CustomerId` reflete o Cliente do
   Contrato **no momento da geração**, estável mesmo que o Contrato seja
   posteriormente alterado (campo copiado, não uma FK ao Contrato para
   leitura do Cliente).

## Notas / fora de escopo

- Emissão fiscal, PDF, cobrança/pagamento, conciliação bancária: fora de
  escopo (ver `requirements.md`).
- Fechamento automático agendado: fora de escopo — sempre uma ação
  explícita via `POST /api/v1/billing-closings`.
- Reabertura de um período já fechado: fora de escopo; correção exige
  cancelar as Faturas manualmente (R5.3), sem gerar novas para o mesmo
  período.
- `EasyPanel.Modules.Billing` não referencia `Modules.Monitoring`/
  `Modules.Contracts`; toda leitura de `PrinterCounter`, chamada a
  `ResolveApplicableAsync` e leitura de `ContractFranchise` acontece em
  `EasyPanel.Infrastructure.Billing`.
