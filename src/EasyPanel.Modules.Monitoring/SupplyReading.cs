using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Leitura de nível de suprimento (toner/cilindro/etc.) de uma <see cref="Printer"/>
/// em um instante (Fase 4). Somente-adição, mesmo padrão de <see cref="PrinterCounter"/>.
/// </summary>
public class SupplyReading : TenantEntity
{
    /// <summary>Impressora à qual esta leitura pertence.</summary>
    public Guid PrinterId { get; set; }

    /// <summary>Rótulo normalizado do suprimento (ex.: <c>toner-preto</c>, <c>cilindro</c>).</summary>
    public required string Label { get; set; }

    /// <summary>Percentual restante (0–100).</summary>
    public int Percent { get; set; }

    /// <summary>Momento da leitura (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Ticks (UTC) de <see cref="Timestamp"/>. Cursor portável (mesmo motivo de <c>PrinterCounter.TimestampTicks</c>).</summary>
    public long TimestampTicks { get; set; }

    /// <summary>Agente que reportou a leitura, quando aplicável.</summary>
    public Guid? WindowsClientId { get; set; }

    /// <summary>Coleta de origem, quando aplicável.</summary>
    public Guid? CollectionId { get; set; }
}
