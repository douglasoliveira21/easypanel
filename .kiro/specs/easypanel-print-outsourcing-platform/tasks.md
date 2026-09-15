# Implementation Plan — EasyPanel (FASE 1)

## Overview

Este plano implementa a Fase 1 (fundação) do EasyPanel de forma incremental e testável. Cada tarefa produz código compilável e é validada por testes antes de avançar. A ordem respeita as dependências: infraestrutura e kernel → tenancy → identity/auth → autorização → auditoria → módulos de negócio → API/observabilidade → fechamento. Todos os requisitos referenciados vivem em `requirements.md` e o design em `design.md`.

## Task Dependency Graph

As "waves" agrupam tarefas que podem ser executadas em paralelo, respeitando as dependências entre elas.

```json
{
  "waves": [
    {
      "wave": 1,
      "tasks": ["1.1"],
      "description": "Base da solução e projetos"
    },
    {
      "wave": 2,
      "tasks": ["1.2", "1.3", "1.4"],
      "description": "Kernel, EF/PostgreSQL e infraestrutura de containers",
      "dependsOn": ["1.1"]
    },
    {
      "wave": 3,
      "tasks": ["1.5", "2.1"],
      "description": "Health checks e entidade Tenant",
      "dependsOn": ["1.3", "1.4"]
    },
    {
      "wave": 4,
      "tasks": ["2.2", "2.3"],
      "description": "Resolução de tenant e isolamento no ORM",
      "dependsOn": ["2.1"]
    },
    {
      "wave": 5,
      "tasks": ["3.1", "3.2"],
      "description": "Identity e serviço de tokens",
      "dependsOn": ["2.3"]
    },
    {
      "wave": 6,
      "tasks": ["3.3", "3.4", "3.5"],
      "description": "Fluxos de autenticação, MFA-ready e senha",
      "dependsOn": ["3.1", "3.2"]
    },
    {
      "wave": 7,
      "tasks": ["4.1", "4.2"],
      "description": "RBAC: catálogo e avaliação de permissões",
      "dependsOn": ["3.3"]
    },
    {
      "wave": 8,
      "tasks": ["5.1", "5.2"],
      "description": "Auditoria e integração automática",
      "dependsOn": ["2.3", "4.2"]
    },
    {
      "wave": 9,
      "tasks": ["6.1", "6.2", "7.1", "7.2"],
      "description": "Módulos de Usuários e Clientes",
      "dependsOn": ["4.2", "5.2"]
    },
    {
      "wave": 10,
      "tasks": ["8.1", "8.2"],
      "description": "Módulo de Locais",
      "dependsOn": ["7.1"]
    },
    {
      "wave": 11,
      "tasks": ["9.1", "9.2", "9.3"],
      "description": "Transversais de API: erros, rate limiting, observabilidade",
      "dependsOn": ["1.5", "3.3"]
    },
    {
      "wave": 12,
      "tasks": ["10.1", "10.2", "10.3"],
      "description": "Integração final, seed, testes de segurança e documentação",
      "dependsOn": ["6.2", "7.2", "8.2", "9.3"]
    }
  ]
}
```

## Tasks

- [x] 1. Configurar solução, kernel compartilhado e infraestrutura base
- [x] 1.1 Criar a solução .NET 10 e a estrutura de projetos
  - Criar `EasyPanel.sln` com os projetos `EasyPanel.Api`, `EasyPanel.Modules.Identity`, `EasyPanel.Modules.Tenancy`, `EasyPanel.Modules.Customers`, `EasyPanel.Modules.Auditing`, `EasyPanel.Shared.Kernel`, `EasyPanel.Infrastructure`
  - Configurar `Directory.Build.props` com TargetFramework net10.0, nullable enabled, treat warnings as errors
  - Configurar a regra de dependência entre projetos (Api → Modules → Kernel; Infrastructure implementa abstrações)
  - _Requirements: R1_

- [x] 1.2 Implementar tipos base do Shared.Kernel
  - Criar `BaseEntity`, `TenantEntity`, `Result`/`Result<T>`, exceções (`NotFoundException`, `ConflictException`, `ForbiddenException`, `ValidationException`, `CrossTenantAccessException`)
  - Criar `PagedResult<T>` e `PageRequest` com limite de PageSize ≤ 100
  - Escrever unit tests para `Result` e clamping de `PageRequest`
  - _Requirements: R12.1, R12.4_

- [x] 1.3 Configurar EF Core + PostgreSQL no Infrastructure
  - Adicionar `AppDbContext` com Npgsql, convenções (Guid PK, DateTimeOffset UTC, enums como int)
  - Configurar connection string via `IConfiguration`/variáveis de ambiente
  - Criar migração inicial vazia e o `MigrationHostedService` com advisory lock do PostgreSQL
  - _Requirements: R1.4, R1.6_

- [x] 1.4 Configurar Docker Compose e reverse proxy
  - Criar `deploy/docker-compose.yml` com serviços api, postgres, redis, minio, caddy, otel-collector, prometheus, loki, grafana, frontend
  - Criar overrides `development`, `staging`, `production` e `.env.example`
  - Criar `Caddyfile` roteando `/api/*` para o backend
  - _Requirements: R1.1, R1.2, R1.7_

- [x] 1.5 Implementar health checks e verificação de dependências
  - Adicionar `HealthController` com `/health/live` e `/health/ready`
  - Registrar health checks para PostgreSQL, Redis e MinIO/S3; reportar não saudável se dependência obrigatória falhar
  - Escrever integration test verificando `ready` degradado quando dependência indisponível
  - _Requirements: R1.3, R1.5_

- [x] 2. Implementar módulo de Tenancy e isolamento no ORM
- [x] 2.1 Criar entidade Tenant e ITenantContext
  - Criar entidade `Tenant` (Id, Name, Slug, IsActive, CreatedAt) e sua configuração EF + migração
  - Implementar `ITenantContext` e `TenantContext` scoped (TenantId, IsSuperAdmin, HasTenant)
  - _Requirements: R6.1, R6.2_

- [x] 2.2 Implementar resolução de tenant no pipeline
  - Criar `TenantResolutionMiddleware` que lê o claim `tenant_id` do ClaimsPrincipal (não do corpo/query)
  - Suportar resolução via endpoint admin designado para Super Admin (header explícito)
  - Escrever unit tests para resolução do tenant a partir de claims
  - _Requirements: R6.3, R6.7_

- [x] 2.3 Aplicar filtros globais e validação de escrita por tenant
  - Aplicar `HasQueryFilter` por TenantId a toda `TenantEntity` no `OnModelCreating`
  - Criar interceptor de `SaveChanges` que preenche TenantId em Added e rejeita Modified/Deleted com TenantId divergente (→ CrossTenantAccessException/404)
  - Escrever integration tests do filtro em list/get/update/delete
  - _Requirements: R6.4, R6.5, R6.6_

- [x] 3. Implementar autenticação (ASP.NET Identity + tokens)
- [x] 3.1 Configurar ASP.NET Identity com ApplicationUser
  - Criar `ApplicationUser : IdentityUser<Guid>` (TenantId nullable, IsActive, MfaEnabled, CreatedAt) e stores sobre PostgreSQL
  - Configurar `PasswordOptions` (mín. 8, maiúscula, minúscula, dígito, especial) e lockout (5 falhas / 15 min)
  - Criar migração do schema Identity; índice único `(TenantId, NormalizedEmail)`
  - _Requirements: R2.8, R3.7, R4.3_

- [x] 3.2 Implementar TokenService (JWT + refresh token)
  - Criar entidade `RefreshToken` (TokenHash, UserId, ExpiresAt, RevokedAt, ReplacedByTokenHash) + migração e índices
  - Implementar emissão de JWT (≤15 min, claims sub/tenant_id/roles/perm_version/jti) e refresh token opaco hasheado com rotação
  - Implementar `ValidateAndRotateAsync` e `RevokeAllForUserAsync`
  - Escrever unit tests de rotação e revogação
  - _Requirements: R2.1, R2.4, R2.5, R2.6_

- [x] 3.3 Implementar AuthService (login/logout/refresh)
  - Implementar login: valida credenciais, verifica IsActive e lockout, emite tokens; credenciais inválidas → mensagem genérica
  - Implementar logout (invalida refresh token da sessão) e refresh (rotaciona)
  - Integrar lockout do Identity: 5 falhas consecutivas → bloqueio 15 min; sucesso zera contador
  - Escrever integration tests: login sucesso/falha, lockout, refresh válido/inválido → 401
  - _Requirements: R2.1, R2.2, R2.3, R2.5, R2.7, R4.2, R4.3, R4.4, R4.5, R7.5_

- [x] 3.4 Implementar fluxo MFA-ready (estrutural)
  - Adicionar ramificação no login para `MfaEnabled`: retornar `MfaRequired` + `mfa_ticket` de curta duração e endpoint de verificação de segundo fator
  - Garantir que usuários sem MFA seguem o fluxo inalterado
  - Escrever unit test do branch de decisão MFA
  - _Requirements: R2.9, R2.10_

- [x] 3.5 Implementar recuperação e alteração de senha
  - Implementar forgot-password (token uso único hasheado, ≤60 min; resposta idêntica para email existente/inexistente)
  - Implementar reset-password e change-password (valida senha atual); ambos revogam todos os refresh tokens do usuário
  - Escrever integration tests: reset válido, resposta não reveladora, revogação de sessões
  - _Requirements: R3.1, R3.2, R3.3, R3.4, R3.5, R3.6, R3.7_

- [x] 4. Implementar autorização RBAC
- [x] 4.1 Definir catálogo de permissões e papéis (seed)
  - Criar constantes `Permissions` (customer.*, location.*, user.manage, audit.view, etc.)
  - Criar seed dos 8 papéis (Super Admin, Administrador, Financeiro, Operacional, Estoque, Técnico, Supervisor, Cliente) e mapeamento role→permissões
  - _Requirements: R5.1, R5.2_

- [x] 4.2 Implementar avaliação de permissões
  - Criar `RequirePermissionAttribute`, `IAuthorizationPolicyProvider` dinâmico (`perm:<permission>`) e `PermissionAuthorizationHandler`
  - Resolver permissões efetivas por role com cache no Redis (reflete mudança de papel na próxima avaliação)
  - Retornar 403 quando faltar permissão; garantir avaliação sempre no backend
  - Escrever unit/security tests: concede/nega por papel; endpoint sem permissão → 403
  - _Requirements: R5.3, R5.4, R5.5, R5.6_

- [x] 5. Implementar módulo de Auditoria
- [x] 5.1 Criar entidade AuditLog e IAuditLogger
  - Criar `AuditLog` (ActorUserId, Action, ResourceType, ResourceId, OldValues, NewValues, Ip, UserAgent, Result, OccurredAt, TenantId) + migração e índices `(TenantId, OccurredAt DESC)`
  - Implementar `IAuditLogger`/`AuditLogger` (append-only; nenhum update/delete exposto)
  - _Requirements: R10.1, R10.3, R10.4_

- [x] 5.2 Integrar auditoria automática de ações sensíveis
  - Registrar eventos de login (sucesso/falha) e tentativas de acesso cross-tenant recusadas
  - Interceptor captura mudanças em usuários/papéis gravando old/new com redaction
  - Expor `GET /api/v1/audit-logs` paginado e filtrado por tenant (permissão audit.view)
  - Escrever integration tests: login gera log; acesso cross-tenant gera log; consulta paginada por tenant
  - _Requirements: R4.1, R6.9, R7.8, R10.2, R10.5_

- [x] 6. Implementar módulo de Usuários
- [x] 6.1 Implementar UserService (CRUD + ativação)
  - Implementar Create (vincula tenant do contexto; email duplicado no tenant → 409), Update (dados/papéis)
  - Implementar Deactivate (marca inativo + revoga refresh tokens) e Reactivate
  - Implementar List paginado filtrado por tenant
  - Escrever unit tests de regras de negócio
  - _Requirements: R7.1, R7.2, R7.3, R7.4, R7.6, R7.7_

- [x] 6.2 Implementar UsersController e DTOs
  - Criar DTOs (`CreateUserRequest`, `UpdateUserRequest`, `UserDto`) + validadores FluentValidation
  - Criar endpoints `/api/v1/users` (GET/POST/PUT, deactivate, reactivate) com `RequirePermission(user.manage)`
  - Garantir auditoria em todas as mutações
  - Escrever integration tests dos endpoints
  - _Requirements: R7.1, R7.3, R7.4, R7.6, R7.7, R7.8, R12.1, R12.2_

- [x] 7. Implementar módulo de Clientes
- [x] 7.1 Criar entidade Customer, validação e serviço
  - Criar `Customer : TenantEntity` com todos os campos e enum `CustomerStatus` (Ativo/Inativo/Bloqueado) + migração e índices (`UNIQUE (TenantId, Cnpj)`, `(TenantId, Status)`, `(TenantId, RazaoSocial)`)
  - Implementar validador de CNPJ (dígitos verificadores) e normalização (somente dígitos)
  - Implementar `CustomerService` (create com unicidade → 409, update, get, list paginado/ordenado, change status)
  - Escrever unit tests de validação de CNPJ e regras de status
  - _Requirements: R8.1, R8.2, R8.3, R8.4, R8.5, R8.6, R8.7, R8.9, R12.5_

- [x] 7.2 Implementar CustomersController e DTOs
  - Criar DTOs + validadores; endpoints GET(list/by-id)/POST/PUT/PATCH status com permissões customer.*
  - Garantir filtro por tenant em get/list e paginação limitada
  - Escrever integration tests: CRUD, CNPJ inválido → 400, CNPJ duplicado → 409, paginação
  - _Requirements: R8.1, R8.4, R8.5, R8.7, R8.8, R8.9, R12.2, R12.4_

- [x] 8. Implementar módulo de Locais
- [x] 8.1 Criar entidade Location e serviço
  - Criar `Location : TenantEntity` (CustomerId FK, campos, enum `LocationStatus`) + migração e índices (`(TenantId, CustomerId)`, `(TenantId, Status)`)
  - Implementar `LocationService`: create valida Customer do mesmo tenant (senão erro de validação), update, get, list por cliente paginado, change status
  - Escrever unit tests de vínculo Customer→Location
  - _Requirements: R9.1, R9.2, R9.3, R9.4, R9.5, R9.6, R9.8, R12.5_

- [x] 8.2 Implementar LocationsController e DTOs
  - Criar DTOs + validadores; endpoints POST/GET(by-id)/PUT/PATCH status e `GET /api/v1/customers/{customerId}/locations`
  - Garantir filtro por tenant e paginação
  - Escrever integration tests: criação com Customer de outro tenant → validação/404; listagem por cliente
  - _Requirements: R9.1, R9.4, R9.6, R9.7, R9.8, R12.2, R12.4_

- [x] 9. Implementar transversais de API: middlewares, rate limiting e erros
- [x] 9.1 Implementar tratamento de erros e correlação
  - Criar `ExceptionHandlingMiddleware` mapeando exceções para ProblemDetails (400/401/403/404/409/429/500) sem vazar dados
  - Criar `CorrelationIdMiddleware` (header `X-Correlation-Id`)
  - Escrever integration tests dos mapeamentos de status
  - _Requirements: R11.4, R12.2_

- [x] 9.2 Implementar rate limiting distribuído
  - Configurar rate limiting nativo do .NET com store no Redis, políticas particionadas por usuário, IP, tenant e endpoint
  - Retornar 429 com `Retry-After` ao exceder
  - Escrever integration test de limite excedido → 429
  - _Requirements: R4.6, R4.7_

- [x] 9.3 Configurar observabilidade e Swagger
  - Configurar Serilog com sink JSON, enrichers de CorrelationId e tenant_id, e redaction de senhas/tokens/segredos
  - Configurar OpenTelemetry (traces/metrics) para o collector
  - Configurar Swagger/OpenAPI com esquema Bearer
  - Escrever test verificando ausência de segredos nos logs
  - _Requirements: R11.1, R11.2, R11.3_

- [x] 10. Integração final, seed e testes de segurança
- [x] 10.1 Compor DI, pipeline e seed de bootstrap
  - Registrar todos os módulos no `Program.cs` na ordem correta de middlewares (correlation → exception → rate limit → auth → tenant → authz)
  - Seed de papéis/permissões e (opcional) tenant + Super Admin inicial para onboarding — sem dados de negócio fictícios
  - _Requirements: R5.1, R5.2, R6.1_

- [x] 10.2 Escrever suíte de testes de isolamento multi-tenant
  - Testes de segurança: usuário do Tenant A não lê/edita/exclui recursos do Tenant B (→404) em Customer, Location, User; tentativa gera AuditLog
  - Testes de unicidade por tenant (CNPJ e email)
  - _Requirements: R6.5, R6.8, R6.9, R7.2, R8.5_

- [x] 10.3 Validar build, testes e documentação
  - Rodar build e a suíte completa (unit + integration + security) com Testcontainers; corrigir falhas
  - Escrever `README.md`, `ARCHITECTURE.md`, `DATABASE.md`, `API.md`, `SECURITY.md`, `DEPLOYMENT.md` da Fase 1
  - _Requirements: R1, R6.8_

## Notes

- Cada tarefa mantém o projeto compilável e é validada por testes antes de avançar (unit + integration + security). Nenhuma fase avança se a atual estiver quebrada.
- Testes de integração usam `WebApplicationFactory` + Testcontainers (PostgreSQL/Redis reais).
- Itens estruturais preparados mas não exercitados funcionalmente na Fase 1: MFA (TOTP), SignalR, Hangfire, uso funcional de MinIO. Provisionados na infra, sinalizados no design.
- Sem dados fake no produto: seeds limitam-se a papéis, permissões e bootstrap opcional de tenant/Super Admin.
- Tarefas puramente de infraestrutura/documentação (1.4, 10.3) envolvem configuração e escrita e podem exigir execução manual de containers pelo desenvolvedor.
