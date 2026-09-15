# Implementation Plan — EasyPanel (FASE 7: Contratos)

## Overview

Constrói sobre as Fases 1 e 2 (Clientes/Locais/Identity, Impressoras/
Contadores), concluídas e verificadas, sem reabrir nenhuma delas — Cliente,
Local e Impressora são referenciados apenas por `Guid`. É a base para a Fase
8 (Fechamento e Faturamento): não executa nenhum cálculo de consolidação de
contador nem geração de fatura. Cada tarefa mantém a solução compilável com
**zero warnings** e todos os testes verdes antes de ser marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2"], "description": "Domínio: módulo Contracts (entidades, contratos) e catálogo de permissões" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Persistência: EF configurations e migração" },
    { "wave": 3, "tasks": ["3.1", "3.2", "3.3"], "description": "Serviços de contrato, escopo e franquia" },
    { "wave": 4, "tasks": ["4.1", "4.2", "4.3"], "description": "Endpoints da API" },
    { "wave": 5, "tasks": ["5.1"], "description": "Auditoria e segurança consolidadas" },
    { "wave": 6, "tasks": ["6.1", "6.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio e permissões
- [x] 1.1 Módulo `EasyPanel.Modules.Contracts`
  - Novo projeto `src/EasyPanel.Modules.Contracts` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums `ContractStatus`, `ContractCounterType` (espelha
    `Monitoring.CounterType`, sem referência de módulo)
  - Entidades `TenantEntity`: `Contract`, `ContractLocation`,
    `ContractPrinter`, `ContractFranchise`
  - DTOs (`ContractDto`, `ContractFranchiseDto`), requests
    (`CreateContractRequest`, `UpdateContractRequest`,
    `ChangeContractStatusRequest`, `SetContractFranchiseRequest`),
    `ContractQuery` e `IContractService`/`IContractScopeService`/
    `IContractFranchiseService` (interfaces)
  - `ContractErrors` (padrão de `TicketingErrors`/`InventoryErrors`)
  - _Requirements: R1.1, R2.1, R3.1, R4.1_
- [x] 1.2 Catálogo e mapeamento de permissões
  - `contrato.view`, `contrato.manage` em `Permissions.All`
  - Mapear em `RolePermissions`: `Financeiro`/`Administrador` → ambas;
    `Operacional`/`Supervisor` → `contrato.view`; `Tecnico`/`Estoque` →
    nenhuma
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R6.1, R6.2_

- [x] 2. Persistência
- [x] 2.1 EF configurations e `AppDbContext`
  - `ContractConfiguration`, `ContractLocationConfiguration`,
    `ContractPrinterConfiguration`, `ContractFranchiseConfiguration` em
    `Infrastructure/Persistence/Configurations/` (índices conforme
    `design.md`)
  - `DbSet<T>` de cada entidade em `AppDbContext`
  - _Requirements: R6.6_
- [x] 2.2 Migração `AddContracts`
  - `dotnet ef migrations add AddContracts` cobrindo as 4 novas tabelas e
    índices
  - Confirmar `has-pending-model-changes` limpo; build + testes de integração
    (SQLite `EnsureCreated`) continuam verdes
  - _Requirements: R6.6_

- [x] 3. Serviços de domínio
- [x] 3.1 `ContractService`
  - `CreateAsync` (valida `Number`, `CustomerId` do tenant,
    `EndDate >= StartDate`), `UpdateAsync` (Number/EndDate/Observations),
    `GetAsync`, `ListAsync` (filtro por Cliente/Status), `ChangeStatusAsync`
    (máquina de estados do `design.md`), `ResolveApplicableAsync`
    (cascata Impressora → Local → Cliente inteiro, só considera Contratos
    `Ativo` e vigentes na data)
  - Auditado: `contract.create`, `contract.update`, `contract.status_change`
  - Testes de integração: criação com validação de datas/cliente inválido;
    transições de status válidas/inválidas; `ResolveApplicableAsync` retorna
    o nível mais específico (impressora > local > cliente), retorna
    `null`/vazio quando não há contrato aplicável, ignora contrato fora de
    vigência ou não-Ativo; isolamento cross-tenant → 404
  - _Requirements: R1.1–R1.4, R4.1–R4.3, R5.1–R5.4, R6.3–R6.5_
- [x] 3.2 `ContractScopeService`
  - `AddLocationAsync`/`AddPrinterAsync` (valida mesmo Cliente do contrato,
    detecta conflito de vigência sobreposta no mesmo Local/Impressora entre
    Contratos não-encerrados do Cliente → 409), `RemoveLocationAsync`/
    `RemovePrinterAsync`, `ListLocationsAsync`/`ListPrintersAsync`
  - Auditado: `contract.scope_add`, `contract.scope_remove`
  - Testes de integração: adicionar Local/Impressora de Cliente diferente →
    400; conflito de vigência sobreposta → 409; remoção; listagem restrita
    ao contrato/tenant; contrato sem nenhum escopo é resolvido como
    "cobre todo o Cliente" (via 3.1)
  - _Requirements: R2.1–R2.4, R6.3–R6.5_
- [x] 3.3 `ContractFranchiseService`
  - `SetAsync` (upsert por CounterType, valida quantidade/preço não
    negativos), `ListAsync`
  - Auditado: `contractfranchise.set`
  - Testes de integração: upsert cria/atualiza; valores negativos → 400;
    no máximo uma franquia por (Contrato, CounterType); isolamento
    cross-tenant
  - _Requirements: R3.1–R3.4, R6.3–R6.5_

- [x] 4. Endpoints da API
- [x] 4.1 `ContractsController`
  - `GET`/`POST`/`GET {id}`/`PUT {id}`/`POST {id}/status` sob
    `api/v1/contracts`, `[RequirePermission]` (`contrato.view`/
    `contrato.manage`)
  - `GET api/v1/printers/{printerId}/applicable-contract?referenceDate=`
    (R4, `204` quando não há contrato aplicável)
  - _Requirements: R1.1–R1.4, R4.1–R4.3, R5.1–R5.4, R6.4, R6.5_
- [x] 4.2 `ContractScopeController`
  - `GET/POST/DELETE api/v1/contracts/{id}/locations[/{locationId}]`
  - `GET/POST/DELETE api/v1/contracts/{id}/printers[/{printerId}]`
  - `[RequirePermission]` (`contrato.view`/`contrato.manage`)
  - _Requirements: R2.1–R2.4, R6.4, R6.5_
- [x] 4.3 `ContractFranchisesController`
  - `GET/POST api/v1/contracts/{id}/franchises` (`contrato.view`/
    `contrato.manage`)
  - _Requirements: R3.1–R3.4, R6.4, R6.5_

- [x] 5. Auditoria e segurança consolidadas
- [x] 5.1 Revisão de auditoria e testes de segurança
  - Confirmado que criação/atualização/transição de status de contrato,
    adição/remoção de escopo e upsert de franquia auditam corretamente via
    `IAuditLogger` em `ContractService`/`ContractScopeService`/
    `ContractFranchiseService`
  - `ContractIsolationTests` (9 casos) em `EasyPanel.SecurityTests`:
    cross-tenant em contratos/escopo/franquias → 404 ou lista vazia;
    resolução de contrato aplicável para impressora de outro tenant → 204
    (sem vazar existência); RBAC nega `contrato.manage` a Operacional (só
    `contrato.view`) e nega toda a listagem a Técnico (sem nenhuma permissão
    de contrato); caminho positivo confirma que Financeiro (dono do módulo)
    cria contratos. A `MultiTenantIsolationWebApplicationFactory`
    (compartilhada) ganhou um usuário `Financeiro` semeado, sem afetar as
    suítes de isolamento de fases anteriores
  - _Requirements: R6.2, R6.3, R6.5_

- [x] 6. Documentação e fechamento de fase
- [x] 6.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo `Modules.Contracts`),
    `docs/DATABASE.md` (novas tabelas/índices), `docs/API.md` (novos
    endpoints), `docs/SECURITY.md` (novas permissões), `README.md`,
    `HANDOFF.md` (Fase 7 concluída, próxima fase)
  - _Requirements: cobertura documental_
- [x] 6.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln`: 0 avisos, 148 unit + 44
    security + 316 integration — 100% verde (flakiness pré-existente de
    teardown do `WebApplicationFactory` observada uma vez, confirmada como
    ambiental via retest em isolamento, mesma causa já documentada nas
    Fases 5/6)
  - `EasyPanel.WindowsClient.slnx` (Agente Windows, não tocado nesta fase):
    build confirmado com 0 avisos, baseline inalterado
  - `dotnet ef migrations has-pending-model-changes`: limpo
  - _Requirements: não-funcionais herdados_

## Notes

- Consolidação de contadores por período, cálculo de excedente real e
  geração de fatura permanecem fora de escopo (ver `requirements.md`/
  `design.md`) — pertencem à Fase 8.
- `EasyPanel.Modules.Contracts` não referencia `Modules.Customers`/
  `Modules.Monitoring`; toda validação cruzada de `CustomerId`/`LocationId`/
  `PrinterId` acontece em `EasyPanel.Infrastructure.Contracts`.
