namespace EasyPanel.WindowsClient.Snmp;

/// <summary>Candidata a impressora descoberta na rede (R7.3).</summary>
/// <param name="Host">Endereço IP/host da candidata.</param>
/// <param name="Probe">Sonda de identificação obtida.</param>
/// <param name="Driver">Driver selecionado para o dispositivo.</param>
public sealed record PrinterCandidate(string Host, DeviceProbe Probe, string Driver);

/// <summary>
/// Descoberta e coleta de impressoras na rede (R7). Sonda uma lista de alvos
/// (IP/faixa/subnet expandidos pelo host) via SNMP, seleciona o driver adequado a
/// cada candidata (R7.4/R8.2) e coleta as leituras normalizadas, ignorando alvos
/// explicitamente excluídos (R7.6). O núcleo é agnóstico de transporte: consome
/// <see cref="ISnmpCollector"/> e <see cref="PrinterDriverSelector"/>.
/// </summary>
public sealed class PrinterDiscoveryService
{
    private readonly ISnmpCollector _collector;
    private readonly PrinterDriverSelector _selector;

    public PrinterDiscoveryService(ISnmpCollector collector, PrinterDriverSelector selector)
    {
        _collector = collector;
        _selector = selector;
    }

    /// <summary>
    /// Sonda os <paramref name="targets"/> e retorna as candidatas que responderam,
    /// já com o driver selecionado. Alvos em <paramref name="ignored"/> são pulados
    /// (R7.6). Falhas de sonda em um alvo não interrompem os demais.
    /// </summary>
    public async Task<IReadOnlyList<PrinterCandidate>> DiscoverAsync(
        IReadOnlyList<string> targets,
        IReadOnlySet<string> ignored,
        SnmpCredentials credentials,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(ignored);

        var candidates = new List<PrinterCandidate>();

        foreach (var host in targets)
        {
            ct.ThrowIfCancellationRequested();

            if (ignored.Contains(host))
            {
                continue;
            }

            DeviceProbe probe;
            try
            {
                probe = await _collector.ProbeAsync(host, credentials, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Alvo sem resposta/erro de sonda: não é candidata; segue os demais.
                continue;
            }

            // Sem qualquer identificação, considera-se não respondeu como impressora.
            if (probe is { SysDescr: null, SysObjectId: null, ManufacturerHint: null })
            {
                continue;
            }

            var driver = _selector.Select(probe);
            candidates.Add(new PrinterCandidate(host, probe, driver.Manufacturer));
        }

        return candidates;
    }

    /// <summary>
    /// Coleta as leituras normalizadas de uma candidata: consulta os OIDs relevantes
    /// e interpreta com o driver selecionado (R8.5).
    /// </summary>
    public async Task<DeviceReading> CollectAsync(
        PrinterCandidate candidate,
        SnmpCredentials credentials,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var driver = _selector.Select(candidate.Probe);

        var oids = new List<string>
        {
            StandardOids.SysDescr,
            StandardOids.SerialNumber,
            StandardOids.DeviceStatus,
            StandardOids.MarkerLifeCount,
            ManufacturerOidsInternal.MonoCounter,
            ManufacturerOidsInternal.ColorCounter,
        };

        // Níveis de suprimento (Fase 4 — Printer-MIB padrão, R8.5): índices fixos,
        // sem exigir SNMP walk (ver StandardOids.MaxSupplyUnits).
        for (var i = 1; i <= StandardOids.MaxSupplyUnits; i++)
        {
            oids.Add(StandardOids.SupplyDescriptionPrefix + i);
            oids.Add(StandardOids.SupplyLevelPrefix + i);
            oids.Add(StandardOids.SupplyMaxCapacityPrefix + i);
        }

        var snmp = await _collector.QueryAsync(candidate.Host, credentials, oids, ct).ConfigureAwait(false);
        return driver.Interpret(snmp);
    }
}

/// <summary>Espelho interno dos OIDs de contador comuns, acessível à descoberta.</summary>
internal static class ManufacturerOidsInternal
{
    public const string MonoCounter = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    public const string ColorCounter = "1.3.6.1.2.1.43.10.2.1.4.1.2";
}
