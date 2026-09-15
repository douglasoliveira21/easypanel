namespace EasyPanel.WindowsClient.Snmp;

/// <summary>
/// Coletor SNMP (R8.6): consulta um conjunto de OIDs de um dispositivo em SNMP
/// v1/v2c. A interface é agnóstica de versão para permitir a adição de v3 (R8.6)
/// sem alterar os chamadores. A implementação concreta (sobre biblioteca SNMP)
/// pertence ao host; o núcleo depende apenas desta abstração, mantendo os testes
/// determinísticos.
/// </summary>
public interface ISnmpCollector
{
    /// <summary>
    /// Sonda o dispositivo em <paramref name="host"/> para identificar fabricante/modelo
    /// (sysDescr/sysObjectID), sem coletar contadores. Usado na descoberta (R7).
    /// </summary>
    Task<DeviceProbe> ProbeAsync(string host, SnmpCredentials credentials, CancellationToken ct);

    /// <summary>Consulta os <paramref name="oids"/> do dispositivo e devolve o bruto.</summary>
    Task<SnmpResult> QueryAsync(
        string host,
        SnmpCredentials credentials,
        IReadOnlyList<string> oids,
        CancellationToken ct);
}

/// <summary>
/// Credenciais SNMP (R8.7). Para v1/v2c, a <see cref="Community"/> é o segredo; ela
/// é sensível e nunca deve ser registrada em log (redação no host). Campos de v3
/// (usuário/auth/priv) serão adicionados quando v3 for implementado.
/// </summary>
public sealed record SnmpCredentials(SnmpVersion Version, string Community)
{
    /// <summary>Credencial v2c padrão de leitura (<c>public</c>), para descoberta inicial.</summary>
    public static readonly SnmpCredentials DefaultV2c = new(SnmpVersion.V2c, "public");
}
