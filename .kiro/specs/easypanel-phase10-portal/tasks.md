# Implementation Plan — EasyPanel (FASE 10: Portal do Cliente)

## Overview

Constrói sobre as Fases 1 (Identity/Tenancy), 2 (Monitoring), 6
(Ticketing), 7 (Contracts) e 8 (Billing), todas concluídas: adiciona um
segundo nível de isolamento (por Cliente, dentro do tenant) e um módulo
novo, somente leitura, para o papel `Cliente`. Única mudança de esquema:
coluna `CustomerId` em `ApplicationUser`. Cada tarefa mantém a solução
compilável com **zero warnings** e todos os testes verdes antes de ser
marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2"], "description": "Vínculo usuário↔Cliente: coluna, contratos de API, validação, migração" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Segundo nível de isolamento: ICustomerContext/CustomerContext, claim JWT, resolução no middleware" },
    { "wave": 3, "tasks": ["3.1"], "description": "Domínio: módulo Portal (DTOs, interfaces) e catálogo de permissões" },
    { "wave": 4, "tasks": ["4.1", "4.2", "4.3"], "description": "Serviços de leitura: parque/contadores, chamados, faturas" },
    { "wave": 5, "tasks": ["5.1"], "description": "Endpoints da API do portal" },
    { "wave": 6, "tasks": ["6.1"], "description": "Segurança consolidada" },
    { "wave": 7, "tasks": ["7.1", "7.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Vínculo usuário↔Cliente
- [x] 1.1 Coluna `ApplicationUser.CustomerId` e migração
  - Adicionar `Guid? CustomerId` a `ApplicationUser` (`Modules.Identity`)
  - Configuração EF: índice `(TenantId, CustomerId)`
  - Migração `AddApplicationUserCustomerId`
  - `dotnet ef migrations has-pending-model-changes` limpo após gerar
  - _Requirements: R1.1_
- [x] 1.2 Contratos de API e validação em `UserService`
  - `CreateUserApiRequest`/`UpdateUserApiRequest` (API) e
    `CreateUserRequest`/`UpdateUserRequest` (domínio) ganham `Guid?
    CustomerId`; `UserResponse`/`UserDto` ganham `CustomerId` na projeção
  - `UserService.CreateAsync`/`UpdateAsync`: IF `Roles` contém `Cliente` →
    exige `CustomerId` válido no tenant (senão 400) e proíbe qualquer
    outro papel junto (senão 400); IF `Roles` não contém `Cliente` →
    força `CustomerId = null`
    - `UserErrors.CustomerRequired`, `UserErrors.RoleConflict`,
      `UserErrors.CustomerNotFound` (padrão `NotFound`/400, sem vazamento
      cross-tenant)
  - Testes de integração/unitários: criação com `Cliente` sem
    `CustomerId` → falha; `Cliente` + outro papel → falha; `CustomerId`
    de outro tenant → falha; caminho positivo cria e persiste o vínculo;
    atualização reconcilia `CustomerId` ao trocar papéis
  - Auditoria: `NewValues`/`OldValues` de `UserCreate`/`UserUpdate`
    passam a incluir `CustomerId` (sem ação de auditoria nova)
  - _Requirements: R1.1, R1.2, R1.3, R1.4_

- [x] 2. Segundo nível de isolamento (Cliente)
- [x] 2.1 `ICustomerContext`/`CustomerContext` e claim JWT
  - `ICustomerContext`/`CustomerContext` em `Modules.Tenancy` (mesmo
    padrão scoped de `ITenantContext`/`TenantContext`)
  - `TokenService`: `CustomerIdClaimType = "customer_id"`, embutida
    apenas quando `user.CustomerId` tem valor
  - Testes unitários: token de usuário `Cliente` contém a claim; token
    de usuário sem `CustomerId` não contém
  - _Requirements: R2.1_
- [x] 2.2 Resolução no `TenantResolutionMiddleware`
  - Após `SetTenant`, ler a claim `customer_id` e chamar
    `CustomerContext.SetCustomer` quando presente e válida
  - Testes de integração: requisição autenticada como `Cliente` resolve
    `ICustomerContext.CustomerId` corretamente; requisição sem a claim
    mantém `HasCustomer == false`
  - _Requirements: R2.2, R2.3_

- [x] 3. Domínio e permissões do portal
- [x] 3.1 Módulo `EasyPanel.Modules.Portal` e catálogo de permissões
  - Novo projeto `src/EasyPanel.Modules.Portal` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums `PortalPrinterStatus`, `PortalTicketStatus`,
    `PortalInvoiceStatus` (espelham os enums originais, sem referência de
    módulo)
  - DTOs (`PortalPrinterDto`, `PortalCounterReadingDto`,
    `PortalTicketSummaryDto`/`PortalTicketDetailDto` com
    interações/anexos, `PortalInvoiceSummaryDto`/`PortalInvoiceDetailDto`
    com linhas) e interfaces (`IPortalFleetService`,
    `IPortalTicketService`, `IPortalInvoiceService`)
  - `PortalErrors.NotFound`
  - `portal.parque.view`, `portal.chamado.view`, `portal.fatura.view` em
    `Permissions.All`; mapeadas exclusivamente ao papel `Cliente` em
    `RolePermissions`
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R6.1, R6.2_

- [x] 4. Serviços de leitura (`Infrastructure.Portal`)
- [x] 4.1 `PortalFleetService`
  - `ListPrintersAsync`: `Printer` filtrado por tenant (global) +
    `Location.CustomerId == ICustomerContext.CustomerId`, cursor
  - `GetCounterHistoryAsync`: histórico de `PrinterCounter` de uma
    Impressora, só se ela pertencer ao Cliente do contexto (senão
    `PortalErrors.NotFound`), cursor
  - Testes de integração: listagem restrita ao Cliente; impressora de
    outro Cliente do mesmo tenant → 404 no histórico; isolamento
    cross-tenant (herdado)
  - _Requirements: R3.1, R3.2_
- [x] 4.2 `PortalTicketService`
  - `ListAsync`: `Ticket.CustomerId == ICustomerContext.CustomerId`,
    cursor
  - `GetAsync`: detalhe com interações/anexos, só se o Chamado pertencer
    ao Cliente (senão `PortalErrors.NotFound`)
  - Testes de integração: listagem restrita; chamado de outro Cliente →
    404; isolamento cross-tenant
  - _Requirements: R4.1, R4.2, R4.3_
- [x] 4.3 `PortalInvoiceService`
  - `ListAsync`: `Invoice` → `Contract.CustomerId ==
    ICustomerContext.CustomerId`, excluindo `Status == Rascunho`, cursor
  - `GetAsync`: detalhe com linhas, só se a Fatura pertencer ao Cliente e
    não estiver em rascunho (senão `PortalErrors.NotFound`)
  - Testes de integração: listagem restrita e sem rascunhos; fatura de
    outro Cliente → 404; fatura em rascunho do próprio Cliente → 404;
    isolamento cross-tenant
  - _Requirements: R5.1, R5.2, R5.3_

- [x] 5. Endpoints da API
- [x] 5.1 Controllers do portal
  - `GET api/v1/portal/printers`, `GET api/v1/portal/printers/{id}/
    counters` (`portal.parque.view`)
  - `GET api/v1/portal/tickets`, `GET api/v1/portal/tickets/{id}`
    (`portal.chamado.view`)
  - `GET api/v1/portal/invoices`, `GET api/v1/portal/invoices/{id}`
    (`portal.fatura.view`)
  - `UsersController`: aceitar `CustomerId` em `POST`/`PUT` (Task 1.2 já
    cobre a validação; aqui só o mapeamento do contrato de API)
  - _Requirements: R3.1–R3.2, R4.1–R4.3, R5.1–R5.3, R6.2_

- [x] 6. Segurança consolidada
- [x] 6.1 Testes de segurança
  - `PortalIsolationTests` em `EasyPanel.SecurityTests`: um
    Usuário-Cliente nunca acessa parque/chamados/faturas de outro
    Cliente do mesmo tenant (404) nem de outro tenant; RBAC nega
    `portal.*.view` a papéis administrativos/operacionais; RBAC nega
    permissões de negócio (ex. `ticket.view`, `invoice.view`) a um
    Usuário-Cliente — confirma que ele não acessa nenhum endpoint fora
    do portal; caminho positivo confirma que um Usuário-Cliente acessa
    seu próprio parque/chamados/faturas; fatura em rascunho nunca
    aparece nem por acesso direto ao id
  - _Requirements: R2.4, R6.3_

- [x] 7. Documentação e fechamento de fase
- [x] 7.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo `Modules.Portal`, novo contexto
    `ICustomerContext`), `docs/DATABASE.md` (coluna `ApplicationUser.
    CustomerId` + índice), `docs/API.md` (novos endpoints do portal e
    campo `CustomerId` em usuários), `docs/SECURITY.md` (novas
    permissões, segundo nível de isolamento, decisão de não auditar
    leituras do portal), `README.md`, `HANDOFF.md` (Fase 10 concluída —
    roteiro atual completo)
  - _Requirements: cobertura documental_
- [x] 7.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln`: 0 avisos, todos os
    testes verdes
  - `EasyPanel.WindowsClient.slnx` (Agente Windows, não tocado nesta
    fase): build confirmado com 0 avisos, baseline inalterado
  - `dotnet ef migrations has-pending-model-changes`: limpo
  - _Requirements: não-funcionais herdados_

## Notes

- Convite/autocadastro do usuário-Cliente, abertura de chamado e
  pagamento de fatura pelo portal, múltiplos Clientes por usuário,
  white-label, exportação/relatórios no portal permanecem fora de escopo
  (ver `requirements.md`/`design.md`).
- `EasyPanel.Modules.Portal` não referencia nenhum outro módulo de
  domínio; toda leitura cruzada acontece em `EasyPanel.Infrastructure.
  Portal`.
- `ICustomerContext`/`CustomerContext` vivem em `EasyPanel.Modules.
  Tenancy`, resolvidos pelo `TenantResolutionMiddleware` já existente.
- Nenhuma operação de leitura desta fase é auditada; apenas o vínculo
  usuário↔Cliente (via `UserCreate`/`UserUpdate` já auditados).
