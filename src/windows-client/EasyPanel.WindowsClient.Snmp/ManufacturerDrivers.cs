namespace EasyPanel.WindowsClient.Snmp;

/// <summary>
/// Drivers por fabricante (R8.3). Cada um detecta seu equipamento por pistas na
/// sonda e, quando aplicável, adiciona OIDs de contadores mono/cor específicos.
/// Os OIDs específicos abaixo refletem faixas conhecidas por fabricante; o núcleo
/// (<see cref="ManufacturerDriverBase"/>) provê a normalização comum. Novos
/// fabricantes entram como novas classes, sem alterar o núcleo (R8.4).
/// </summary>
internal static class ManufacturerOids
{
    // Contadores mono/cor comuns em Printer-MIB de várias marcas (prtMarkerLifeCount
    // por subunidade). Mantidos como constantes nomeadas para clareza.
    public const string MonoCounter = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    public const string ColorCounter = "1.3.6.1.2.1.43.10.2.1.4.1.2";
}

/// <summary>Driver HP (R8.3).</summary>
public sealed class HpDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "HP";

    protected override IReadOnlyList<string> Hints => ["HP", "Hewlett", "Hewlett-Packard"];

    protected override IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids =>
    [
        (ManufacturerOids.MonoCounter, CounterKind.BlackAndWhite, null),
        (ManufacturerOids.ColorCounter, CounterKind.Color, null),
    ];
}

/// <summary>Driver Canon (R8.3).</summary>
public sealed class CanonDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Canon";

    protected override IReadOnlyList<string> Hints => ["Canon"];

    protected override IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids =>
    [
        (ManufacturerOids.MonoCounter, CounterKind.BlackAndWhite, null),
        (ManufacturerOids.ColorCounter, CounterKind.Color, null),
    ];
}

/// <summary>Driver Epson (R8.3).</summary>
public sealed class EpsonDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Epson";

    protected override IReadOnlyList<string> Hints => ["Epson", "Seiko Epson"];
}

/// <summary>Driver Brother (R8.3).</summary>
public sealed class BrotherDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Brother";

    protected override IReadOnlyList<string> Hints => ["Brother"];
}

/// <summary>Driver Kyocera (R8.3).</summary>
public sealed class KyoceraDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Kyocera";

    protected override IReadOnlyList<string> Hints => ["Kyocera", "KYOCERA"];

    protected override IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids =>
    [
        (ManufacturerOids.MonoCounter, CounterKind.BlackAndWhite, null),
        (ManufacturerOids.ColorCounter, CounterKind.Color, null),
    ];
}

/// <summary>Driver Xerox (R8.3).</summary>
public sealed class XeroxDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Xerox";

    protected override IReadOnlyList<string> Hints => ["Xerox"];

    protected override IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids =>
    [
        (ManufacturerOids.MonoCounter, CounterKind.BlackAndWhite, null),
        (ManufacturerOids.ColorCounter, CounterKind.Color, null),
    ];
}

/// <summary>Driver Ricoh (R8.3).</summary>
public sealed class RicohDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Ricoh";

    protected override IReadOnlyList<string> Hints => ["Ricoh", "RICOH", "NRG", "Lanier", "Savin"];

    protected override IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids =>
    [
        (ManufacturerOids.MonoCounter, CounterKind.BlackAndWhite, null),
        (ManufacturerOids.ColorCounter, CounterKind.Color, null),
    ];
}

/// <summary>Driver Konica Minolta (R8.3).</summary>
public sealed class KonicaMinoltaDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Konica Minolta";

    protected override IReadOnlyList<string> Hints => ["Konica", "Minolta", "KONICA MINOLTA", "bizhub"];

    protected override IReadOnlyList<(string Oid, CounterKind Kind, string? Label)> ExtraCounterOids =>
    [
        (ManufacturerOids.MonoCounter, CounterKind.BlackAndWhite, null),
        (ManufacturerOids.ColorCounter, CounterKind.Color, null),
    ];
}

/// <summary>Driver Lexmark (R8.3).</summary>
public sealed class LexmarkDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Lexmark";

    protected override IReadOnlyList<string> Hints => ["Lexmark"];
}

/// <summary>
/// Driver genérico de fallback (R8.4): aceita qualquer dispositivo e interpreta
/// apenas os OIDs padrão da Printer-MIB. Garante coleta mínima de equipamentos de
/// fabricantes ainda sem driver dedicado. Deve ser avaliado por último na seleção.
/// </summary>
public sealed class GenericPrinterDriver : ManufacturerDriverBase
{
    public override string Manufacturer => "Genérico";

    protected override IReadOnlyList<string> Hints => [];

    /// <summary>Aceita qualquer dispositivo como último recurso.</summary>
    public override bool CanHandle(DeviceProbe probe) => true;
}
