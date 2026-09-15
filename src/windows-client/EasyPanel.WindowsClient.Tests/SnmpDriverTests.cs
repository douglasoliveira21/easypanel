using EasyPanel.WindowsClient.Snmp;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes de seleção de driver por sonda e de normalização de leitura (Task 8.3 —
/// R8.2/R8.5): o driver correto é escolhido pela pista do fabricante; equipamentos
/// desconhecidos caem no driver genérico; a interpretação normaliza série/status/
/// contadores.
/// </summary>
public sealed class SnmpDriverTests
{
    private readonly PrinterDriverSelector _selector = new();

    [Theory]
    [InlineData("HP LaserJet Pro M404", "HP")]
    [InlineData("Canon iR-ADV C5535", "Canon")]
    [InlineData("Brother HL-L2350DW", "Brother")]
    [InlineData("KYOCERA ECOSYS M5521", "Kyocera")]
    [InlineData("Xerox VersaLink C405", "Xerox")]
    [InlineData("RICOH MP C3004", "Ricoh")]
    [InlineData("KONICA MINOLTA bizhub C258", "Konica Minolta")]
    [InlineData("Lexmark MX421", "Lexmark")]
    [InlineData("EPSON WF-C5790", "Epson")]
    public void Select_ChoosesDriverByHint(string sysDescr, string expectedManufacturer)
    {
        var probe = new DeviceProbe(sysDescr, SysObjectId: null, ManufacturerHint: null);

        var driver = _selector.Select(probe);

        Assert.Equal(expectedManufacturer, driver.Manufacturer);
    }

    [Fact]
    public void Select_UnknownDevice_FallsBackToGeneric()
    {
        var probe = new DeviceProbe("Marca Desconhecida XYZ", null, null);

        var driver = _selector.Select(probe);

        Assert.Equal("Genérico", driver.Manufacturer);
    }

    [Fact]
    public void Interpret_NormalizesSerialStatusAndCounters()
    {
        var driver = new HpDriver();
        var snmp = new SnmpResult(new Dictionary<string, string>
        {
            [StandardOids.SysDescr] = "HP LaserJet",
            [StandardOids.SerialNumber] = "SN-12345",
            [StandardOids.DeviceStatus] = "2", // running/online
            ["1.3.6.1.2.1.43.10.2.1.4.1.1"] = "15000", // mono
            ["1.3.6.1.2.1.43.10.2.1.4.1.2"] = "4200",  // color
        });

        var reading = driver.Interpret(snmp);

        Assert.Equal("HP", reading.Manufacturer);
        Assert.Equal("SN-12345", reading.SerialNumber);
        Assert.Equal(PrinterStatusKind.Online, reading.Status);
        Assert.Contains(reading.Counters, c => c.Kind == CounterKind.BlackAndWhite && c.Value == 15000);
        Assert.Contains(reading.Counters, c => c.Kind == CounterKind.Color && c.Value == 4200);
    }

    [Fact]
    public void Interpret_MissingCounters_ProducesNoCounterEntries()
    {
        var driver = new EpsonDriver();
        var snmp = new SnmpResult(new Dictionary<string, string>
        {
            [StandardOids.SysDescr] = "EPSON WF",
            [StandardOids.DeviceStatus] = "5", // down/offline
        });

        var reading = driver.Interpret(snmp);

        Assert.Equal(PrinterStatusKind.Offline, reading.Status);
        Assert.Empty(reading.Counters);
    }

    // ---- Suprimentos (Fase 4 — Printer-MIB padrão, R8.5) --------------------

    [Fact]
    public void Interpret_ReadsSupplyLevel_ByLevelOverMaxCapacity()
    {
        var driver = new HpDriver();
        var snmp = new SnmpResult(new Dictionary<string, string>
        {
            [StandardOids.SysDescr] = "HP LaserJet",
            [StandardOids.SupplyDescriptionPrefix + "1"] = "Black Toner",
            [StandardOids.SupplyLevelPrefix + "1"] = "25",
            [StandardOids.SupplyMaxCapacityPrefix + "1"] = "100",
        });

        var reading = driver.Interpret(snmp);

        Assert.Equal(25, reading.SupplyLevels["Black Toner"]);
    }

    [Fact]
    public void Interpret_MultipleSupplyUnits_AreAllRead()
    {
        var driver = new HpDriver();
        var snmp = new SnmpResult(new Dictionary<string, string>
        {
            [StandardOids.SupplyDescriptionPrefix + "1"] = "Black Toner",
            [StandardOids.SupplyLevelPrefix + "1"] = "10",
            [StandardOids.SupplyMaxCapacityPrefix + "1"] = "100",
            [StandardOids.SupplyDescriptionPrefix + "2"] = "Drum",
            [StandardOids.SupplyLevelPrefix + "2"] = "50",
            [StandardOids.SupplyMaxCapacityPrefix + "2"] = "200",
        });

        var reading = driver.Interpret(snmp);

        Assert.Equal(10, reading.SupplyLevels["Black Toner"]);
        Assert.Equal(25, reading.SupplyLevels["Drum"]); // 50/200 = 25%
    }

    [Fact]
    public void Interpret_NegativeLevel_IsIgnored_NotZero()
    {
        // Printer-MIB: -2 = desconhecido, -3 = "algum restante sem valor numérico".
        var driver = new HpDriver();
        var snmp = new SnmpResult(new Dictionary<string, string>
        {
            [StandardOids.SupplyDescriptionPrefix + "1"] = "Black Toner",
            [StandardOids.SupplyLevelPrefix + "1"] = "-2",
            [StandardOids.SupplyMaxCapacityPrefix + "1"] = "100",
        });

        var reading = driver.Interpret(snmp);

        Assert.Empty(reading.SupplyLevels);
    }

    [Fact]
    public void Interpret_MissingSupplyOids_ProducesNoSupplyEntries()
    {
        var driver = new HpDriver();
        var snmp = new SnmpResult(new Dictionary<string, string> { [StandardOids.SysDescr] = "HP LaserJet" });

        var reading = driver.Interpret(snmp);

        Assert.Empty(reading.SupplyLevels);
    }

    [Fact]
    public void Interpret_SupplyWithoutDescription_UsesGenericLabel()
    {
        var driver = new HpDriver();
        var snmp = new SnmpResult(new Dictionary<string, string>
        {
            [StandardOids.SupplyLevelPrefix + "1"] = "30",
            [StandardOids.SupplyMaxCapacityPrefix + "1"] = "100",
        });

        var reading = driver.Interpret(snmp);

        Assert.Equal(30, reading.SupplyLevels["supply-1"]);
    }
}
