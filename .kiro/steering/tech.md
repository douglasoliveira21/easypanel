# Tecnologia e Convenções — EasyPanel

## Stack
- .NET 10 / ASP.NET Core Web API, EF Core, PostgreSQL (Npgsql).
- Redis (cache/processamento), MinIO/S3 (storage), Caddy (proxy), Docker Compose.
- Identity + JWT (HMAC-SHA256, acesso ≤ 15 min) + refresh opaco rotativo (hash SHA-256).
- Serilog (JSON), OpenTelemetry (OTLP). Arquitetura: monólito modular.

## Build e teste (Windows; PowerShell usa `;` como separador, não `&&`)
```powershell
dotnet build EasyPanel.sln
dotnet test  EasyPanel.sln
dotnet build src\windows-client\EasyPanel.WindowsClient.slnx
dotnet test  src\windows-client\EasyPanel.WindowsClient.slnx
dotnet ef migrations add <Nome> --project src\EasyPanel.Infrastructure --startup-project src\EasyPanel.Infrastructure --output-dir Persistence\Migrations
dotnet ef migrations has-pending-model-changes --project src\EasyPanel.Infrastructure --startup-project src\EasyPanel.Infrastructure
```

## Regra de dependência
`Api → Modules.* → Shared.Kernel`. `Infrastructure` implementa as abstrações e é
referenciada pela Api só para DI. Nenhum módulo de negócio referencia outro por
implementação.

## Padrões obrigatórios (reusar os existentes)
- Entidades de negócio herdam de `TenantEntity` → isolamento automático por `TenantId`
  (query filter global + interceptor de `SaveChanges`). Acesso cross-tenant → **404**.
- **Result pattern:** serviços retornam `Result`/`Result<T>` com `Error`; a API mapeia
  por `ErrorType` (Validation→400, NotFound→404, Conflict→409, Forbidden→403).
- **Paginação:** `PagedResult`/`PageRequest` (PageSize ≤ 100); grandes volumes → cursor
  pagination (ver `CounterService`, `PrinterCounter.TimestampTicks`).
- **DTOs distintos das entidades** (nunca expor hash/stamps).
- **Controllers:** `[RequirePermission(Permissions.X)]` + `MapFailure(error)` switch por
  `ErrorType` (ver `CustomersController`/`PrintersController`).
- **Permissões novas:** adicionar em `Permissions.All` e mapear em `RolePermissions`
  (há teste que valida consistência catálogo↔mapa).
- **Auditoria:** eventos sensíveis via `IAuditLogger.LogAsync(AuditEntry)` com redaction.
- **EF:** uma `IEntityTypeConfiguration<>` por entidade em `Persistence/Configurations/`,
  DbSet no `AppDbContext`, migração dedicada por mudança de modelo.
- **Workers cross-tenant:** usar contexto de sistema Super Admin
  (`ISystemDbContextFactory`/`SystemTenantContext`) — ver `HeartbeatMonitor`,
  `CollectionProcessingWorker`.

## Armadilha SQLite (testes)
O provider SQLite dos testes **não** traduz `ORDER BY` nem comparação sobre
`DateTimeOffset`. Ordene/compare por colunas portáveis (string, `long` de *ticks*) ou em
memória. Ver `HeartbeatMonitor` e `PrinterCounter.TimestampTicks`.

## Testes
- Integração: `AppDbContext` sobre SQLite in-memory; usar `ITenantContext` mutável
  (SetSuperAdmin para semear, SetTenant para exercitar). Ver `PrinterServiceTests`,
  `CounterServiceTests`, `Phase2IsolationTests`.
- CNPJs válidos: `11.222.333/0001-81`, `04.252.011/0001-10`, `34.028.316/0001-03`,
  `45.723.174/0001-10`.
