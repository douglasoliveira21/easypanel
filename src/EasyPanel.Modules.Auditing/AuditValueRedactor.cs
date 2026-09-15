using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Utilitário de serialização com redaction para os campos <c>OldValues</c>/
/// <c>NewValues</c> do <see cref="AuditEntry"/> (R10.1, R11.2).
///
/// <para>
/// A auditoria de mutações sensíveis (ex.: alterações em usuários/papéis — R7.8)
/// grava um retrato JSON do estado anterior e do novo. Segredos e credenciais
/// <b>nunca</b> podem ser persistidos nessa trilha (R11.2): hashes de senha,
/// <c>SecurityStamp</c>, <c>ConcurrencyStamp</c> e quaisquer campos de token são
/// removidos <b>antes</b> da serialização. A remoção é aplicada no pipeline de
/// metadados do <see cref="System.Text.Json"/> (um resolvedor que descarta as
/// propriedades sensíveis), de modo que os valores redigidos jamais são
/// materializados na string resultante — em vez de serem serializados e depois
/// mascarados.
/// </para>
///
/// <para>
/// A correspondência de nomes é <b>case-insensitive</b> e cobre tanto nomes
/// exatos (ex.: <c>PasswordHash</c>, <c>SecurityStamp</c>, <c>ConcurrencyStamp</c>)
/// quanto o padrão de "contém <c>token</c>" (ex.: <c>RefreshToken</c>,
/// <c>ResetToken</c>, <c>AccessToken</c>), além de <c>password</c>/<c>secret</c>.
/// </para>
/// </summary>
public static class AuditValueRedactor
{
    /// <summary>
    /// Nomes de propriedade redigidos por correspondência exata (case-insensitive).
    /// Cobrem os campos sensíveis do ASP.NET Core Identity (R11.2).
    /// </summary>
    private static readonly HashSet<string> ExactRedactedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "SecurityStamp",
        "ConcurrencyStamp",
    };

    /// <summary>
    /// Subcadeias que, se presentes no nome da propriedade (case-insensitive),
    /// disparam a redaction. Cobrem qualquer variação de token/segredo/senha.
    /// </summary>
    private static readonly string[] RedactedSubstrings =
    {
        "token",
        "password",
        "secret",
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { RemoveSensitiveProperties },
        },
    };

    /// <summary>
    /// Serializa <paramref name="value"/> em JSON, omitindo qualquer propriedade
    /// sensível (R11.2). Retorna <c>null</c> quando <paramref name="value"/> é
    /// <c>null</c>, para que o campo do <see cref="AuditEntry"/> permaneça vazio.
    /// </summary>
    /// <typeparam name="T">Tipo do objeto a serializar.</typeparam>
    /// <param name="value">Estado a capturar (anterior ou novo), ou <c>null</c>.</param>
    /// <returns>JSON redigido, ou <c>null</c> se a entrada for <c>null</c>.</returns>
    public static string? Serialize<T>(T? value)
        where T : class
    {
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(value, value.GetType(), Options);
    }

    /// <summary>
    /// Indica se um nome de propriedade é considerado sensível e, portanto, deve
    /// ser omitido da trilha de auditoria (R11.2). Exposto para permitir que os
    /// chamadores validem/derivem a mesma política de redaction.
    /// </summary>
    public static bool IsSensitive(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        if (ExactRedactedNames.Contains(propertyName))
        {
            return true;
        }

        foreach (var fragment in RedactedSubstrings)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveSensitiveProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var property = typeInfo.Properties[i];
            if (IsSensitive(property.Name))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}
