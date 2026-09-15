using System.Security.Cryptography;
using System.Text;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Hash e verificação do segredo próprio do agente (R4.5).
///
/// O segredo é um valor opaco de alta entropia (256 bits) gerado no registro; por
/// isso é hasheado com SHA-256 (base64url), à semelhança dos refresh tokens, sem
/// necessidade de um KDF com fator de trabalho (que se destina a senhas de baixa
/// entropia). O valor bruto nunca é persistido; apenas o hash é comparado, em
/// tempo constante.
/// </summary>
public static class ClientSecretHasher
{
    /// <summary>Gera um novo segredo opaco (256 bits, base64url).</summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    /// <summary>Calcula o hash SHA-256 (base64url) de um segredo.</summary>
    public static string Hash(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Base64UrlEncode(hash);
    }

    /// <summary>Verifica um segredo contra o hash persistido, em tempo constante.</summary>
    public static bool Verify(string secret, string expectedHash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(Hash(secret));
        var expected = Encoding.UTF8.GetBytes(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
