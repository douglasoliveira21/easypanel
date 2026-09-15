namespace EasyPanel.Api.Controllers.Client;

/// <summary>Requisição de autenticação do agente (R4.1).</summary>
public sealed record ClientAuthRequest(string ClientId, string ClientSecret);

/// <summary>Requisição de renovação do token do agente (R4.4).</summary>
public sealed record ClientRefreshRequest(string RefreshToken);

/// <summary>Par de tokens retornado ao agente (R4.1/R4.4).</summary>
public sealed record ClientTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

/// <summary>Requisição de registro de agente (R3.2).</summary>
public sealed record ClientRegisterRequest(
    string ProvisioningKey,
    string UniqueId,
    string Hostname,
    string AgentVersion);

/// <summary>Resposta de registro: identidade e credenciais próprias emitidas (R3.4).</summary>
public sealed record ClientRegisterResponse(
    Guid ClientId,
    string ClientSecret,
    ClientTokenResponse Tokens);
