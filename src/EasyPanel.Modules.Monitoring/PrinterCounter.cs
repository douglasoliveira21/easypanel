using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Leitura de contador de uma <see cref="Printer"/> em um instante (R11). É uma
/// <see cref="TenantEntity"/> e é <b>somente-adição</b>: leituras nunca são
/// alteradas/removidas (R11.4). A validação de não-decréscimo por (impressora,
/// tipo) e o ajuste administrativo auditado são aplicados no serviço (R11.5/R11.6).
/// </summary>
public class PrinterCounter : TenantEntity
{
    /// <summary>Impressora à qual a leitura pertence (R11.1).</summary>
    public Guid PrinterId { get; set; }

    /// <summary>Instante da leitura (R11.1).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Instante da leitura em ticks UTC (long), usado como chave de ordenação e
    /// cursor (R11.7). Armazenar como inteiro torna a ordenação/paginação portável
    /// entre PostgreSQL e o SQLite dos testes (que não ordena por
    /// <see cref="DateTimeOffset"/>). Preenchido a partir de <see cref="Timestamp"/>.
    /// </summary>
    public long TimestampTicks { get; set; }

    /// <summary>Tipo de contador (R11.1/R11.3).</summary>
    public CounterType CounterType { get; set; }

    /// <summary>
    /// Rótulo do tipo quando <see cref="CounterType"/> é <see cref="CounterType.Other"/>
    /// (tipos configuráveis — R11.3). Nulo para os tipos padrão.
    /// </summary>
    public string? CounterTypeLabel { get; set; }

    /// <summary>Valor do contador (R11.1). Não-decrescente por (impressora, tipo) salvo ajuste (R11.5).</summary>
    public long Value { get; set; }

    /// <summary>Origem da leitura (R11.2).</summary>
    public CounterSource Source { get; set; }

    /// <summary>Agente que originou a leitura (R11.1), quando automática.</summary>
    public Guid? WindowsClientId { get; set; }

    /// <summary>Coleta que originou a leitura (R11.1), quando aplicável.</summary>
    public Guid? CollectionId { get; set; }

    /// <summary>
    /// Indica que esta leitura é um ajuste administrativo (R11.6), permitido a
    /// reduzir valor mediante auditoria. Falso para leituras normais.
    /// </summary>
    public bool IsAdministrativeAdjustment { get; set; }
}
