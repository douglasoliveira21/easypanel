namespace EasyPanel.WindowsClient.Snmp;

/// <summary>Versão do protocolo SNMP suportada (R8.6). v3 é previsto por extensibilidade.</summary>
public enum SnmpVersion
{
    /// <summary>SNMP v1.</summary>
    V1 = 0,

    /// <summary>SNMP v2c.</summary>
    V2c = 1,

    /// <summary>SNMP v3 (previsto; não implementado na Fase 2).</summary>
    V3 = 2,
}

/// <summary>Tipo de contador normalizado produzido pelos drivers.</summary>
public enum CounterKind
{
    /// <summary>Total de páginas impressas.</summary>
    Total = 0,

    /// <summary>Páginas preto e branco.</summary>
    BlackAndWhite = 1,

    /// <summary>Páginas coloridas.</summary>
    Color = 2,

    /// <summary>Digitalizações.</summary>
    Scan = 3,

    /// <summary>Outro tipo (ver rótulo).</summary>
    Other = 99,
}

/// <summary>Status normalizado da impressora.</summary>
public enum PrinterStatusKind
{
    /// <summary>Online e respondendo.</summary>
    Online = 0,

    /// <summary>Offline.</summary>
    Offline = 1,

    /// <summary>Desconhecido.</summary>
    Unknown = 2,

    /// <summary>Sem comunicação.</summary>
    NoCommunication = 4,
}

/// <summary>
/// Sonda de identificação obtida na descoberta/SNMP, usada pelos drivers para
/// decidir se sabem interpretar o equipamento (R8.2). Não contém credenciais (R8.7).
/// </summary>
public sealed record DeviceProbe(string? SysDescr, string? SysObjectId, string? ManufacturerHint);

/// <summary>Resultado bruto de uma consulta SNMP: OID → valor textual.</summary>
public sealed record SnmpResult(IReadOnlyDictionary<string, string> Values);

/// <summary>Uma leitura de contador normalizada.</summary>
public sealed record CounterReading(CounterKind Kind, string? Label, long Value);

/// <summary>Atributos normalizados de uma impressora produzidos por um driver (R8.5).</summary>
public sealed record DeviceReading
{
    /// <summary>Fabricante normalizado.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Modelo.</summary>
    public string? Model { get; init; }

    /// <summary>Número de série.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Status normalizado.</summary>
    public PrinterStatusKind Status { get; init; } = PrinterStatusKind.Unknown;

    /// <summary>Contadores lidos (R8.5).</summary>
    public IReadOnlyList<CounterReading> Counters { get; init; } = [];

    /// <summary>Níveis de suprimento por rótulo (percentual), quando disponíveis.</summary>
    public IReadOnlyDictionary<string, int> SupplyLevels { get; init; } = new Dictionary<string, int>();
}
