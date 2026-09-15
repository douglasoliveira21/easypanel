# Implementation Plan — EasyPanel (FASE 5: Estoque)

## Overview

Constrói sobre as Fases 1, 2 e 4 (Clientes/Locais, Impressoras, Suprimentos),
concluídas e verificadas, sem reabrir nenhuma delas — Local e Impressora são
referenciados apenas por `Guid`. Cada tarefa mantém a solução compilável com
**zero warnings** e todos os testes verdes antes de ser marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2"], "description": "Domínio: módulo Inventory (entidades, contratos) e catálogo de permissões" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Persistência: EF configurations e migração" },
    { "wave": 3, "tasks": ["3.1", "3.2"], "description": "Serviços de item e de movimentação/saldo/mínimo" },
    { "wave": 4, "tasks": ["4.1", "4.2", "4.3"], "description": "Endpoints da API" },
    { "wave": 5, "tasks": ["5.1"], "description": "Auditoria e segurança consolidadas" },
    { "wave": 6, "tasks": ["6.1", "6.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio e permissões
- [x] 1.1 Módulo `EasyPanel.Modules.Inventory`
  - Novo projeto `src/EasyPanel.Modules.Inventory` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums `InventoryMovementType` (Entrada/Saida/Ajuste),
    `AdjustmentDirection` (Increase/Decrease)
  - Entidades `TenantEntity`: `InventoryItem`, `InventoryMovement`,
    `InventoryBalance`, `InventoryMinimum`
  - DTOs (`InventoryItemDto`, `InventoryMovementDto`, `InventoryBalanceDto`,
    `InventoryMinimumDto`), requests (`CreateInventoryItemRequest`,
    `UpdateInventoryItemRequest`, `RegisterInventoryMovementRequest`,
    `SetInventoryMinimumRequest`), `InventoryCursorPage<T>` e
    `IInventoryItemService`/`IInventoryMovementService` (interfaces)
  - `InventoryErrors` (padrão de `MonitoringErrors`/`SupplyErrors`)
  - _Requirements: R1.1, R2.1, R3.1, R5.1_
- [x] 1.2 Catálogo e mapeamento de permissões
  - `estoque.view`, `estoque.manage` em `Permissions.All`
  - Mapear em `RolePermissions`: `Estoque`/`Administrador` → ambas;
    `Operacional`/`Técnico`/`Supervisor` → `estoque.view`
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R6.1, R6.2_

- [x] 2. Persistência
- [x] 2.1 EF configurations e `AppDbContext`
  - `InventoryItemConfiguration`, `InventoryMovementConfiguration`,
    `InventoryBalanceConfiguration`, `InventoryMinimumConfiguration` em
    `Infrastructure/Persistence/Configurations/` (índices conforme `design.md`)
  - `DbSet<T>` de cada entidade em `AppDbContext`
  - _Requirements: R6.6_
- [x] 2.2 Migração `AddInventory`
  - `dotnet ef migrations add AddInventory` cobrindo as 4 novas tabelas e índices
  - Confirmar `has-pending-model-changes` limpo; build + testes de integração
    (SQLite `EnsureCreated`) continuam verdes
  - _Requirements: R6.6_

- [x] 3. Serviços de domínio
- [x] 3.1 `InventoryItemService`
  - CRUD (`CreateAsync`, `UpdateAsync` incluindo `IsActive`, `GetAsync`,
    `ListAsync` com busca por nome/SKU), auditado em create/update
  - Testes de integração: criação/atualização, validação de nome obrigatório
    (R1.3), desativação preserva histórico (R1.5), isolamento cross-tenant → 404
  - _Requirements: R1.1–R1.5, R6.3, R6.4, R6.5_
- [x] 3.2 `InventoryMovementService`
  - `RegisterAsync` transacional: valida item ativo do tenant, local do tenant,
    impressora do tenant quando informada, exige `Reason`+`AdjustmentDirection`
    em `Ajuste`, calcula efeito no saldo (R2.4/R2.5, piso em zero para Ajuste),
    persiste movimento + saldo materializado na mesma operação, audita
  - `ListHistoryAsync` (cursor por item/local), `ListByPrinterAsync` (cursor),
    `GetBalanceByLocationAsync`, `SetMinimumAsync` (upsert auditado),
    `ListBelowMinimumAsync`
  - Testes de integração: Entrada/Saída atualizam saldo corretamente; Saída sem
    saldo suficiente → 409; Ajuste sem justificativa → 400; Ajuste-Decrease trava
    em zero; movimentação contra item inativo → 400; movimentação com
    local/impressora de outro tenant → 400; cursor pagination do histórico (por
    item e por impressora); consulta de saldo por local; upsert de mínimo e
    listagem de itens abaixo do mínimo; isolamento cross-tenant → 404/vazio
  - _Requirements: R2.1–R2.7, R3.1–R3.4, R4.1–R4.3, R5.1–R5.3, R6.3, R6.4, R6.5_

- [x] 4. Endpoints da API
- [x] 4.1 `InventoryItemsController`
  - `GET`/`GET {id}`/`POST`/`PUT {id}` sob `api/v1/inventory-items`,
    `[RequirePermission]` (`estoque.view`/`estoque.manage`)
  - _Requirements: R1.1–R1.5, R6.4, R6.5_
- [x] 4.2 `InventoryMovementsController`
  - `POST api/v1/inventory-movements` (`estoque.manage`)
  - `GET api/v1/inventory-items/{itemId}/movements` (cursor, filtro
    `?locationId=`, `estoque.view`)
  - `GET api/v1/printers/{printerId}/inventory-movements` (cursor, `estoque.view`)
  - _Requirements: R2.1–R2.7, R3.3, R4.2, R4.3, R6.4, R6.5_
- [x] 4.3 Saldo e mínimo (`InventoryStockController`)
  - `GET api/v1/locations/{locationId}/inventory` (`estoque.view`)
  - `POST api/v1/inventory-minimums` (upsert, `estoque.manage`)
  - `GET api/v1/inventory-minimums/below` (`estoque.view`)
  - _Requirements: R3.2, R5.1–R5.3, R6.4, R6.5_

- [x] 5. Auditoria e segurança consolidadas
- [x] 5.1 Revisão de auditoria e testes de segurança
  - Confirmado que create/update de item, toda movimentação e set de mínimo
    auditam corretamente (ator, antes/depois quando aplicável) via
    `IAuditLogger` em `InventoryItemService`/`InventoryMovementService`
  - `InventoryIsolationTests` (9 casos) em `EasyPanel.SecurityTests`:
    cross-tenant em itens/movimentações/histórico/saldo/mínimo → 404 ou lista
    vazia; RBAC nega `estoque.manage` a Técnico (só `estoque.view`) em
    criação de item e registro de movimento; Técnico consegue listar itens
  - _Requirements: R6.2, R6.3, R6.5_

- [x] 6. Documentação e fechamento de fase
- [x] 6.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo `Modules.Inventory`), `docs/DATABASE.md`
    (novas tabelas/índices), `docs/API.md` (novos endpoints), `docs/SECURITY.md`
    (novas permissões), `README.md`, `HANDOFF.md` (Fase 5 concluída, próxima fase)
  - _Requirements: cobertura documental_
- [x] 6.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln`: 0 avisos, 148 unit + 25
    security + 264 integration — 100% verde
  - `EasyPanel.WindowsClient.slnx` (Agente Windows, não tocado nesta fase):
    build confirmado com 0 avisos, baseline inalterado
  - `dotnet ef migrations has-pending-model-changes`: limpo
  - _Requirements: não-funcionais herdados_

## Notes

- Alertas automáticos de estoque baixo, pedido de compra/reposição e baixa
  automática a partir de suprimento SNMP permanecem fora de escopo (ver
  `requirements.md`/`design.md`).
- `EasyPanel.Modules.Inventory` não referencia `Modules.Customers`/
  `Modules.Monitoring`; toda validação cruzada de `LocationId`/`PrinterId`
  acontece em `EasyPanel.Infrastructure.Inventory`.
