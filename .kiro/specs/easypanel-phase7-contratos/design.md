# Design Document — EasyPanel (FASE 7: Contratos)

## Overview

A Fase 7 introduz um módulo de domínio novo, `EasyPanel.Modules.Contracts`
(mesmo padrão arquitetural de `Modules.Ticketing`/`Modules.Inventory`):
entidades próprias, sem referenciar `Modules.Customers`/`Modules.Monitoring`
— Cliente, Local e Impressora são referenciados apenas por `Guid`. A
validação de que esses `Guid` pertencem ao tenant/Cliente corrente acontece
em `EasyPanel.Infrastructure` (mesmo padrão do `AlertEngine`/
`InventoryMovementService`/`TicketService`).

Escopo: cadastro de Contrato vinculado a um Cliente, com vigência e ciclo de
vida de status; escopo opcional a Locais/Impressoras específicos (vazio =
todo o Cliente); franquia por tipo de contador (quantidade incluída + preço
único de excedente); e a resolução do Contrato aplicável a uma Impressora
numa data (cascata de especificidade). **Nenhum cálculo de consolidação de
contador nem geração de fatura** — isso é a Fase 8 (ver `requirements.md`,
"Fora de escopo").

## Modelo de dados (`EasyPanel.Modules.Contracts`)

### Enums

```csharp
public enum ContractStatus { Rascunho = 0, Ativo = 1, Suspenso = 2, Encerrado = 3 }

// Espelha Modules.Monitoring.CounterType (mesmos valores), mantido separado
// para não criar referência de módulo cruzada — Infrastructure.Contracts é
// quem sabe que os dois enums correspondem à mesma semântica de contador.
public enum ContractCounterType { BlackAndWhite = 0, Color = 1, A3 = 2, A4 = 3, Scan = 4, Other = 99 }
```

### `Contract : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| Number | string (≤100) | número/nome do contrato, obrigatório |
| CustomerId | Guid | obrigatório (R1.3), imutável após criação |
| StartDate | DateTimeOffset | data de início da vigência (sem componente de hora relevante — normalizada para `00:00:00Z`) |
| EndDate | DateTimeOffset? | nulo = prazo indeterminado |
| Status | ContractStatus | default `Rascunho` |
| Observations | string? (≤1000) | |

### `ContractLocation : TenantEntity` (escopo)

| Campo | Tipo | Notas |
|---|---|---|
| ContractId | Guid | |
| LocationId | Guid | validado: mesmo Cliente do Contrato (R2.3) |

Índice único `(TenantId, ContractId, LocationId)`.

### `ContractPrinter : TenantEntity` (escopo)

| Campo | Tipo | Notas |
|---|---|---|
| ContractId | Guid | |
| PrinterId | Guid | validado: mesmo Cliente do Contrato (R2.3) |

Índice único `(TenantId, ContractId, PrinterId)`.

### `ContractFranchise : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| ContractId | Guid | |
| CounterType | ContractCounterType | |
| CounterTypeLabel | string? (≤100) | usado quando `CounterType = Other` |
| IncludedQuantity | long | ≥ 0 (R3.2) |
| ExcessUnitPrice | decimal(18,4) | ≥ 0 (R3.2), preço único de excedente (sem faixas) |
| Currency | string(3) | fixo `"BRL"` nesta fase (ISO 4217), campo já modelado para multi-moeda futura |

Índice único `(TenantId, ContractId, CounterType)` — no máximo uma Franquia
por tipo (R3.3), upsert.

## Serviços de domínio

### `IContractService`

```csharp
Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken ct);
Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken ct); // Number/EndDate/Observations
Task<Result<ContractDto>> GetAsync(Guid id, CancellationToken ct);
Task<Result<PagedResult<ContractDto>>> ListAsync(ContractQuery query, CancellationToken ct); // filtro por CustomerId/Status
Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken ct);

// R4: resolução do contrato aplicável a uma Impressora numa data.
Task<Result<ContractDto?>> ResolveApplicableAsync(Guid printerId, DateTimeOffset referenceDate, CancellationToken ct);
```

**`CreateAsync`:** valida `Number` não vazio, `CustomerId` existente no
tenant, `EndDate >= StartDate` quando informado — senão 400. Persiste com
`Status = Rascunho`, audita `contract.create`.

**Máquina de estados (`ChangeStatusAsync`):**

```
Rascunho ──► Ativo ──► Suspenso
   │           │  ▲        │
   │           │  └────────┘
   │           ▼
   └────────► Encerrado
```

Transições permitidas: `Rascunho → {Ativo, Encerrado}`; `Ativo → {Suspenso,
Encerrado}`; `Suspenso → {Ativo, Encerrado}`; `Encerrado` é terminal.
Qualquer outra combinação → 400 (R5.2). Auditado `contract.status_change`.

**`ResolveApplicableAsync` (R4):** busca a Impressora (cross-módulo, só em
Infrastructure) para obter `CustomerId`/`LocationId`, então busca, nesta
ordem, o primeiro Contrato `Ativo` do Cliente com vigência cobrindo
`referenceDate`:
1. Vinculado diretamente à Impressora (`ContractPrinter`);
2. Vinculado ao Local da Impressora (`ContractLocation`);
3. Sem nenhum escopo (`ContractLocation`/`ContractPrinter` vazios) — cobre
   todo o Cliente.

Nenhum resultado em nenhum nível → `Result.Success<ContractDto?>(null)`
(R4.2, não é erro). A unicidade em cada nível é garantida pela validação de
conflito de R2.4 (abaixo), então o primeiro resultado de cada nível já é
determinístico.

### `IContractScopeService`

```csharp
Task<Result> AddLocationAsync(Guid contractId, Guid locationId, CancellationToken ct);
Task<Result> RemoveLocationAsync(Guid contractId, Guid locationId, CancellationToken ct);
Task<Result<IReadOnlyList<Guid>>> ListLocationsAsync(Guid contractId, CancellationToken ct);

Task<Result> AddPrinterAsync(Guid contractId, Guid printerId, CancellationToken ct);
Task<Result> RemovePrinterAsync(Guid contractId, Guid printerId, CancellationToken ct);
Task<Result<IReadOnlyList<Guid>>> ListPrintersAsync(Guid contractId, CancellationToken ct);
```

**`AddLocationAsync`/`AddPrinterAsync`:** valida que o Contrato existe no
tenant; que o Local/Impressora existe e pertence ao **mesmo Cliente** do
Contrato (R2.3) — senão 400. Verifica conflito de vigência sobreposta
(R2.4): busca outros Contratos do mesmo Cliente, com `Status` em
`{Rascunho, Ativo, Suspenso}`, que já tenham esse mesmo Local/Impressora
vinculado e cuja vigência se sobreponha à do Contrato corrente — se houver,
409. (A mesma verificação vale para Local, por simetria: dois Contratos não
podem cobrir o mesmo Local com vigência sobreposta, pelo mesmo motivo que
quebraria o determinismo de R4.) Auditado `contract.scope_add`/
`contract.scope_remove`.

### `IContractFranchiseService`

```csharp
Task<Result<ContractFranchiseDto>> SetAsync(Guid contractId, SetContractFranchiseRequest request, CancellationToken ct); // upsert por CounterType
Task<Result<IReadOnlyList<ContractFranchiseDto>>> ListAsync(Guid contractId, CancellationToken ct);
```

Auditado `contractfranchise.set` (ator, contrato, tipo, valor anterior/novo).

## Endpoints da API

Convenção idêntica às fases anteriores: `[RequirePermission]`, `MapFailure`,
`PagedResult`/cursor conforme volume.

- `GET/POST /api/v1/contracts` (`contrato.view`/`contrato.manage`)
- `GET/PUT /api/v1/contracts/{id}` (`contrato.view`/`contrato.manage`)
- `POST /api/v1/contracts/{id}/status` (`contrato.manage`)
- `GET /api/v1/contracts/{id}/locations` (`contrato.view`)
- `POST /api/v1/contracts/{id}/locations/{locationId}` (`contrato.manage`)
- `DELETE /api/v1/contracts/{id}/locations/{locationId}` (`contrato.manage`)
- `GET /api/v1/contracts/{id}/printers` (`contrato.view`)
- `POST /api/v1/contracts/{id}/printers/{printerId}` (`contrato.manage`)
- `DELETE /api/v1/contracts/{id}/printers/{printerId}` (`contrato.manage`)
- `GET /api/v1/contracts/{id}/franchises` (`contrato.view`)
- `POST /api/v1/contracts/{id}/franchises` (`contrato.manage`) — upsert por CounterType
- `GET /api/v1/printers/{printerId}/applicable-contract?referenceDate=` (`contrato.view`) — R4, `204 No Content` quando não há contrato aplicável

## Permissões (RBAC)

`contrato.view`, `contrato.manage` — mapeamento confirmado no
`requirements.md`:
- `Financeiro`: ambas (dono natural do módulo).
- `Administrador`: ambas.
- `Operacional`/`Supervisor`: `contrato.view`.
- `Tecnico`/`Estoque`: nenhuma.

## Auditoria

Via `IAuditLogger`: `contract.create`, `contract.update`,
`contract.status_change` (ator, de/para), `contract.scope_add`/
`contract.scope_remove` (ator, tipo de escopo, id), `contractfranchise.set`
(ator, contrato, tipo, valor anterior/novo).

## Migração e índices

Uma migração `AddContracts`: `Contracts`, `ContractLocations`,
`ContractPrinters`, `ContractFranchises`.

Índices: `(TenantId, CustomerId, Status)` em `Contract` (listagem/filtro,
resolução R4); único `(TenantId, ContractId, LocationId)` em
`ContractLocation`; único `(TenantId, ContractId, PrinterId)` em
`ContractPrinter`; `(TenantId, LocationId)`/`(TenantId, PrinterId)` em
ambas as tabelas de escopo (consulta reversa para conflito de R2.4 e
resolução de R4); único `(TenantId, ContractId, CounterType)` em
`ContractFranchise`.

## Correctness properties

1. **Isolamento multi-tenant**: nenhuma operação cria/lê/atualiza Contrato,
   escopo ou franquia de um `TenantId` diferente do contexto autenticado.
2. **Escopo consistente com o Cliente**: nenhum `ContractLocation`/
   `ContractPrinter` referencia um Local/Impressora de um Cliente diferente
   do `Contract.CustomerId`.
3. **Sem sobreposição de escopo**: nenhum Local ou Impressora está vinculado,
   simultaneamente, a dois Contratos não-encerrados do mesmo Cliente com
   vigência sobreposta (R2.4).
4. **Franquia única por tipo**: nenhum Contrato tem mais de uma
   `ContractFranchise` para o mesmo `CounterType`.
5. **Transições de status restritas**: nenhum Contrato alcança um `Status`
   por uma transição fora da máquina de estados definida.
6. **Resolução determinística (R4)**: dado um conjunto de Contratos que
   satisfazem as propriedades 3–5, `ResolveApplicableAsync` para uma
   Impressora e data sempre retorna no máximo um resultado por nível de
   especificidade, e nunca um Contrato fora de vigência ou não-`Ativo`.

## Notas / fora de escopo

- Consolidação de contadores por período, cálculo de excedente real e
  geração de fatura: fora de escopo (Fase 8).
- Preço em camadas/volume: fora de escopo desta fase (franquia de preço
  único de excedente).
- Encerramento automático por vigência expirada: fora de escopo (indicador
  calculado, sem transição automática de `Status`).
- `EasyPanel.Modules.Contracts` não referencia `Modules.Customers`/
  `Modules.Monitoring`; toda validação cruzada de `CustomerId`/`LocationId`/
  `PrinterId` acontece em `EasyPanel.Infrastructure.Contracts`.
