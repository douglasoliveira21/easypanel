using EasyPanel.Modules.Ticketing;

namespace EasyPanel.Infrastructure.Ticketing;

/// <summary>
/// Parâmetros operacionais de Chamados/SLA (Fase 6), vinculados à seção
/// <c>Ticketing</c> da configuração: prazos padrão de plataforma por
/// prioridade (usados quando o tenant não configurou uma <see cref="SlaPolicy"/>
/// própria, R4.2) e limites de anexo (R5.2).
/// </summary>
public sealed class TicketingOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Ticketing";

    /// <summary>Prazo padrão de plataforma para prioridade Baixa.</summary>
    public SlaDefault Baixa { get; set; } = new() { FirstResponseMinutes = 240, ResolutionMinutes = 2880 };

    /// <summary>Prazo padrão de plataforma para prioridade Média.</summary>
    public SlaDefault Media { get; set; } = new() { FirstResponseMinutes = 120, ResolutionMinutes = 1440 };

    /// <summary>Prazo padrão de plataforma para prioridade Alta.</summary>
    public SlaDefault Alta { get; set; } = new() { FirstResponseMinutes = 60, ResolutionMinutes = 480 };

    /// <summary>Prazo padrão de plataforma para prioridade Urgente.</summary>
    public SlaDefault Urgente { get; set; } = new() { FirstResponseMinutes = 30, ResolutionMinutes = 240 };

    /// <summary>Tamanho máximo de um anexo, em bytes (R5.2). Default: 10 MB.</summary>
    public long MaxAttachmentSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Tipos MIME permitidos para anexo (R5.2).</summary>
    public IReadOnlyList<string> AllowedAttachmentContentTypes { get; set; } =
    [
        "image/png",
        "image/jpeg",
        "image/webp",
        "application/pdf",
    ];

    /// <summary>Resolve o prazo padrão de plataforma para a prioridade informada.</summary>
    public SlaDefault ForPriority(TicketPriority priority) => priority switch
    {
        TicketPriority.Baixa => Baixa,
        TicketPriority.Media => Media,
        TicketPriority.Alta => Alta,
        TicketPriority.Urgente => Urgente,
        _ => Media,
    };
}

/// <summary>Prazos de SLA (em minutos) de primeira resposta e de resolução.</summary>
public sealed record SlaDefault
{
    /// <summary>Prazo de primeira resposta, em minutos.</summary>
    public int FirstResponseMinutes { get; init; }

    /// <summary>Prazo de resolução, em minutos.</summary>
    public int ResolutionMinutes { get; init; }
}
