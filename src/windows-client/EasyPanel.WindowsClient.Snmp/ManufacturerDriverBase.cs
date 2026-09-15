using System.Globalization;

namespace EasyPanel.WindowsClient.Snmp;

/// <summary>
/// Base compartilhada dos drivers por fabricante (R8.3/R8.4). Implementa a
/// normalização comum baseada na Printer-MIB padrão (série, contador de vida,
/// status) e a detecção por pista de fabricante no <c>sysDescr</c>/<c>sysObjectID</c>.
/// Drivers concretos sobrescrevem OIDs específicos ou o mapeamento de contadores
/// coloridos/mono do fabricante, mantendo o núcleo agnóstico.
/// </summary>
public abstract class ManufacturerDriverBase : IPrinterDriver
{
    /// <inheritdoc />
    public abstract string Manufacturer { get; }

    /// <summary>Pistas (case-insensitive) que identificam o fabricante na sonda.</summary>
    protected abstract IReadOnlyList<string> Hints { get; }

    /// <inheritdoc />
    public virtual bool CanHandle(DeviceProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var haystack = string.Join(
            ' ',
            probe.ManufacturerHint,
            probe.SysDescr,
            probe.SysObjectId);

        return Hints.Any(h => haystack.Contains(h, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public virtual DeviceReading Interpret(SnmpResult snmp)
    {
        ArgumentNullException.ThrowIfNull(snmp);

        var counters = new List<CounterReading>();
        if (TryReadLong(snmp, TotalCounterOid, out var total))
        {
            counters.Add(new CounterReading(CounterKind.Total, null, total));
        }

        foreach (var (oid, kind, label) in ExtraCounterOids)
        {
            if (TryReadLong(snmp, oid, out var value))
            {
                counters.Add(new CounterReading(kind, label, value));
            }
        }

        return new DeviceReading
        {
            Manufacturer = Manufacturer,
            Model = snmp.Values.GetValueOrDefault(ModelOid),
            SerialNumber = snmp.Values.GetValueOrDefault(StandardOids.SerialNumber),
            Status = MapStatus(snmp.Values.GetValueOrDefault(StandardOids.DeviceStatus)),
            Counters = counters,
            SupplyLevels = ReadSupplyLevels(snmp),
        };
    }

    /// <summary>
    /// Lê os níveis de suprimento pela Printer-MIB padrão (RFC 3805 — Fase 4):
    /// para cada índice de 1 a <see cref="StandardOids.MaxSupplyUnits"/>, calcula o
    /// percentual como <c>nível/capacidade_máxima</c> quando ambos os OIDs
    /// respondem com valores positivos (níveis negativos da MIB — ex.: -2
    /// "desconhecido", -3 "algum restante sem valor numérico" — são ignorados, não
    /// interpretados como zero). O rótulo vem da descrição do fabricante quando
    /// disponível, ou de um rótulo genérico por índice. Compartilhado por todos os
    /// drivers: não requer especificidade de fabricante (R8.3/R8.4).
    /// </summary>
    private static IReadOnlyDictionary<string, int> ReadSupplyLevels(SnmpResult snmp)
    {
        var supplies = new Dictionary<string, int>();

        for (var i = 1; i <= StandardOids.MaxSupplyUnits; i++)
        {
            if (!TryReadLong(snmp, StandardOids.SupplyLevelPrefix + i, out var level)
                || !TryReadLong(snmp, StandardOids.SupplyMaxCapacityPrefix + i, out var maxCapacity))
            {
                continue;
            }

            if (level < 0 || maxCapacity <= 0)
            {
                // Valores especiais da Printer-MIB (ex.: -2 desconhecido, -3 sem
                // valor numérico) não são um nível válido — pula esta unidade.
                continue;
            }

            var percent = (int)Math.Clamp(level * 100L / maxCapacity, 0, 100);
            var label = snmp.Values.GetValueOrDefault(StandardOids.SupplyDescriptionPrefix + i);
            supplies[string.IsNullOrWhiteSpace(label) ? $"supply-{i}" : label] = percent;
        }

        return supplies;
    }

    /// <summary>OID do contador total (padrão Printer-MIB; sobrescrevível).</summary>
    protected virtual string TotalCounterOid => StandardOids.MarkerLifeCount;

    /// <summary>OID do modelo (padrão sysDescr; sobrescrevível).</summary>
    protected virtual string ModelOid => StandardOids.SysDescr;

    /// <summary>Contadores adicionais específicos do fabricante (mono/cor/etc.).</summary>
    protected virtual IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids => [];

    /// <summary>Mapeia o valor de hrDeviceStatus para o status normalizado.</summary>
    protected static PrinterStatusKind MapStatus(string? raw) => raw switch
    {
        // hrDeviceStatus: 2=running(up), 3=warning, 4=testing, 5=down.
        "2" => PrinterStatusKind.Online,
        "3" => PrinterStatusKind.Online,
        "5" => PrinterStatusKind.Offline,
        null or "" => PrinterStatusKind.Unknown,
        _ => PrinterStatusKind.Unknown,
    };

    /// <summary>Lê um OID como <see cref="long"/>, tolerando ausência/valor inválido.</summary>
    protected static bool TryReadLong(SnmpResult snmp, string oid, out long value)
    {
        value = 0;
        return snmp.Values.TryGetValue(oid, out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
