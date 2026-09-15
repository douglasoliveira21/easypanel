using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace EasyPanel.WindowsClient.Snmp;

/// <summary>
/// Implementação real de <see cref="ISnmpCollector"/> sobre SNMP v1/v2c por UDP
/// (Fase 4 — R8.1), via <c>Lextm.SharpSnmpLib</c>. Cada OID é consultado
/// individualmente: um dispositivo real não suporta necessariamente todos os OIDs
/// especulados (ex.: unidades de suprimento inexistentes — <see cref="StandardOids.MaxSupplyUnits"/>),
/// e SNMP v1 falha a PDU inteira quando qualquer OID não existe; consultar um a um
/// isola essa falha ao OID específico, sem exigir SNMP walk/GETNEXT.
/// </summary>
public sealed class SnmpCollector : ISnmpCollector
{
    private const int PortNumber = 161;
    private const int PerOidTimeoutMs = 1500;

    /// <inheritdoc />
    public async Task<DeviceProbe> ProbeAsync(string host, SnmpCredentials credentials, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var sysDescr = await TryGetAsync(host, credentials, StandardOids.SysDescr, ct).ConfigureAwait(false);
        var sysObjectId = await TryGetAsync(host, credentials, StandardOids.SysObjectId, ct).ConfigureAwait(false);

        if (sysDescr is null && sysObjectId is null)
        {
            throw new System.TimeoutException($"Sem resposta SNMP de {host}.");
        }

        return new DeviceProbe(sysDescr, sysObjectId, ManufacturerHint: null);
    }

    /// <inheritdoc />
    public async Task<SnmpResult> QueryAsync(
        string host,
        SnmpCredentials credentials,
        IReadOnlyList<string> oids,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(oids);

        var values = new Dictionary<string, string>();

        foreach (var oid in oids)
        {
            ct.ThrowIfCancellationRequested();

            var value = await TryGetAsync(host, credentials, oid, ct).ConfigureAwait(false);
            if (value is not null)
            {
                values[oid] = value;
            }
        }

        return new SnmpResult(values);
    }

    /// <summary>
    /// Consulta um único OID, tolerando ausência/timeout/erro do dispositivo
    /// (retorna <c>null</c> em vez de lançar) — o chamador decide se a ausência de
    /// um OID específico é significativa.
    /// </summary>
    private static async Task<string?> TryGetAsync(string host, SnmpCredentials credentials, string oid, CancellationToken ct)
    {
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(host), PortNumber);
            var community = new OctetString(credentials.Community);
            var version = credentials.Version == SnmpVersion.V1 ? VersionCode.V1 : VersionCode.V2;
            var variables = new List<Variable> { new(new ObjectIdentifier(oid)) };

            var result = await Task.Run(
                    () => Messenger.Get(version, endpoint, community, variables, PerOidTimeoutMs),
                    ct)
                .ConfigureAwait(false);

            var variable = result.Count > 0 ? result[0] : null;

            // noSuchObject/noSuchInstance/endOfMibView (SNMPv2c) não são um valor —
            // o OID não existe neste dispositivo.
            if (variable is null || variable.Data is NoSuchObject or NoSuchInstance or EndOfMibView)
            {
                return null;
            }

            return variable.Data.ToString();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cancelamento interno do Task.Run (não cancelamento externo): trata como sem resposta.
            return null;
        }
        catch (Lextm.SharpSnmpLib.Messaging.TimeoutException)
        {
            // Timeout de resposta do próprio SharpSnmpLib: OID sem resposta do dispositivo.
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (SnmpException)
        {
            return null;
        }
        catch (FormatException)
        {
            // IPAddress.Parse falhou (host não é um IP literal) — sem fallback de DNS nesta fase.
            return null;
        }
    }
}
