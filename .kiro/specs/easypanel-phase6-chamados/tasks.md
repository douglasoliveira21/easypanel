# Implementation Plan — EasyPanel (FASE 6: Chamados/Helpdesk e SLA)

## Overview

Constrói sobre as Fases 1, 2 e 5 (Clientes/Locais/Identity, Impressoras,
Estoque), concluídas e verificadas, sem reabrir nenhuma delas — Cliente,
Local, Impressora e usuário (Técnico) são referenciados apenas por `Guid`.
Introduz a primeira implementação funcional de storage (MinIO) da plataforma.
Cada tarefa mantém a solução compilável com **zero warnings** e todos os
testes verdes antes de ser marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2", "1.3"], "description": "Domínio: módulo Ticketing (entidades, contratos), abstração IFileStorage, catálogo de permissões" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Persistência: EF configurations e migração" },
    { "wave": 3, "tasks": ["3.1", "3.2", "3.3", "3.4"], "description": "MinioFileStorage, serviços de chamado/SLA/anexo" },
    { "wave": 4, "tasks": ["4.1", "4.2", "4.3"], "description": "Endpoints da API" },
    { "wave": 5, "tasks": ["5.1"], "description": "Auditoria e segurança consolidadas" },
    { "wave": 6, "tasks": ["6.1", "6.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio e permissões
- [x] 1.1 Módulo `EasyPanel.Modules.Ticketing`
  - Novo projeto `src/EasyPanel.Modules.Ticketing` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums `TicketStatus`, `TicketPriority`, `TicketInteractionType`,
    `SlaComplianceStatus`
  - Entidades `TenantEntity`: `Ticket`, `TicketInteraction`, `SlaPolicy`,
    `TicketAttachment`
  - DTOs (`TicketDto`, `TicketInteractionDto`, `SlaPolicyDto`,
    `TicketAttachmentDto`), requests (`CreateTicketRequest`,
    `ChangeTicketStatusRequest`, `AssignTicketRequest`,
    `AddTicketCommentRequest`, `SetSlaPolicyRequest`,
    `UploadTicketAttachmentRequest`), `TicketQuery`, `TicketCursorPage<T>` e
    `ITicketService`/`ISlaPolicyService`/`ITicketAttachmentService`
    (interfaces)
  - `TicketingErrors` (padrão de `InventoryErrors`/`SupplyErrors`)
  - _Requirements: R1.1, R2.1, R2.5, R4.1_
- [x] 1.2 Abstração `IFileStorage`
  - Interface `IFileStorage` (Upload/Download/Delete por `key`) em
    `EasyPanel.Shared.Kernel`, sem dependência de SDK de storage
  - _Requirements: R5.1_
- [x] 1.3 Catálogo e mapeamento de permissões
  - `chamado.view`, `chamado.manage`, `sla.manage` em `Permissions.All`
  - Mapear em `RolePermissions`: `Supervisor` → todas; `Tecnico` →
    `chamado.view`+`chamado.manage`; `Administrador` → todas; `Operacional` →
    `chamado.view`; `Financeiro`/`Estoque` → nenhuma
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R6.1, R6.2_

- [x] 2. Persistência
- [x] 2.1 EF configurations e `AppDbContext`
  - `TicketConfiguration`, `TicketInteractionConfiguration`,
    `SlaPolicyConfiguration`, `TicketAttachmentConfiguration` em
    `Infrastructure/Persistence/Configurations/` (índices conforme
    `design.md`)
  - `DbSet<T>` de cada entidade em `AppDbContext`
  - `StorageOptions` (`Infrastructure/Health/`) estendida com `AccessKey`,
    `SecretKey`, `Bucket` (config já existe em `docker-compose.yml`/`.env`,
    hoje não vinculada); `StorageHealthCheck` continua funcionando sem
    alteração de comportamento
  - _Requirements: R6.6_
- [x] 2.2 Migração `AddTicketing`
  - `dotnet ef migrations add AddTicketing` cobrindo as 4 novas tabelas e
    índices
  - Confirmar `has-pending-model-changes` limpo; build + testes de integração
    (SQLite `EnsureCreated`) continuam verdes
  - _Requirements: R6.6_

- [x] 3. Storage e serviços de domínio
- [x] 3.1 `MinioFileStorage`
  - Novo pacote `Minio` (SDK oficial, S3-compatible) referenciado em
    `EasyPanel.Infrastructure`, versão pinada e verificada com
    `dotnet list package --vulnerable --include-transitive`
  - `MinioFileStorage : IFileStorage` sobre `StorageOptions`
    (Endpoint/AccessKey/SecretKey/Bucket); credenciais nunca logadas
  - Testes de integração contra um bucket real requerem MinIO em execução —
    se indisponível no ambiente de teste, cobrir com um fake `IFileStorage`
    nos testes de serviço (3.3/3.4) e documentar a lacuna de cobertura de
    integração real no fechamento da fase (6.1)
  - _Requirements: R5.1, R5.3_
- [x] 3.2 `TicketService`
  - `CreateAsync` (calcula prazos de SLA a partir da `SlaPolicy` vigente ou
    do padrão de plataforma), `GetAsync`, `ListAsync` (filtros de R3.2),
    `ChangeStatusAsync` (máquina de estados do `design.md`, marca
    `ResolvedAt`/`ResolutionCompliance`), `AssignAsync`, `AddCommentAsync`
    (marca `FirstResponseAt`/`FirstResponseCompliance` na 1ª resposta de um
    Técnico), `ListInteractionsAsync` (cursor), `ListSlaBreachedAsync`
  - Auditado: `ticket.create`, `ticket.status_change`, `ticket.assign`,
    `ticket.comment`
  - Testes de integração: abertura calcula prazos corretamente; transições
    de status válidas/inválidas (400 nas inválidas); atribuição registra
    Interação; comentário registra Interação e marca 1ª resposta; resolução
    marca `ResolvedAt`/cumprimento; consulta de SLA violado (violado e ainda
    vencendo); cursor pagination do histórico de interações; isolamento
    cross-tenant → 404/vazio
  - _Requirements: R1.1–R1.5, R2.1–R2.7, R3.1–R3.3, R4.3–R4.6, R6.3–R6.5_
- [x] 3.3 `SlaPolicyService`
  - `ListAsync`, `SetAsync` (upsert por `Priority`, auditado
    `slapolicy.set`)
  - Testes de integração: upsert cria/atualiza; valores fora do permitido →
    400; isolamento cross-tenant
  - _Requirements: R4.1, R4.2, R4.7, R6.3–R6.5_
- [x] 3.4 `TicketAttachmentService`
  - `UploadAsync` (valida tamanho/tipo antes de chamar `IFileStorage`, grava
    no storage e só então persiste `TicketAttachment`), `DownloadAsync`,
    `ListAsync`
  - Auditado: `ticket.attachment_upload`
  - Testes de integração (com fake `IFileStorage`, ver 3.1): upload
    válido; upload acima do tamanho máximo → 400 sem persistir; tipo fora da
    allowlist → 400; download restrito ao tenant do chamado; listagem
    restrita ao chamado/tenant
  - _Requirements: R5.1–R5.4, R6.3–R6.5_

- [x] 4. Endpoints da API
- [x] 4.1 `TicketsController`
  - `GET`/`POST` sob `api/v1/tickets`, `GET {id}`, `POST {id}/status`,
    `POST {id}/assign`, `POST {id}/comments`, `GET {id}/interactions`
    (cursor), `GET sla-breached`
  - `[RequirePermission]` (`chamado.view`/`chamado.manage`)
  - _Requirements: R1.1–R1.5, R2.1–R2.7, R3.1–R3.3, R4.6, R6.4, R6.5_
- [x] 4.2 `TicketAttachmentsController`
  - `POST api/v1/tickets/{id}/attachments` (multipart, `chamado.manage`)
  - `GET api/v1/tickets/{id}/attachments` (`chamado.view`)
  - `GET api/v1/tickets/{id}/attachments/{attachmentId}` (download,
    `chamado.view`)
  - _Requirements: R5.1–R5.4, R6.4, R6.5_
- [x] 4.3 `SlaPoliciesController`
  - `GET`/`POST` sob `api/v1/sla-policies` (`chamado.view`/`sla.manage`)
  - _Requirements: R4.1, R4.2, R4.7, R6.4, R6.5_

- [x] 5. Auditoria e segurança consolidadas
- [x] 5.1 Revisão de auditoria e testes de segurança
  - Confirmado que criação/mudança de status/atribuição/comentário/upload de
    anexo e upsert de política de SLA auditam corretamente via `IAuditLogger`
    em `TicketService`/`SlaPolicyService`/`TicketAttachmentService`
  - `TicketingIsolationTests` (10 casos) em `EasyPanel.SecurityTests`:
    cross-tenant em chamados/interações/anexos/políticas de SLA → 404 ou
    lista vazia; RBAC nega `chamado.manage` a Operacional (só `chamado.view`)
    e `sla.manage` a Técnico (só `chamado.view`+`chamado.manage`); testes
    positivos confirmam que Operacional lista e Técnico cria chamados. A
    `MultiTenantIsolationWebApplicationFactory` (compartilhada) ganhou um
    `IFileStorage` fake em memória (este ambiente não tem MinIO real — ver
    Task 3.1) e um usuário `Operacional` semeado, sem afetar as suítes de
    isolamento de fases anteriores
  - _Requirements: R6.2, R6.3, R6.5_

- [x] 6. Documentação e fechamento de fase
- [x] 6.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo `Modules.Ticketing`, primeira
    implementação funcional de storage), `docs/DATABASE.md` (novas
    tabelas/índices), `docs/API.md` (novos endpoints), `docs/SECURITY.md`
    (novas permissões, isolamento de anexos por caminho no bucket),
    `README.md`, `HANDOFF.md` (Fase 6 concluída, próxima fase, lacuna de
    cobertura de integração real do MinIO registrada)
  - _Requirements: cobertura documental_
- [x] 6.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln`: 0 avisos, 148 unit + 35
    security + 290 integration — 100% verde
  - `EasyPanel.WindowsClient.slnx` (Agente Windows, não tocado nesta fase):
    build confirmado com 0 avisos, baseline inalterado
  - `dotnet ef migrations has-pending-model-changes`: limpo
  - _Requirements: não-funcionais herdados_

## Notes

- SLA por Contrato, notificação proativa de violação, portal do cliente,
  horário comercial no cálculo de SLA permanecem fora de escopo (ver
  `requirements.md`/`design.md`).
- `EasyPanel.Modules.Ticketing` não referencia `Modules.Customers`/
  `Modules.Monitoring`/`Modules.Identity`; toda validação cruzada de
  `CustomerId`/`LocationId`/`PrinterId`/`AssignedToUserId` acontece em
  `EasyPanel.Infrastructure.Ticketing`.
- Se o ambiente de execução dos testes não tiver um MinIO acessível, a
  tarefa 3.1 registra essa limitação explicitamente em vez de mascará-la —
  a cobertura funcional dos serviços de anexo (3.4) usa um fake
  `IFileStorage`, e a integração real com o MinIO é validada manualmente ou
  via um teste explicitamente marcado/ignorável quando o serviço não está
  disponível.
