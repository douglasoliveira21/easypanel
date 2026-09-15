using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IClientAuthService"/> (R4.1, R4.4, R4.5).
///
/// Autentica o agente por <c>client_id</c> (o Id do <see cref="WindowsClient"/>) e
/// segredo, comparando o hash persistido em tempo constante
/// (<see cref="ClientSecretHasher"/>). Em sucesso, emite um par de tokens com o
/// tenant/local do próprio agente embutidos (<see cref="IClientTokenService"/>).
/// Credenciais inválidas/desconhecidas e agentes desabilitados produzem falha
/// <see cref="MonitoringErrors.Unauthorized"/>, traduzida em HTTP 401 pela API.
///
/// <para>As consultas usam <c>IgnoreQueryFilters</c> porque a autenticação ocorre
/// antes de haver um tenant resolvido no contexto; a identidade é estabelecida
/// pela posse do segredo, e o tenant é recuperado do próprio registro.</para>
/// </summary>
public sealed class ClientAuthService : IClientAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IClientTokenService _tokenService;

    public ClientAuthService(AppDbContext dbContext, IClientTokenService tokenService)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
    }

    /// <inheritdoc />
    public async Task<Result<ClientTokenPair>> AuthenticateAsync(
        string clientId,
        string clientSecret,
        CancellationToken ct)
    {
        if (!Guid.TryParse(clientId, out var id) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Result.Failure<ClientTokenPair>(MonitoringErrors.Unauthorized);
        }

        var client = await _dbContext.Set<WindowsClient>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

        // Desconhecido, desabilitado ou segredo divergente → 401 sem distinção
        // (não-vazamento de existência).
        if (client is null
            || client.State == WindowsClientState.Disabled
            || !ClientSecretHasher.Verify(clientSecret, client.SecretHash))
        {
            return Result.Failure<ClientTokenPair>(MonitoringErrors.Unauthorized);
        }

        var pair = await _tokenService
            .IssueAsync(client.Id, client.TenantId, client.LocationId, ct)
            .ConfigureAwait(false);

        return Result.Success(pair);
    }

    /// <inheritdoc />
    public async Task<Result<ClientTokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var pair = await _tokenService.RefreshAsync(refreshToken, ct).ConfigureAwait(false);

        return pair is null
            ? Result.Failure<ClientTokenPair>(MonitoringErrors.Unauthorized)
            : Result.Success(pair);
    }
}
