# Design Document — EasyPanel (FASE 5: Estoque)

## Overview

A Fase 5 introduz um módulo de domínio novo, `EasyPanel.Modules.Inventory`
(mesmo padrão arquitetural de `EasyPanel.Modules.Alerting` na Fase 3): entidades
próprias, sem referenciar `Modules.Customers`/`Modules.Monitoring` — Local e
Impressora são referenciados apenas por `Guid`. A validação de que esses `Guid`
pertencem ao tenant corrente acontece em `EasyPanel.Infrastructure` (que já
referencia todos os módulos), no mesmo padrão usado pelo `AlertEngine` da Fase 3
e pelo `ClientConfigService`/`CollectionProcessor` da Fase 4.

Escopo: catálogo de itens, movimentação append-only (Entrada/Saída/Ajuste), saldo
materializado por (Item, Local) atualizado atomicamente a cada movimentação,
vínculo referencial (não FK) a suprimentos da Fase 4 e vínculo por `Guid` a
impressoras da Fase 2, e configuração de estoque mínimo com consulta de itens
abaixo dele — sem alertas automáticos (ver `requirements.md`, "Fora de escopo").

## Modelo de dados (`EasyPanel.Modules.Inventory`)

### Enums

```csharp
public enum InventoryMovementType { Entrada = 0, Saida = 1, Ajuste = 2 }

// Só relevante quando Type = Ajuste (R2.5): direção da correção de saldo.
public enum AdjustmentDirection { Increase = 0, Decrease = 1 }
```

### `InventoryItem : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| Name | string (≤200) | obrigatório |
| Sku | string? (≤100) | código/SKU opcional |
| Unit | string (≤20) | unidade de medida; default `"unidade"` |
| SupplyLabel | string? (≤100) | vínculo referencial (R4.1) ao `Label` de `SupplyReading`/`SupplyThreshold` da Fase 4 — sem validação de existência, é convenção de nome |
| IsActive | bool | default true |
| Observations | string? (≤1000) | |

### `InventoryMovement : TenantEntity` (somente-adição)

| Campo | Tipo | Notas |
|---|---|---|
| ItemId | Guid | |
| LocationId | Guid | |
| Type | InventoryMovementType | |
| AdjustmentDirection | AdjustmentDirection? | obrigatório sse `Type = Ajuste`; nulo caso contrário |
| Quantity | int | sempre positivo (R2.2) — a direção vem de `Type`/`AdjustmentDirection`, nunca do sinal |
| PrinterId | Guid? | opcional, só relevante para Saída (R4.2) |
| Reason | string? (≤500) | obrigatório sse `Type = Ajuste` (R2.5) |
| ActorUserId | Guid? | |
| OccurredAt | DateTimeOffset | |
| OccurredAtTicks | long | cursor portável (mesmo motivo de `PrinterCounter.TimestampTicks`) |

**Efeito no saldo:** `Entrada` → `+Quantity`; `Saida` → `-Quantity`; `Ajuste` com
`Increase` → `+Quantity`; `Ajuste` com `Decrease` → `-Quantity`, nunca levando o
saldo abaixo de zero (estoque físico não é negativo) — mas, diferente de uma
`Saida` normal, um `Ajuste` que reduz não passa pela verificação prévia de "saldo
suficiente para a operação solicitada" do R2.4: ele recalibra o saldo para a
contagem física real, então o piso em zero (não um "saldo mínimo necessário") é a
única trava.

### `InventoryBalance : TenantEntity` (materializado, não exposto para escrita direta pela API)

| Campo | Tipo | Notas |
|---|---|---|
| ItemId | Guid | |
| LocationId | Guid | |
| Quantity | int | saldo corrente; nunca negativo |

Índice único `(TenantId, ItemId, LocationId)`. Uma linha só existe depois da
primeira movimentação daquele (Item, Local) — antes disso, saldo implícito é zero
(R3.2 só lista o que já teve movimentação).

### `InventoryMinimum : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| ItemId | Guid | |
| LocationId | Guid | |
| MinimumQuantity | int | ≥ 0 |

Índice único `(TenantId, ItemId, LocationId)`. Ausência de linha = sem mínimo
configurado (R5.1) — nunca considerado baixo.

## Serviços de domínio

### `IInventoryItemService`

```csharp
Task<Result<InventoryItemDto>> CreateAsync(CreateInventoryItemRequest request, CancellationToken ct);
Task<Result<InventoryItemDto>> UpdateAsync(Guid id, UpdateInventoryItemRequest request, CancellationToken ct); // inclui IsActive
Task<Result<InventoryItemDto>> GetAsync(Guid id, CancellationToken ct);
Task<Result<PagedResult<InventoryItemDto>>> ListAsync(InventoryItemQuery query, CancellationToken ct); // busca por Name/Sku
```

### `IInventoryMovementService`

```csharp
Task<Result<InventoryMovementDto>> RegisterAsync(RegisterInventoryMovementRequest request, CancellationToken ct);

Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListHistoryAsync(
    Guid itemId, Guid? locationId, string? cursor, int pageSize, CancellationToken ct); // R3.3

Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListByPrinterAsync(
    Guid printerId, string? cursor, int pageSize, CancellationToken ct); // R4.3

Task<Result<IReadOnlyList<InventoryBalanceDto>>> GetBalanceByLocationAsync(Guid locationId, CancellationToken ct); // R3.2

Task<Result<InventoryMinimumDto>> SetMinimumAsync(SetInventoryMinimumRequest request, CancellationToken ct); // upsert, auditado (R5.3)

Task<Result<PagedResult<InventoryMinimumDto>>> ListBelowMinimumAsync(PageRequest page, CancellationToken ct); // R5.2
```

**`RegisterAsync` (transação única):**
1. Valida que `ItemId` existe, pertence ao tenant e está ativo (R2.7) — senão 400.
2. Valida que `LocationId` existe e pertence ao tenant — senão 400.
3. Se `PrinterId` informado, valida que pertence ao tenant — senão 400.
4. Se `Type = Ajuste`, exige `Reason` não vazio e `AdjustmentDirection` — senão 400.
5. Carrega (ou cria, saldo 0) o `InventoryBalance` do (Item, Local) com bloqueio
   otimista padrão do EF (linha rastreada na mesma transação).
6. Calcula o novo saldo pelo efeito descrito acima; se `Saida` e saldo
   insuficiente → 409 (R2.4); se `Ajuste`-`Decrease` levaria abaixo de zero,
   trava em zero (não é erro — corrige para o piso).
7. Insere o `InventoryMovement` e atualiza o `InventoryBalance` na mesma
   `SaveChanges` (R3.1/R3.4).
8. Audita via `IAuditLogger` (R6.3), com atenção a `Ajuste` (R2.5: ator, saldo
   anterior, novo saldo).

## Endpoints da API

Convenção idêntica às fases anteriores: `[RequirePermission]`, `MapFailure`,
`PagedResult`/cursor conforme volume.

- `GET/POST /api/v1/inventory-items` (`estoque.view`/`estoque.manage`)
- `GET/PUT /api/v1/inventory-items/{id}` (`estoque.view`/`estoque.manage`)
- `POST /api/v1/inventory-movements` (`estoque.manage`) — registra Entrada/Saída/Ajuste
- `GET /api/v1/inventory-items/{itemId}/movements` (`estoque.view`, cursor, filtro `?locationId=`)
- `GET /api/v1/printers/{printerId}/inventory-movements` (`estoque.view`, cursor)
- `GET /api/v1/locations/{locationId}/inventory` (`estoque.view`) — saldo corrente
- `GET/POST /api/v1/inventory-minimums` (`estoque.view`/`estoque.manage`) — upsert por (item, local)
- `GET /api/v1/inventory-minimums/below` (`estoque.view`) — itens abaixo do mínimo

## Permissões (RBAC)

`estoque.view`, `estoque.manage` — mapeamento confirmado no `requirements.md`:
- `Estoque`: ambas (dono natural do módulo).
- `Administrador`: ambas.
- `Operacional`/`Técnico`/`Supervisor`: `estoque.view`.

## Auditoria

Via `IAuditLogger`: `inventoryitem.create`/`inventoryitem.update`,
`inventorymovement.register` (todo tipo, R6.3), com atenção redobrada em
`Ajuste` (R2.5: valor anterior/novo do saldo), `inventoryminimum.set`.

## Migração e índices

Uma migração `AddInventory`: `InventoryItems`, `InventoryMovements`,
`InventoryBalances`, `InventoryMinimums`.

Índices: `(TenantId, ItemId, LocationId, OccurredAtTicks)` em
`InventoryMovement` (histórico/cursor); `(TenantId, PrinterId, OccurredAtTicks)`
em `InventoryMovement` (R4.3); único `(TenantId, ItemId, LocationId)` em
`InventoryBalance` e em `InventoryMinimum`; `(TenantId, Name)`/`(TenantId, Sku)`
em `InventoryItem` (busca).

## Correctness properties

1. **Saldo nunca negativo**: nenhuma sequência de movimentações produz
   `InventoryBalance.Quantity < 0`.
2. **Consistência histórico↔saldo (R3.4)**: para qualquer (Item, Local), o saldo
   materializado é sempre igual ao efeito acumulado (conforme a regra de sinal
   por tipo) de todas as `InventoryMovement` daquele par, na ordem em que foram
   aplicadas.
3. **Saída bloqueada por saldo insuficiente**: uma `Saida` cuja quantidade excede
   o saldo corrente nunca é persistida (409, sem efeito parcial).
4. **Ajuste sempre auditado e justificado**: nenhuma `InventoryMovement` do tipo
   `Ajuste` é persistida sem `Reason` não vazio nem sem uma entrada
   correspondente no `AuditLog`.
5. **Isolamento multi-tenant**: nenhuma operação cria/lê/atualiza item,
   movimentação, saldo ou mínimo de um `TenantId` diferente do contexto
   autenticado.
6. **Item inativo bloqueia nova movimentação (R2.7)**: nenhuma `InventoryMovement`
   nova é persistida contra um `InventoryItem` com `IsActive = false`.

## Notas / fora de escopo

- Alertas automáticos de estoque baixo, pedido de compra/reposição e baixa
  automática a partir de suprimento SNMP: fora de escopo (ver `requirements.md`).
- `EasyPanel.Modules.Inventory` não referencia `Modules.Customers`/
  `Modules.Monitoring`; toda validação cruzada de `LocationId`/`PrinterId`
  acontece em `EasyPanel.Infrastructure.Inventory`.
- Unidade de medida única por item, sem conversão entre unidades.
