namespace EasyPanel.WindowsClient.Snmp;

/// <summary>
/// Seleciona o driver adequado a um dispositivo sondado (R8.2). Avalia os drivers
/// dedicados na ordem de registro e recorre ao <see cref="GenericPrinterDriver"/>
/// como último recurso, garantindo que todo equipamento tenha ao menos coleta
/// padrão. O núcleo permanece agnóstico: adicionar um fabricante é registrar um
/// novo driver, sem alterar o seletor (R8.4).
/// </summary>
public sealed class PrinterDriverSelector
{
    private readonly IReadOnlyList<IPrinterDriver> _drivers;
    private readonly IPrinterDriver _fallback;

    /// <summary>
    /// Cria o seletor com o conjunto padrão de drivers de fabricante e o driver
    /// genérico de fallback avaliado por último.
    /// </summary>
    public PrinterDriverSelector()
        : this(DefaultDrivers(), new GenericPrinterDriver())
    {
    }

    /// <summary>Cria o seletor com drivers e fallback explícitos (para testes).</summary>
    public PrinterDriverSelector(IReadOnlyList<IPrinterDriver> drivers, IPrinterDriver fallback)
    {
        _drivers = drivers;
        _fallback = fallback;
    }

    /// <summary>Retorna o primeiro driver dedicado que reconhece a sonda, ou o genérico.</summary>
    public IPrinterDriver Select(DeviceProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        return _drivers.FirstOrDefault(d => d.CanHandle(probe)) ?? _fallback;
    }

    /// <summary>Conjunto padrão de drivers de fabricante suportados na Fase 2.</summary>
    public static IReadOnlyList<IPrinterDriver> DefaultDrivers() =>
    [
        new HpDriver(),
        new CanonDriver(),
        new EpsonDriver(),
        new BrotherDriver(),
        new KyoceraDriver(),
        new XeroxDriver(),
        new RicohDriver(),
        new KonicaMinoltaDriver(),
        new LexmarkDriver(),
    ];
}
