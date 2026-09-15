using System.Text.RegularExpressions;

namespace EasyPanel.WindowsClient;

/// <summary>
/// Redação de segredos em mensagens de log do agente (R5.6/R5.7): nunca registrar
/// credenciais, tokens de acesso/renovação ou community strings SNMP em texto
/// claro. Substitui valores sensíveis por um marcador, preservando a utilidade do
/// log para diagnóstico.
///
/// A redação é defensiva e por padrões: cobre pares chave/valor comuns
/// (<c>secret</c>, <c>token</c>, <c>password</c>, <c>community</c>,
/// <c>authorization</c>) em JSON e em cabeçalhos, além do esquema Bearer.
/// </summary>
public static partial class SecretRedactor
{
    /// <summary>Marcador que substitui valores sensíveis nos logs.</summary>
    public const string Mask = "***REDACTED***";

    /// <summary>
    /// Redige segredos conhecidos em <paramref name="message"/>. Entrada nula/vazia
    /// é devolvida inalterada.
    /// </summary>
    public static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message ?? string.Empty;
        }

        // Bearer primeiro: evita que o padrão chave/valor consuma apenas a palavra
        // "Bearer" e deixe o token JWT seguinte exposto.
        var result = BearerPattern().Replace(message, $"Bearer {Mask}");
        result = KeyValuePattern().Replace(result, m => $"{m.Groups["key"].Value}{m.Groups["sep"].Value}{Mask}");
        return result;
    }

    // Captura pares como: "secret":"x", token = y, community: z (JSON ou texto).
    [GeneratedRegex(
        @"(?<key>(?i:secret|token|refresh_token|access_token|password|senha|community|authorization|client_secret|provisioning_?key))(?<sep>""?\s*[:=]\s*""?)(?<val>[^""&,\s}]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();
}
