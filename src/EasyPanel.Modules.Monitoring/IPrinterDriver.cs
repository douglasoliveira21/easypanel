namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Sonda de identificação de um dispositivo obtida na descoberta/SNMP, usada pelos
/// drivers para decidir se sabem interpretar o equipamento (R8.2). Não contém
/// credenciais SNMP (R8.7).
/// </summary>
/// <param name="SysDescr">Descrição do sistema (OID sysDescr), quando disponível.</param>
/// <param name="SysObjectId">Identificador do objeto do sistema (OID sysObjectID).</param>
/// <param name="ManufacturerHint">Pista de fabricante extraída da sonda, quando houver.</param>
public sealed record SnmpDeviceProbe(string? SysDescr, string? SysObjectId, string? ManufacturerHint);

/// <summary>
/// Resultado bruto de uma consulta SNMP: pares OID → valor coletados de uma
/// impressora. A interpretação (normalização) é responsabilidade do driver.
/// </summary>
/// <param name="Values">Mapa de OID para valor textual coletado.</param>
public sealed record SnmpQueryResult(IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Uma leitura de contador normalizada produzida por um driver.
/// </summary>
/// <param name="CounterType">Tipo do contador.</param>
/// <param name="CounterTypeLabel">Rótulo, quando o tipo é configurável (Other).</param>
/// <param name="Value">Valor do contador.</param>
public sealed record CounterReading(CounterType CounterType, string? CounterTypeLabel, long Value);

/// <summary>
/// Atributos normalizados de uma impressora produzidos por um driver a partir dos
/// dados SNMP (R8.5): identificação, status, contadores e suprimentos disponíveis.
/// </summary>
public sealed record PrinterReading
{
    /// <summary>Fabricante normalizado.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Modelo.</summary>
    public string? Model { get; init; }

    /// <summary>Número de série.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>Hostname.</summary>
    public string? Hostname { get; init; }

    /// <summary>Endereço MAC.</summary>
    public string? Mac { get; init; }

    /// <summary>Uptime reportado, quando disponível.</summary>
    public TimeSpan? Uptime { get; init; }

    /// <summary>Status normalizado.</summary>
    public PrinterStatus Status { get; init; } = PrinterStatus.Unknown;

    /// <summary>Contadores lidos (R8.5).</summary>
    public IReadOnlyList<CounterReading> Counters { get; init; } = [];

    /// <summary>
    /// Níveis de suprimento normalizados (toner/cilindro/unidades) por rótulo,
    /// em percentual quando disponível. Consumidos em fases futuras; a Fase 2
    /// apenas coleta e transporta.
    /// </summary>
    public IReadOnlyDictionary<string, int> SupplyLevels { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Códigos/descrições de erro reportados, quando houver.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Abstração de driver por fabricante (R8.2/R8.3): interpreta OIDs/dados SNMP em
/// atributos normalizados de impressora, mantendo as especificidades de fabricante
/// restritas às implementações — o núcleo de coleta permanece agnóstico e
/// extensível (R8.4). Novos fabricantes entram como novas implementações, sem
/// alterar o núcleo.
/// </summary>
public interface IPrinterDriver
{
    /// <summary>Nome do fabricante que este driver atende.</summary>
    string Manufacturer { get; }

    /// <summary>Indica se este driver sabe interpretar o dispositivo sondado (R8.2).</summary>
    bool CanHandle(SnmpDeviceProbe probe);

    /// <summary>Interpreta o resultado SNMP em uma leitura normalizada (R8.5).</summary>
    PrinterReading Interpret(SnmpQueryResult snmp);
}
