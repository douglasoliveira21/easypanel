using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Entrada somente-adição do histórico de movimentação/ciclo de vida de uma
/// <see cref="Printer"/> (R10). Nunca é alterada ou removida (R10.3), preservando
/// a rastreabilidade de onde cada equipamento esteve e seu estado ao longo do tempo.
/// </summary>
public class PrinterMovement : TenantEntity
{
    /// <summary>Impressora movimentada (R10.2).</summary>
    public Guid PrinterId { get; set; }

    /// <summary>Operação de ciclo de vida registrada (R10.1).</summary>
    public MovementOperation Operation { get; set; }

    /// <summary>Local de origem, quando aplicável (R10.2).</summary>
    public Guid? FromLocationId { get; set; }

    /// <summary>Local de destino, quando aplicável (transferência/instalação) (R10.2).</summary>
    public Guid? ToLocationId { get; set; }

    /// <summary>Ator (usuário) que executou a operação (R10.2).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Momento da operação (UTC) (R10.2).</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
