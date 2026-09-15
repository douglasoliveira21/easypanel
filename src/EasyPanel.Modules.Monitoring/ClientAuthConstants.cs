namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Constantes bem conhecidas da autenticação própria do agente Windows (R4).
///
/// Centraliza o nome do esquema de autenticação do cliente, o audience dedicado
/// dos tokens de cliente e os nomes dos claims que carregam a identidade do
/// agente (client/tenant/local), de modo que emissão, validação e resolução de
/// contexto compartilhem uma única fonte de verdade.
/// </summary>
public static class ClientAuthConstants
{
    /// <summary>Nome do esquema de autenticação Bearer do agente.</summary>
    public const string SchemeName = "ClientBearer";

    /// <summary>Audience dedicado dos tokens de cliente (R4.1), distinto do de usuário.</summary>
    public const string Audience = "easypanel:client";

    /// <summary>Claim com o identificador do <see cref="WindowsClient"/> (subject do token).</summary>
    public const string ClientIdClaimType = "client_id";

    /// <summary>Claim com o tenant resolvido da identidade do agente (R4.2).</summary>
    public const string TenantIdClaimType = "tenant_id";

    /// <summary>Claim com o local resolvido da identidade do agente (R4.2).</summary>
    public const string LocationIdClaimType = "location_id";

    /// <summary>Claim que marca o token como de cliente (defesa em profundidade).</summary>
    public const string TokenKindClaimType = "token_kind";

    /// <summary>Valor do claim <see cref="TokenKindClaimType"/> para tokens de cliente.</summary>
    public const string TokenKindValue = "client";
}
