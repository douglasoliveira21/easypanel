using EasyPanel.Api.Controllers.Alerting;
using EasyPanel.Api.Controllers.Customers;
using EasyPanel.Api.Controllers.Locations;
using EasyPanel.Api.Controllers.Users;
using EasyPanel.Api.Middleware;
using EasyPanel.Api.Observability;
using EasyPanel.Api.RateLimiting;
using EasyPanel.Infrastructure.Alerting;
using EasyPanel.Infrastructure.Billing;
using EasyPanel.Infrastructure.Contracts;
using EasyPanel.Infrastructure.Health;
using EasyPanel.Infrastructure.Inventory;
using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Portal;
using EasyPanel.Infrastructure.Reporting;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Infrastructure.Security.Authorization;
using EasyPanel.Infrastructure.Ticketing;
using EasyPanel.Modules.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// Observabilidade — logs (R11.1–R11.3): Serilog como provedor de logs do host,
// com formato JSON compacto (compatível com Loki), enriquecimento por escopo
// (CorrelationId/tenant_id) e redação de segredos (R11.2).
builder.Host.UsePlatformSerilog();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Documentação da API (R45): Swagger/OpenAPI com esquema de segurança Bearer.
builder.Services.AddPlatformSwagger();

// Observabilidade — telemetria (R11): OpenTelemetry (traces + métricas) com
// instrumentação de ASP.NET Core/HttpClient e exportação OTLP para o collector.
builder.Services.AddPlatformOpenTelemetry();

// Persistência (EF Core + PostgreSQL): registra o AppDbContext, vincula as
// DatabaseOptions a partir da configuração e agenda a aplicação de migrações
// pendentes na inicialização via MigrationHostedService (R1.4, R1.6).
builder.Services.AddPersistence(builder.Configuration);

// ASP.NET Core Identity (núcleo) com stores sobre o AppDbContext (PostgreSQL):
// registra ApplicationUser/ApplicationRole e aplica as políticas de senha (R3.7)
// e de bloqueio por tentativas (R4.3/R4.4). O middleware de autenticação e a
// emissão de tokens são introduzidos nas tarefas 3.2+.
builder.Services.AddPlatformIdentity();

// Serviço de tokens (JWT de acesso curto + refresh token opaco rotativo):
// vincula e valida as JwtOptions e registra o ITokenService (R2.1, R2.4–R2.6).
builder.Services.AddTokenService(builder.Configuration);

// Esquema de autenticação Bearer (JWT): valida os tokens de acesso emitidos e
// recusa tokens expirados/inválidos com HTTP 401 (R2.7). Fornece também os
// serviços de autenticação exigidos por UseAuthentication e pela resolução de
// tenant a partir do ClaimsPrincipal.
builder.Services.AddJwtAuthentication(builder.Configuration);

// Fluxos de autenticação de sessão (login/refresh/logout) sobre o Identity e o
// serviço de tokens (R2.1–R2.5, R4.2–R4.5, R7.5).
builder.Services.AddAuthService();

// Autenticação própria do agente Windows (Fase 2 — R4): esquema ClientBearer com
// audience dedicado, IClientContext scoped e IClientTokenService. Resolve
// tenant/local sempre da identidade do agente, nunca da requisição.
builder.Services.AddClientAuthentication();

// Serviços de monitoramento (Fase 2): opções, TimeProvider e o worker
// HeartbeatMonitor que detecta agentes sem heartbeat e gera eventos (R6.4).
builder.Services.AddMonitoring(builder.Configuration);

// Serviços de domínio da Fase 3 (Alertas e Notificações): regras, alertas e
// silenciamentos. O motor de avaliação e o despachante de notificação são
// registrados separadamente quando implementados.
builder.Services.AddAlertingEndpoint();
builder.Services.AddAlertingEngine(builder.Configuration);

// Serviços de domínio da Fase 5 (Estoque): itens, movimentações, saldo e mínimo.
builder.Services.AddInventoryServices();

// Serviços de domínio da Fase 6 (Chamados/Helpdesk e SLA): chamados, política de
// SLA e anexos (primeiro uso funcional do storage MinIO, via IFileStorage).
builder.Services.AddTicketingServices(builder.Configuration);

// Serviços de domínio da Fase 7 (Contratos): contrato, escopo (Local/
// Impressora) e franquia por tipo de contador — base para a Fase 8.
builder.Services.AddContractServices();

// Serviços de domínio da Fase 8 (Fechamento e Faturamento): fechamento
// mensal (consolida consumo, resolve contrato, calcula excedente) e
// consulta/ciclo de vida de faturas.
builder.Services.AddBillingServices();

// Serviços de domínio da Fase 9 (Relatórios e Dashboards): painel de visão
// geral e relatórios agregados (consumo, faturamento, SLA), sem cache.
builder.Services.AddReportingServices();
builder.Services.AddPortalServices();

// Trilha de auditoria somente-adição (R10): registra o IAuditLogger e a fábrica
// de contexto dedicada para escritas independentes. Consumido pela integração
// automática de eventos sensíveis (tarefa 5.2).
builder.Services.AddAuditing();

// Autorização por permissão (RBAC — R5.3–R5.6): resolvedor de permissões
// efetivas por papel (em memória, a partir do catálogo em código), handler do
// PermissionRequirement e policy provider dinâmico que materializa políticas
// perm:<permission> sob demanda. A permissão exigida por um endpoint é sempre
// avaliada no backend a partir dos papéis do token, nunca do frontend.
builder.Services.AddPermissionAuthorization();

// Health checks das dependências obrigatórias (PostgreSQL, Redis, S3/MinIO)
// expostos pelo HealthController em /health/ready; /health/live é liveness
// simples (R1.3, R1.5).
builder.Services.AddDependencyHealthChecks(builder.Configuration);

// Contexto de tenant (scoped) resolvido por requisição a partir do token
// autenticado; consumido pelos filtros de isolamento multi-tenant (R6.1–R6.3).
builder.Services.AddTenancy();

// Endpoints de gestão de usuários (R7): registra o IUserService, o acessor do
// usuário atuante (enriquece a auditoria com o ator — R7.8) e os validadores de
// contrato dos DTOs de entrada (R12.2).
builder.Services.AddUsersEndpoint();

// Endpoints de cadastro de clientes (R8): registra o ICustomerService e os
// validadores de contrato dos DTOs de entrada (R12.2).
builder.Services.AddCustomersEndpoint();

// Endpoints de cadastro de locais (R9): registra o ILocationService e os
// validadores de contrato dos DTOs de entrada (R12.2).
builder.Services.AddLocationsEndpoint();

// Rate limiting (R4.6, R4.7): limiter global particionado por usuário/tenant/IP
// e por endpoint; ao exceder, responde 429 com Retry-After.
builder.Services.AddPlatformRateLimiting(builder.Configuration);

// Bootstrap de inicialização (10.1): após as migrações, semeia os papéis fixos
// (R5.1) e, opcionalmente, o Super Admin inicial para onboarding (R68) — sem
// dados fictícios. Ignorado quando as migrações na inicialização estão desligadas
// (ex.: hosts de teste).
builder.Services.AddBootstrap(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Documentação da API (R45): Swagger JSON + UI. Exposto em desenvolvimento; em
// produção o acesso deve ser controlado pelo proxy reverso/autorização.
app.UseSwagger();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}

// Identificador de correlação por requisição (R11.3): resolve/gera o
// X-Correlation-Id, ecoa na resposta e abre um escopo de log. Executa primeiro,
// para que até falhas não tratadas sejam correlacionadas.
app.UseMiddleware<CorrelationIdMiddleware>();

// Tratamento centralizado de erros (R11.4, R12.2): traduz exceções de domínio e
// não tratadas em ProblemDetails (400/403/404/409/500) sem vazar detalhes.
// Envolve todo o restante do pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

// Autenticação (JWT bearer) antes da autorização e da resolução de tenant, de
// modo que o ClaimsPrincipal esteja disponível para os middlewares seguintes
// (R2.7, R6.3). Endpoints anônimos (health, login, refresh) permanecem públicos.
app.UseAuthentication();

// Rate limiting após a autenticação, de modo que a partição possa usar o usuário
// e o tenant do token quando presentes; requisições anônimas particionam por IP
// (R4.6). Ao exceder, o middleware responde 429 com Retry-After (R4.7).
app.UseRateLimiter();

app.UseAuthorization();

// Resolução do tenant a partir do ClaimsPrincipal autenticado. Executa após a
// autorização/autenticação e é no-op para requisições anônimas (ex.: health
// checks), preservando o comportamento dos endpoints públicos (R6.3, R6.7).
app.UseTenantResolution();

app.MapControllers();

app.Run();

// Expõe a classe Program para os testes de integração (WebApplicationFactory).
public partial class Program;
