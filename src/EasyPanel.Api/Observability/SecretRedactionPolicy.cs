using Serilog.Core;
using Serilog.Events;

namespace EasyPanel.Api.Observability;

/// <summary>
/// Política de destructuring do Serilog que redige (mascara) propriedades cujo
/// nome sugira dado sensível — senha, token, segredo, chave, hash, credencial —
/// antes de a entrada de log ser serializada (R11.2). Isso impede que objetos
/// estruturados logados vazem esses valores, mesmo que um chamador os inclua por
/// engano.
///
/// <para>A verificação é por <b>nome de propriedade</b> (case-insensitive,
/// substring), cobrindo variações em pt/en (ex.: <c>password</c>, <c>senha</c>,
/// <c>token</c>, <c>secret</c>, <c>segredo</c>, <c>apikey</c>, <c>passwordHash</c>,
/// <c>securityStamp</c>). O valor é substituído por um marcador fixo, preservando a
/// estrutura do log sem revelar o conteúdo.</para>
/// </summary>
public sealed class SecretRedactionPolicy : IDestructuringPolicy
{
    private const string RedactedMarker = "***REDACTED***";

    private static readonly string[] SensitiveNameFragments =
    [
        "password",
        "senha",
        "token",
        "secret",
        "segredo",
        "credential",
        "credencial",
        "apikey",
        "api_key",
        "securitystamp",
        "hash",
        "signingkey",
        "connectionstring",
    ];

    /// <inheritdoc />
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var type = value.GetType();

        // Aplica apenas a tipos complexos (objetos com propriedades). Tipos
        // primitivos/strings soltos não têm nome de propriedade para inspecionar e
        // são tratados normalmente pelo pipeline padrão.
        if (type.IsPrimitive || value is string || type.IsEnum)
        {
            result = null!;
            return false;
        }

        var properties = type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (properties.Length == 0)
        {
            result = null!;
            return false;
        }

        var logProperties = new List<LogEventProperty>(properties.Length);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (IsSensitive(property.Name))
            {
                logProperties.Add(new LogEventProperty(property.Name, new ScalarValue(RedactedMarker)));
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception)
            {
                // Getter que lança: ignora a propriedade em vez de derrubar o log.
                continue;
            }

            logProperties.Add(new LogEventProperty(
                property.Name,
                propertyValueFactory.CreatePropertyValue(propertyValue, destructureObjects: true)));
        }

        result = new StructureValue(logProperties, type.Name);
        return true;
    }

    private static bool IsSensitive(string propertyName)
    {
        foreach (var fragment in SensitiveNameFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
