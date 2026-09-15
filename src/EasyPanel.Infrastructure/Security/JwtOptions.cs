using System.ComponentModel.DataAnnotations;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Parâmetros de emissão e assinatura de tokens (R2.1, R2.4, R2.6), vinculados a
/// partir da seção <c>Jwt</c> da configuração (variáveis de ambiente / arquivos
/// por ambiente — segredos nunca versionados, R1.6).
///
/// A chave de assinatura (<see cref="SigningKey"/>) é usada em HMAC-SHA256; deve
/// ter comprimento suficiente (≥ 32 bytes) para a força do algoritmo.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Emissor (claim <c>iss</c>) dos tokens.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Audiência (claim <c>aud</c>) esperada dos tokens.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Chave simétrica de assinatura (HMAC-SHA256). Deve ter ao menos 32 bytes.
    /// Injetada por configuração/segredo; nunca versionada.
    /// </summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Validade do JWT de acesso, em minutos. Limitada a 15 (R2.6); valores
    /// maiores são recusados na validação das opções.
    /// </summary>
    [Range(1, 15)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Validade do refresh token, em dias.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}
