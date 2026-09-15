using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IHeartbeatService"/> (R6.1–R6.3).
///
/// Atualiza <c>LastHeartbeatAt</c>, versão, hostname e (quando informada) a última
/// coleta do agente autenticado, restaurando o estado <see cref="WindowsClientState.Active"/>
/// quando ele retorna de <see cref="WindowsClientState.HeartbeatMissing"/>. O agente
/// é sempre o resolvido do <see cref="IClientContext"/> (identidade do token),
/// nunca do payload (R6.3/R4.2).
/// </summary>
public sealed class HeartbeatService : IHeartbeatService
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly AppDbContext _dbContext;
    private readonly IClientContext _clientContext;

    public HeartbeatService(AppDbContext dbContext, IClientContext clientContext)
    {
        _dbContext = dbContext;
        _clientContext = clientContext;
    }

    /// <inheritdoc />
    public async Task<Result> RecordAsync(HeartbeatRequest request, CancellationToken ct)
    {
        if (!_clientContext.IsAuthenticated || _clientContext.ClientId is not { } clientId)
        {
            return Result.Failure(MonitoringErrors.Unauthorized);
        }

        // Filtro global de tenant garante que só o agente do tenant corrente é
        // alcançado; acesso cross-tenant não encontra a linha (→ 404).
        var client = await _dbContext.Set<WindowsClient>()
            .FirstOrDefaultAsync(c => c.Id == clientId, ct)
            .ConfigureAwait(false);

        if (client is null)
        {
            return Result.Failure(MonitoringErrors.NotFound);
        }

        var now = Clock.GetUtcNow();
        client.LastHeartbeatAt = now;
        client.AgentVersion = request.AgentVersion;
        client.Hostname = request.Hostname;

        if (request.LastCollectionAt is { } lastCollection)
        {
            client.LastCollectionAt = lastCollection;
        }

        // Um agente desabilitado administrativamente não é reativado por heartbeat.
        if (client.State != WindowsClientState.Disabled)
        {
            client.State = WindowsClientState.Active;
        }

        client.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
