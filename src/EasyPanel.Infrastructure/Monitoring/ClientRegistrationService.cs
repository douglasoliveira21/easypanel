using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IClientRegistrationService"/> (R3.2–R3.7).
///
/// Valida a chave de provisionamento do Local (hash em tempo constante), resolve
/// tenant/cliente/local <b>da própria chave</b> (nunca da requisição), cria o
/// <see cref="WindowsClient"/> com um segredo próprio (armazenado apenas hasheado)
/// e emite credenciais. Permite múltiplos agentes por Local enquanto a chave está
/// ativa (R3.6). O registro é auditado (R3.7); chave inválida/expirada → 401 (R3.5).
///
/// <para>Opera com <see cref="ISystemDbContextFactory"/> (contexto de sistema),
/// pois o registro é anônimo mas grava uma <c>TenantEntity</c> com o
/// <c>TenantId</c> validado a partir da chave.</para>
/// </summary>
public sealed class ClientRegistrationService : IClientRegistrationService
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly ISystemDbContextFactory _contextFactory;
    private readonly IClientTokenService _tokenService;
    private readonly IAuditLogger _auditLogger;

    public ClientRegistrationService(
        ISystemDbContextFactory contextFactory,
        IClientTokenService tokenService,
        IAuditLogger auditLogger)
    {
        _contextFactory = contextFactory;
        _tokenService = tokenService;
        _auditLogger = auditLogger;
    }

    /// <inheritdoc />
    public async Task<Result<RegisterClientResult>> RegisterAsync(
        RegisterClientRequest request,
        string? ip,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.ProvisioningKey)
            || string.IsNullOrWhiteSpace(request.UniqueId))
        {
            return Result.Failure<RegisterClientResult>(MonitoringErrors.Unauthorized);
        }

        var now = Clock.GetUtcNow();
        var keyHash = ClientSecretHasher.Hash(request.ProvisioningKey);

        await using var context = _contextFactory.Create();

        var provisioningKey = await context.Set<LocationProvisioningKey>()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct)
            .ConfigureAwait(false);

        if (provisioningKey is null || !provisioningKey.IsActiveAt(now))
        {
            await AuditFailureAsync(ip, request.UniqueId, ct).ConfigureAwait(false);
            return Result.Failure<RegisterClientResult>(MonitoringErrors.Unauthorized);
        }

        // Reuso idempotente: se o mesmo UniqueId já foi registrado neste tenant,
        // rotaciona o segredo e reemite credenciais em vez de duplicar o agente.
        var existing = await context.Set<WindowsClient>()
            .FirstOrDefaultAsync(
                c => c.TenantId == provisioningKey.TenantId && c.UniqueId == request.UniqueId,
                ct)
            .ConfigureAwait(false);

        var secret = ClientSecretHasher.GenerateSecret();
        WindowsClient client;

        if (existing is not null)
        {
            existing.SecretHash = ClientSecretHasher.Hash(secret);
            existing.Hostname = request.Hostname;
            existing.AgentVersion = request.AgentVersion;
            existing.UpdatedAt = now;
            client = existing;
        }
        else
        {
            client = new WindowsClient
            {
                Id = Guid.NewGuid(),
                TenantId = provisioningKey.TenantId,
                CustomerId = provisioningKey.CustomerId,
                LocationId = provisioningKey.LocationId,
                UniqueId = request.UniqueId,
                Hostname = request.Hostname,
                AgentVersion = request.AgentVersion,
                State = WindowsClientState.Registered,
                SecretHash = ClientSecretHasher.Hash(secret),
                CreatedAt = now,
            };
            context.Set<WindowsClient>().Add(client);
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        var tokens = await _tokenService
            .IssueInAsync(context, client.Id, client.TenantId, client.LocationId, ct)
            .ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                TenantId = client.TenantId,
                Action = "client.register",
                ResourceType = nameof(WindowsClient),
                ResourceId = client.Id.ToString(),
                Ip = ip,
                Result = AuditResult.Success,
                NewValues = $"{{\"uniqueId\":\"{client.UniqueId}\",\"locationId\":\"{client.LocationId}\"}}",
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(new RegisterClientResult(client.Id, secret, tokens));
    }

    private Task AuditFailureAsync(string? ip, string uniqueId, CancellationToken ct) =>
        _auditLogger.LogAsync(
            new AuditEntry
            {
                Action = "client.register",
                ResourceType = nameof(WindowsClient),
                ResourceId = uniqueId,
                Ip = ip,
                Result = AuditResult.Failure,
            },
            ct);
}
