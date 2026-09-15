# Design Document — EasyPanel (FASE 10: Portal do Cliente)

## Overview

A Fase 10 introduz um **segundo nível de isolamento** — por `CustomerId`,
dentro do tenant já resolvido — e um módulo novo, `EasyPanel.Modules.
Portal`, sem nenhuma entidade persistida nova (mesmo padrão da Fase 9):
apenas DTOs, interfaces de leitura e as implementações em `Infrastructure.
Portal`, que projetam dados já existentes em `Modules.Monitoring`
(`Printer`, `PrinterCounter`), `Modules.Ticketing` (`Ticket`,
`TicketInteraction`, `TicketAttachment`) e `Modules.Billing`/`Modules.
Contracts` (`Invoice`, `Contract`).

A única mudança de esquema é uma coluna nova em `ApplicationUser`
(`CustomerId`, nula) — o vínculo usuário↔Cliente (R1). Não há tabela de
associação N:N (confirmado no `requirements.md`, questão 1: um usuário do
portal vincula-se a **no máximo um** `Customer`).

## Modelo de dados

### `ApplicationUser.CustomerId` (`Modules.Identity`)

```csharp
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public Guid? CustomerId { get; set; }   // novo — R1.1/R1.3
    public bool IsActive { get; set; }
    public bool MfaEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

`CustomerId` é nulo para todo usuário que não tem o papel `Cliente` (R1.3).
Não é uma FK de EF Core formal (mesma razão de `ApplicationUser` não ser
`TenantEntity` hoje — Identity é modelado fora do agregado tenant-cêntrico
do EF); validado por consulta explícita em `UserService`, como já é feito
para `TenantId`. Migração: `AddApplicationUserCustomerId` (coluna nula +
índice `(TenantId, CustomerId)` para a listagem futura "usuários de um
Cliente").

### Contratos de API (`UserContracts.cs`, `Modules.Identity` contracts)

`CreateUserApiRequest`/`CreateUserRequest` e `UpdateUserApiRequest`/
`UpdateUserRequest` ganham um campo `Guid? CustomerId` (R1.1). Validação em
`UserService.CreateAsync`/`UpdateAsync`:

- IF `Roles` contém `Cliente`: `CustomerId` é obrigatório e deve existir no
  mesmo tenant (`UserErrors.CustomerRequired` / reuso de padrão de erro já
  existente para FK cross-tenant → tratado como inexistente, HTTP 400);
  `Roles` não pode conter nenhum outro papel (R1.2 →
  `UserErrors.RoleConflict`, HTTP 400).
- IF `Roles` não contém `Cliente`: `CustomerId` é ignorado/forçado a `null`
  (R1.3), mesmo se informado no corpo da requisição.

`UserResponse` ganha `Guid? CustomerId` para refletir o vínculo.

### `EasyPanel.Modules.Portal` (sem entidades)

Três enums espelhados (mesmo motivo de todos os anteriores — módulo de
domínio não referencia outro módulo de domínio): sexto, sétimo e oitavo da
plataforma.

```csharp
public enum PortalPrinterStatus { Online = 0, Offline = 1, Error = 2, Maintenance = 3 } // espelha Monitoring.PrinterStatus
public enum PortalTicketStatus { Open = 0, InProgress = 1, Resolved = 2, Closed = 3 }    // espelha Ticketing.TicketStatus
public enum PortalInvoiceStatus { Rascunho = 0, Emitida = 1, Cancelada = 2 }             // espelha Billing.InvoiceStatus
```

```csharp
public sealed record PortalPrinterDto(
    Guid Id, string Model, string SerialNumber, Guid LocationId, string LocationName, PortalPrinterStatus Status);

public sealed record PortalCounterReadingDto(
    DateTimeOffset Timestamp, string CounterType, long Value);

public sealed record PortalTicketSummaryDto(
    Guid Id, string Subject, PortalTicketStatus Status, DateTimeOffset CreatedAt);

public sealed record PortalTicketDetailDto(
    Guid Id, string Subject, string Description, PortalTicketStatus Status,
    DateTimeOffset CreatedAt, IReadOnlyList<PortalTicketInteractionDto> Interactions,
    IReadOnlyList<PortalTicketAttachmentDto> Attachments);

public sealed record PortalTicketInteractionDto(DateTimeOffset Timestamp, string Message);
public sealed record PortalTicketAttachmentDto(Guid Id, string FileName, string ContentType);

public sealed record PortalInvoiceSummaryDto(
    Guid Id, int Year, int Month, PortalInvoiceStatus Status, decimal TotalAmount);

public sealed record PortalInvoiceDetailDto(
    Guid Id, int Year, int Month, PortalInvoiceStatus Status, decimal TotalAmount,
    IReadOnlyList<PortalInvoiceLineItemDto> LineItems);

public sealed record PortalInvoiceLineItemDto(string Description, long Quantity, decimal UnitPrice, decimal Amount);
```

`PortalErrors.NotFound` — único erro de domínio necessário (404 uniforme
para "não existe" e "existe mas é de outro Cliente/tenant", R6.3/R3.2/R4.2/
R5.3).

## Isolamento de Cliente (`ICustomerContext`)

Adicionado a `EasyPanel.Modules.Tenancy` (mesmo módulo de `ITenantContext`
— é o local natural: ambos são contexto de requisição resolvido de claims,
sem entidade própria):

```csharp
public interface ICustomerContext
{
    Guid? CustomerId { get; }
    bool HasCustomer { get; }
}

public sealed class CustomerContext : ICustomerContext
{
    public Guid? CustomerId { get; private set; }
    public bool HasCustomer => CustomerId.HasValue;
    public void SetCustomer(Guid customerId) => CustomerId = customerId;
}
```

Registrado `scoped`, mesmo ciclo de vida de `TenantContext`. Resolvido no
mesmo `TenantResolutionMiddleware` (não um middleware separado — a
resolução de Cliente é um passo adicional da mesma resolução de contexto
de requisição, executa sempre após o tenant já estar resolvido):

```csharp
// TenantResolutionMiddleware.Resolve, após tenantContext.SetTenant(tenantId):
var customerClaim = principal.FindFirstValue(TenancyConstants.CustomerIdClaimType);
if (Guid.TryParse(customerClaim, out var customerId))
{
    context.RequestServices.GetRequiredService<CustomerContext>().SetCustomer(customerId);
}
```

`TokenService` (`Infrastructure.Security`) ganha `CustomerIdClaimType =
"customer_id"` e embute a claim somente quando `user.CustomerId` tem valor
(mesmo padrão condicional de `tenant_id`):

```csharp
if (user.CustomerId is { } customerId)
{
    claims[CustomerIdClaimType] = customerId.ToString();
}
```

## Serviços de domínio (`Infrastructure.Portal`)

```csharp
public interface IPortalFleetService
{
    Task<Result<PagedResult<PortalPrinterDto>>> ListPrintersAsync(CursorPageRequest query, CancellationToken ct);
    Task<Result<PagedResult<PortalCounterReadingDto>>> GetCounterHistoryAsync(Guid printerId, CursorPageRequest query, CancellationToken ct);
}

public interface IPortalTicketService
{
    Task<Result<PagedResult<PortalTicketSummaryDto>>> ListAsync(CursorPageRequest query, CancellationToken ct);
    Task<Result<PortalTicketDetailDto>> GetAsync(Guid ticketId, CancellationToken ct);
}

public interface IPortalInvoiceService
{
    Task<Result<PagedResult<PortalInvoiceSummaryDto>>> ListAsync(CursorPageRequest query, CancellationToken ct);
    Task<Result<PortalInvoiceDetailDto>> GetAsync(Guid invoiceId, CancellationToken ct);
}
```

Toda implementação segue o mesmo padrão de duas camadas de filtro:

1. Filtro global de tenant (automático, via `TenantEntity`/`ITenantContext`
   — já herdado de todas as fases anteriores).
2. Filtro explícito adicional por `ICustomerContext.CustomerId`:
   - `PortalFleetService`: `Printer` filtrado por `Location.CustomerId ==
     customerId` (join já usado em relatórios da Fase 9); contadores por
     `PrinterId` pertencente a essa lista.
   - `PortalTicketService`: `Ticket.CustomerId == customerId` (coluna
     direta, sem join).
   - `PortalInvoiceService`: `Invoice` → `Contract.CustomerId ==
     customerId` (join já usado em `InvoiceService`/relatório de
     faturamento da Fase 9), **excluindo `Status == Rascunho`** (R5.2).

Se `ICustomerContext.HasCustomer` é `false` (defensivo — não deveria
ocorrer para um usuário `Cliente` válido, já que R1.1 exige `CustomerId` na
criação), todo método retorna `PortalErrors.NotFound`/lista vazia, nunca
lança exceção nem consulta sem filtro.

Um recurso (Impressora, Chamado, Fatura) que existe mas pertence a outro
Cliente do mesmo tenant — ou a outro tenant — é indistinguível de
inexistente: `PortalErrors.NotFound` → HTTP 404 em ambos os casos (R3.2,
R4.2, R5.3, R6.3 — mesmo padrão de não-enumeração cross-tenant já usado em
`UserService.FindInTenantAsync`).

## Endpoints da API

Novo `EasyPanel.Api/Controllers/Portal/`, convenção idêntica às fases
anteriores (`[RequirePermission]`, `MapFailure`, cursor pagination):

- `GET /api/v1/portal/printers` (`portal.parque.view`)
- `GET /api/v1/portal/printers/{id}/counters` (`portal.parque.view`)
- `GET /api/v1/portal/tickets` (`portal.chamado.view`)
- `GET /api/v1/portal/tickets/{id}` (`portal.chamado.view`)
- `GET /api/v1/portal/invoices` (`portal.fatura.view`)
- `GET /api/v1/portal/invoices/{id}` (`portal.fatura.view`)

Todos somente leitura (R4.3) — nenhum verbo de mutação nesta fase.

### `UsersController` (extensão)

`POST /api/v1/users` e `PUT /api/v1/users/{id}` passam a aceitar
`CustomerId` no corpo (R1.1); nenhuma rota nova — reaproveita o endpoint
existente (confirmado no `requirements.md`, questão 5).

## Permissões (RBAC)

Três permissões novas, mapeadas **exclusivamente** ao papel `Cliente`
(R6.1, questão 3 do `requirements.md` — granularidade por área, não uma
única `portal.view`, para permitir restrição futura):

- `portal.parque.view`
- `portal.chamado.view`
- `portal.fatura.view`

```csharp
[Roles.Cliente] = new[] { Permissions.PortalParqueView, Permissions.PortalChamadoView, Permissions.PortalFaturaView },
```

Nenhum outro papel recebe essas permissões — reforça R2.4 (Usuário-Cliente
nunca acessa endpoints fora do portal, e nenhum outro papel acessa o
portal, mesmo que tecnicamente HTTP-alcançável).

## Auditoria

- **Vínculo usuário↔Cliente (R1.4)**: auditado como parte de
  `AuditActions.UserCreate`/`UserUpdate` já existentes (o `CustomerId`
  passa a fazer parte do `NewValues`/`OldValues` serializado do usuário —
  nenhuma ação de auditoria nova necessária, reaproveita o mecanismo
  existente em `UserService`).
- **Leitura do portal (R3/R4/R5)**: **nenhuma** — mesma decisão da Fase 9
  (consultas `GET`, mesmo padrão de toda listagem já existente na
  plataforma).

## Índices

- Novo: `ApplicationUser(TenantId, CustomerId)` — suporta a futura
  listagem "usuários de um Cliente" e a checagem de unicidade (R1.1).
- Reaproveitados sem alteração: `Ticket(TenantId, CustomerId, ...)` (Fase
  6), `Location(TenantId, CustomerId)` (Fase 1), `Contract(TenantId,
  CustomerId)` (Fase 7) — todos já cobrem os filtros adicionais desta
  fase.

## Correctness properties

1. **Isolamento de Cliente**: nenhuma consulta do portal retorna ou agrega
   registro de um `CustomerId` diferente do `ICustomerContext` resolvido,
   mesmo dentro do mesmo tenant.
2. **Isolamento de tenant (herdado)**: nenhuma consulta do portal cruza
   `TenantId`.
3. **Não-enumeração**: um recurso de outro Cliente/tenant responde HTTP 404
   idêntico a um recurso inexistente.
4. **Exclusividade de papel**: nenhum usuário tem `Cliente` combinado com
   outro papel; nenhum usuário sem `Cliente` tem `CustomerId` não nulo.
5. **Faturas em rascunho nunca aparecem no portal**: toda `PortalInvoiceSummaryDto`/
   `PortalInvoiceDetailDto` tem `Status != Rascunho`.
6. **Fora do portal, sem acesso**: um Usuário-Cliente autenticado recebe
   403 em qualquer endpoint das Fases 1–9 (nenhuma permissão fora de
   `portal.*.view`).

## Notas / fora de escopo

- Convite/autocadastro do usuário-Cliente, abertura de chamado e pagamento
  de fatura pelo portal, múltiplos Clientes por usuário, white-label,
  exportação/relatórios no portal: fora de escopo (ver `requirements.md`).
- `EasyPanel.Modules.Portal` não referencia nenhum outro módulo de
  domínio; toda leitura cruzada (`Printer`, `Location`, `PrinterCounter`,
  `Ticket`, `Invoice`, `Contract`) acontece em `EasyPanel.Infrastructure.
  Portal`.
- `ICustomerContext`/`CustomerContext` vivem em `EasyPanel.Modules.Tenancy`
  junto de `ITenantContext`/`TenantContext`, resolvidos pelo mesmo
  `TenantResolutionMiddleware` — sem novo middleware nem novo módulo só
  para o contexto.
