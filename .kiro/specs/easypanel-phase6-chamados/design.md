# Design Document — EasyPanel (FASE 6: Chamados/Helpdesk e SLA)

## Overview

A Fase 6 introduz um módulo de domínio novo, `EasyPanel.Modules.Ticketing`
(mesmo padrão arquitetural de `Modules.Alerting`/`Modules.Inventory`):
entidades próprias, sem referenciar `Modules.Customers`/`Modules.Monitoring`
— Cliente, Local, Impressora e Técnico (usuário) são referenciados apenas por
`Guid`. A validação de que esses `Guid` pertencem ao tenant corrente acontece
em `EasyPanel.Infrastructure` (mesmo padrão do `AlertEngine`/
`InventoryMovementService`).

Escopo: abertura de chamados vinculados a Cliente (+ Local/Impressora
opcionais), ciclo de vida de status com histórico append-only de interações
(comentário, mudança de status, atribuição), política de SLA por (tenant,
prioridade) com cálculo de prazos e indicador de violação, e anexos
armazenados no MinIO — primeira implementação funcional de storage da
plataforma. Sem SLA por Contrato, sem notificação proativa de violação, sem
portal do cliente (ver `requirements.md`, "Fora de escopo").

## Modelo de dados (`EasyPanel.Modules.Ticketing`)

### Enums

```csharp
public enum TicketStatus { Aberto = 0, EmAndamento = 1, AguardandoCliente = 2, Resolvido = 3, Fechado = 4, Cancelado = 5 }

public enum TicketPriority { Baixa = 0, Media = 1, Alta = 2, Urgente = 3 }

// Tipo de Interação no histórico append-only (R2.5/R2.6).
public enum TicketInteractionType { Comentario = 0, MudancaStatus = 1, Atribuicao = 2 }

// Estado de cumprimento de um prazo de SLA (R4.4/R4.5).
public enum SlaComplianceStatus { Pendente = 0, Cumprido = 1, Violado = 2 }
```

### `Ticket : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| Title | string (≤200) | obrigatório |
| Description | string (≤4000) | |
| CustomerId | Guid | obrigatório (R1.3) |
| LocationId | Guid? | opcional (R1.4) |
| PrinterId | Guid? | opcional (R1.4) |
| Priority | TicketPriority | |
| Status | TicketStatus | default `Aberto` |
| RequestedByUserId | Guid | ator de abertura |
| AssignedToUserId | Guid? | Técnico responsável |
| FirstResponseDueAt / FirstResponseDueAtTicks | DateTimeOffset / long | calculado na abertura (R1.5/R4.3) |
| FirstResponseAt / FirstResponseAtTicks | DateTimeOffset? / long? | timestamp da 1ª Interação de resposta de um Técnico (R4.4) |
| FirstResponseCompliance | SlaComplianceStatus | default `Pendente` |
| ResolutionDueAt / ResolutionDueAtTicks | DateTimeOffset / long | calculado na abertura |
| ResolvedAt / ResolvedAtTicks | DateTimeOffset? / long? | 1ª transição para `Resolvido` (R2.7) |
| ResolutionCompliance | SlaComplianceStatus | default `Pendente` |
| CreatedAtTicks | long | cursor portável para listagem (R3.1) |

Não há coluna `IsActive`/soft-delete: um Chamado sempre existe uma vez criado;
seu ciclo de vida é expresso pelo `Status`.

### `TicketInteraction : TenantEntity` (somente-adição)

| Campo | Tipo | Notas |
|---|---|---|
| TicketId | Guid | FK `Restrict` |
| Type | TicketInteractionType | |
| ActorUserId | Guid | |
| Comment | string? (≤4000) | obrigatório sse `Type = Comentario` |
| FromStatus / ToStatus | TicketStatus? | preenchidos sse `Type = MudancaStatus` |
| AssignedToUserId | Guid? | preenchido sse `Type = Atribuicao` (novo responsável; `null` = desatribuído) |
| OccurredAt / OccurredAtTicks | DateTimeOffset / long | cursor portável (R2.6) |

Uma transição de status **também** atualiza `Ticket.Status` diretamente (campo
desnormalizado para consulta/filtro em R3.2), mas a fonte de verdade histórica
é a sequência de `TicketInteraction`.

### `SlaPolicy : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| Priority | TicketPriority | |
| FirstResponseMinutes | int | > 0 |
| ResolutionMinutes | int | > 0, e ≥ `FirstResponseMinutes` |

Índice único `(TenantId, Priority)` — upsert por prioridade (R4.1). Ausência de
linha para uma prioridade → usa o prazo padrão de plataforma
(`TicketingOptions.DefaultSla[Priority]`, R4.2).

### `TicketAttachment : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| TicketId | Guid | FK `Restrict` |
| FileName | string (≤255) | nome original |
| ContentType | string (≤100) | MIME validado contra allowlist (R5.2) |
| SizeBytes | long | ≤ limite configurado (R5.2) |
| StorageKey | string (≤500) | caminho no bucket, isolado por tenant (R5.1) |
| UploadedByUserId | Guid | |
| UploadedAt / UploadedAtTicks | DateTimeOffset / long | |

`StorageKey` segue o padrão `tickets/{tenantId}/{ticketId}/{attachmentId}-{fileName sanitizado}`
— o isolamento por tenant está no próprio caminho do objeto, não apenas na
linha de metadados, para que uma eventual leitura direta do bucket (fora da
API) ainda respeite o particionamento por tenant.

## Storage de anexos (`IFileStorage`, novo em `Infrastructure`)

Primeira implementação funcional do MinIO na plataforma (`requirements.md`,
premissa 2). Abstração de domínio simples, implementada sobre o SDK oficial
`Minio` (S3-compatible), pinado na versão mais recente sem vulnerabilidade
conhecida no momento da implementação (verificar com
`dotnet list package --vulnerable --include-transitive` antes de fechar a
tarefa, mesmo procedimento da Fase 4 com `Lextm.SharpSnmpLib`).

```csharp
// EasyPanel.Shared.Kernel — abstração de domínio, sem dependência do SDK MinIO
public interface IFileStorage
{
    Task<Result> UploadAsync(string key, Stream content, string contentType, long sizeBytes, CancellationToken ct);
    Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct);
    Task<Result> DeleteAsync(string key, CancellationToken ct);
}
```

`MinioFileStorage : IFileStorage` em `Infrastructure/Storage/`, configurado a
partir de `StorageOptions` estendida (novos campos `AccessKey`, `SecretKey`,
`Bucket` — já provisionados no `docker-compose.yml`/`.env`, mas ainda não
vinculados em `StorageOptions`; ver "Alterações em código existente"). Upload e
download passam pelo backend como stream (sem presigned URL nesta fase — mais
simples de auditar e de manter o isolamento por tenant na própria rota da
API, já protegida por `[RequirePermission]`).

**Validação de anexo (R5.2, antes de qualquer chamada ao storage):**
- Tamanho: ≤ `TicketingOptions.MaxAttachmentSizeBytes` (default 10 MB).
- Tipo: `ContentType` declarado deve constar em
  `TicketingOptions.AllowedAttachmentContentTypes` (default: `image/png`,
  `image/jpeg`, `image/webp`, `application/pdf`).
- Falha em qualquer verificação → 400, sem chamar `IFileStorage.UploadAsync`
  nem persistir `TicketAttachment`.

## Serviços de domínio

### `ITicketService`

```csharp
Task<Result<TicketDto>> CreateAsync(CreateTicketRequest request, CancellationToken ct);
Task<Result<TicketDto>> GetAsync(Guid id, CancellationToken ct);
Task<Result<PagedResult<TicketDto>>> ListAsync(TicketQuery query, CancellationToken ct); // filtros R3.2

Task<Result<TicketDto>> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct);
Task<Result<TicketDto>> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct);
Task<Result<TicketInteractionDto>> AddCommentAsync(Guid id, AddTicketCommentRequest request, CancellationToken ct);

Task<Result<TicketCursorPage<TicketInteractionDto>>> ListInteractionsAsync(
    Guid ticketId, string? cursor, int pageSize, CancellationToken ct); // R2.6

Task<Result<PagedResult<TicketDto>>> ListSlaBreachedAsync(PageRequest page, CancellationToken ct); // R4.6
```

**`CreateAsync`:**
1. Valida `Title` não vazio e `CustomerId` existente no tenant — senão 400.
2. Se `LocationId`/`PrinterId` informados, valida pertencimento ao tenant —
   senão 400.
3. Resolve a `SlaPolicy` da prioridade (ou o padrão de plataforma) e calcula
   `FirstResponseDueAt`/`ResolutionDueAt` = `now + minutos` (R1.5/R4.3).
4. Persiste o `Ticket` com `Status = Aberto`, audita `ticket.create`.

**`ChangeStatusAsync`:**
1. Valida a transição contra uma tabela de transições permitidas (ver
   "Máquina de estados" abaixo) — senão 400 (R2.3).
2. Insere `TicketInteraction` (`MudancaStatus`, `FromStatus`/`ToStatus`),
   atualiza `Ticket.Status` na mesma operação.
3. Se `ToStatus = Resolvido` e `ResolvedAt` ainda não preenchido, grava
   `ResolvedAt`/`ResolvedAtTicks` e calcula `ResolutionCompliance` comparando
   ao `ResolutionDueAt` (R2.7/R4.5).
4. Audita `ticket.status_change` (ator, de/para).

**`AddCommentAsync`:**
1. Insere `TicketInteraction` (`Comentario`).
2. Se é a primeira Interação com `ActorUserId` diferente de
   `RequestedByUserId` (i.e., a primeira resposta de um Técnico) e
   `FirstResponseAt` ainda não preenchido, grava
   `FirstResponseAt`/`FirstResponseAtTicks` e calcula
   `FirstResponseCompliance` comparando ao `FirstResponseDueAt` (R4.4).
3. Audita `ticket.comment`.

**`ListSlaBreachedAsync` (R4.6):** busca os `Ticket` do tenant cujo
`FirstResponseCompliance = Violado` OU `ResolutionCompliance = Violado`, OU
(para chamados ainda sem resposta/resolução) cujo `FirstResponseDueAt`/
`ResolutionDueAt` já passou em relação a `now` — mesmo raciocínio "calcular em
memória, paginar depois" já usado em `ListBelowMinimumAsync` da Fase 5, pela
mesma razão de portabilidade SQLite/PostgreSQL.

### `ISlaPolicyService`

```csharp
Task<Result<IReadOnlyList<SlaPolicyDto>>> ListAsync(CancellationToken ct);
Task<Result<SlaPolicyDto>> SetAsync(SetSlaPolicyRequest request, CancellationToken ct); // upsert por Priority, auditado
```

### `ITicketAttachmentService`

```csharp
Task<Result<TicketAttachmentDto>> UploadAsync(Guid ticketId, UploadTicketAttachmentRequest request, Stream content, CancellationToken ct);
Task<Result<TicketAttachmentDownload>> DownloadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct);
Task<Result<IReadOnlyList<TicketAttachmentDto>>> ListAsync(Guid ticketId, CancellationToken ct);
```

`UploadAsync` valida o Chamado (existe, pertence ao tenant), valida
tamanho/tipo (R5.2), grava no `IFileStorage` sob a chave determinística acima
e só então persiste `TicketAttachment` — se a escrita no storage falhar, nada
é persistido (sem metadado órfão apontando para um objeto inexistente).
`DownloadAsync` resolve o `TicketAttachment` restrito ao tenant (404 se de
outro tenant ou inexistente) e então lê o stream do `IFileStorage`.

## Máquina de estados do Chamado (R2.1/R2.3)

```
Aberto ──────────────► EmAndamento ─────► AguardandoCliente
  │                        │  ▲                   │
  │                        │  └───────────────────┘
  │                        ▼
  │                    Resolvido ─────► Fechado
  │                        │
  └────────────────────────┴──────────► Cancelado
```

Transições permitidas: `Aberto → {EmAndamento, Cancelado}`;
`EmAndamento → {AguardandoCliente, Resolvido, Cancelado}`;
`AguardandoCliente → {EmAndamento, Resolvido, Cancelado}`;
`Resolvido → {EmAndamento, Fechado}` (reabertura explícita volta a
`EmAndamento`, `Fechado` encerra); `Fechado`/`Cancelado` são terminais (nenhuma
transição de saída). Qualquer outra combinação → 400 (R2.3).

## Endpoints da API

Convenção idêntica às fases anteriores: `[RequirePermission]`, `MapFailure`,
`PagedResult`/cursor conforme volume.

- `GET/POST /api/v1/tickets` (`chamado.view`/`chamado.manage`)
- `GET /api/v1/tickets/{id}` (`chamado.view`)
- `POST /api/v1/tickets/{id}/status` (`chamado.manage`)
- `POST /api/v1/tickets/{id}/assign` (`chamado.manage`)
- `POST /api/v1/tickets/{id}/comments` (`chamado.manage`)
- `GET /api/v1/tickets/{id}/interactions` (`chamado.view`, cursor)
- `GET /api/v1/tickets/sla-breached` (`chamado.view`)
- `POST /api/v1/tickets/{id}/attachments` (`chamado.manage`, multipart)
- `GET /api/v1/tickets/{id}/attachments` (`chamado.view`)
- `GET /api/v1/tickets/{id}/attachments/{attachmentId}` (`chamado.view`, download)
- `GET/POST /api/v1/sla-policies` (`chamado.view`/`sla.manage`)

## Permissões (RBAC)

`chamado.view`, `chamado.manage`, `sla.manage` — mapeamento confirmado no
`requirements.md`:
- `Supervisor`: todas as três (dono do módulo).
- `Tecnico`: `chamado.view` + `chamado.manage`.
- `Administrador`: todas as três.
- `Operacional`: `chamado.view`.
- `Financeiro`/`Estoque`: nenhuma.

## Auditoria

Via `IAuditLogger`: `ticket.create`, `ticket.status_change` (ator, de/para),
`ticket.assign` (ator, técnico anterior/novo), `ticket.comment`,
`ticket.attachment_upload`, `slapolicy.set` (ator, prioridade, valor
anterior/novo).

## Migração e índices

Uma migração `AddTicketing`: `Tickets`, `TicketInteractions`, `SlaPolicies`,
`TicketAttachments`.

Índices: `(TenantId, Status, CreatedAtTicks)`, `(TenantId, CustomerId)`,
`(TenantId, AssignedToUserId)` em `Ticket` (listagem/filtro, R3.2); `(TenantId,
TicketId, OccurredAtTicks)` em `TicketInteraction` (histórico/cursor, R2.6);
único `(TenantId, Priority)` em `SlaPolicy`; `(TenantId, TicketId)` em
`TicketAttachment`.

## Alterações em código existente

- `StorageOptions` (`Infrastructure/Health/`) ganha `AccessKey`, `SecretKey`,
  `Bucket` (já existentes em `docker-compose.yml`/`.env`, hoje não vinculados)
  — reaproveitado tanto pelo `StorageHealthCheck` (sem mudança de
  comportamento) quanto pelo novo `MinioFileStorage`.
- `Permissions`/`RolePermissions`: `chamado.view`, `chamado.manage`,
  `sla.manage` adicionados ao catálogo e mapeados conforme RBAC acima.
- Nenhuma mudança em `Modules.Customers`/`Modules.Monitoring`/
  `Modules.Identity` além do catálogo de permissões.

## Correctness properties

1. **Isolamento multi-tenant**: nenhuma operação cria/lê/atualiza Chamado,
   Interação, Anexo ou política de SLA de um `TenantId` diferente do contexto
   autenticado.
2. **Histórico append-only**: nenhuma `TicketInteraction` é alterada ou
   removida após criada; o `Status` corrente do `Ticket` é sempre consistente
   com a última `TicketInteraction` do tipo `MudancaStatus`.
3. **Transições de status restritas**: nenhum `Ticket` alcança um `Status` por
   uma transição fora da máquina de estados definida.
4. **SLA calculado na abertura, nunca retroativo**: `FirstResponseDueAt`/
   `ResolutionDueAt` são gravados na criação do `Ticket` e nunca recalculados
   por uma alteração posterior da `SlaPolicy` (mudar a política afeta apenas
   chamados abertos depois da mudança).
5. **Anexo nunca órfão**: todo `TicketAttachment` persistido tem um objeto
   correspondente gravável no `IFileStorage`; nenhuma falha de upload deixa
   metadado sem arquivo, nem arquivo sem metadado (upload confirmado antes de
   persistir a linha).
6. **Validação de anexo antes do storage**: nenhum arquivo que exceda o
   tamanho máximo ou tenha tipo fora da allowlist chega a ser enviado ao
   `IFileStorage`.

## Notas / fora de escopo

- SLA por Contrato, notificação proativa de violação, portal do cliente,
  horário comercial no cálculo de SLA: fora de escopo (ver `requirements.md`).
- `EasyPanel.Modules.Ticketing` não referencia `Modules.Customers`/
  `Modules.Monitoring`/`Modules.Identity`; toda validação cruzada de
  `CustomerId`/`LocationId`/`PrinterId`/`AssignedToUserId` acontece em
  `EasyPanel.Infrastructure.Ticketing`.
- Upload/download de anexo passa pelo backend (stream), sem presigned URL
  direta ao MinIO nesta fase.
