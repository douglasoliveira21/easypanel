namespace EasyPanel.WindowsClient.Snmp;

/// <summary>
/// Abstração de driver por fabricante (R8.2/R8.3): interpreta OIDs/dados SNMP em
/// atributos normalizados de impressora. As especificidades de cada fabricante
/// ficam restritas às implementações; o núcleo de coleta permanece agnóstico e
/// extensível (R8.4). Novos fabricantes entram como novas implementações.
/// </summary>
public interface IPrinterDriver
{
    /// <summary>Nome do fabricante atendido por este driver.</summary>
    string Manufacturer { get; }

    /// <summary>Indica se este driver sabe interpretar o dispositivo sondado (R8.2).</summary>
    bool CanHandle(DeviceProbe probe);

    /// <summary>Interpreta o resultado SNMP em uma leitura normalizada (R8.5).</summary>
    DeviceReading Interpret(SnmpResult snmp);
}

/// <summary>OIDs padrão (RFC 1213 / Printer-MIB) usados como base pelos drivers.</summary>
public static class StandardOids
{
    /// <summary>sysDescr.0 — descrição do sistema.</summary>
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";

    /// <summary>sysObjectID.0 — identificador do objeto do sistema.</summary>
    public const string SysObjectId = "1.3.6.1.2.1.1.2.0";

    /// <summary>prtMarkerLifeCount — contador de vida do marcador (total de páginas).</summary>
    public const string MarkerLifeCount = "1.3.6.1.2.1.43.10.2.1.4.1.1";

    /// <summary>prtGeneralSerialNumber — número de série.</summary>
    public const string SerialNumber = "1.3.6.1.2.1.43.5.1.1.17.1";

    /// <summary>hrDeviceStatus — status do dispositivo.</summary>
    public const string DeviceStatus = "1.3.6.1.2.1.25.3.2.1.5.1";

    /// <summary>
    /// prtMarkerSuppliesDescription — prefixo (Fase 4): concatenar com o índice da
    /// unidade de suprimento (1..<see cref="MaxSupplyUnits"/>) para o OID completo.
    /// </summary>
    public const string SupplyDescriptionPrefix = "1.3.6.1.2.1.43.11.1.1.6.1.";

    /// <summary>
    /// prtMarkerSuppliesLevel — prefixo (Fase 4): nível corrente da unidade de
    /// suprimento (concatenar com o índice).
    /// </summary>
    public const string SupplyLevelPrefix = "1.3.6.1.2.1.43.11.1.1.9.1.";

    /// <summary>
    /// prtMarkerSuppliesMaxCapacity — prefixo (Fase 4): capacidade máxima da
    /// unidade de suprimento (concatenar com o índice) — nível/capacidade = percentual.
    /// </summary>
    public const string SupplyMaxCapacityPrefix = "1.3.6.1.2.1.43.11.1.1.8.1.";

    /// <summary>
    /// Número de unidades de suprimento sondadas por GET fixo (Fase 4). O
    /// <see cref="ISnmpCollector"/> desta fase não suporta SNMP walk/GETNEXT — a
    /// Printer-MIB (RFC 3805) indexa as unidades a partir de 1; a maioria dos
    /// equipamentos reporta poucas unidades (toner(s) + cilindro), então um
    /// intervalo fixo cobre o caso comum sem exigir walk.
    /// </summary>
    public const int MaxSupplyUnits = 6;
}
