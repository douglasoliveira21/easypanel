using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Contexto do agente autenticado da requisição corrente (R4.2). Resolvido pelo
/// esquema de autenticação do cliente a partir da identidade do agente, nunca de
/// valores da requisição. Scoped por requisição.
/// </summary>
public interface IClientContext
{
    /// <summary>Id do <see cref="WindowsClient"/> autenticado, quando houver.</summary>
    Guid? ClientId { get; }

    /// <summary>Tenant resolvido da identidade do agente.</summary>
    Guid? TenantId { get; }

    /// <summary>Local resolvido da identidade do agente.</summary>
    Guid? LocationId { get; }

    /// <summary>Indica se há um agente autenticado no contexto.</summary>
    bool IsAuthenticated { get; }
}

/// <summary>Par de tokens emitido ao agente (acesso curto + renovação) (R4.1/R4.4).</summary>
public sealed record ClientTokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

/// <summary>Serviço de autenticação própria do agente (R4).</summary>
public interface IClientAuthService
{
    /// <summary>Autentica o agente por client_id + secret e emite tokens (R4.1).</summary>
    Task<Result<ClientTokenPair>> AuthenticateAsync(string clientId, string clientSecret, CancellationToken ct);

    /// <summary>Renova o token de acesso via credencial de renovação (R4.4).</summary>
    Task<Result<ClientTokenPair>> RefreshAsync(string refreshToken, CancellationToken ct);
}

/// <summary>Requisição de registro de agente (R3.2).</summary>
public sealed record RegisterClientRequest(
    string ProvisioningKey,
    string UniqueId,
    string Hostname,
    string AgentVersion);

/// <summary>Resultado do registro: identidade e credenciais próprias emitidas (R3.4).</summary>
public sealed record RegisterClientResult(
    Guid ClientId,
    string ClientSecret,
    ClientTokenPair Tokens);

/// <summary>Serviço de registro de agentes (R3).</summary>
public interface IClientRegistrationService
{
    /// <summary>
    /// Registra um agente a partir de uma credencial de provisionamento válida,
    /// vinculando-o ao Tenant/Local resolvidos e emitindo credenciais próprias (R3.2/R3.4).
    /// </summary>
    Task<Result<RegisterClientResult>> RegisterAsync(RegisterClientRequest request, string? ip, CancellationToken ct);
}

/// <summary>Payload de heartbeat enviado pelo agente (R6.2).</summary>
public sealed record HeartbeatRequest(
    string AgentVersion,
    string Hostname,
    DateTimeOffset Timestamp,
    string State,
    int PrinterCount,
    DateTimeOffset? LastCollectionAt);

/// <summary>Serviço de heartbeat (R6).</summary>
public interface IHeartbeatService
{
    /// <summary>Registra um heartbeat do agente autenticado, atualizando estado/horário (R6.3).</summary>
    Task<Result> RecordAsync(HeartbeatRequest request, CancellationToken ct);
}
