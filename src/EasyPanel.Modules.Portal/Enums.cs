namespace EasyPanel.Modules.Portal;

/// <summary>
/// Status de uma impressora exposto pelo Portal do Cliente (Fase 10 — R3).
/// Espelha <c>Modules.Monitoring.PrinterStatus</c> (mesmos valores) — sexto
/// enum espelhado da plataforma (depois de <c>ContractCounterType</c>,
/// <c>BillingCounterType</c>, <c>ReportingCounterType</c> e
/// <c>ReportingInvoiceStatus</c>) pelo mesmo motivo: <c>Modules.Portal</c> não
/// referencia <c>Modules.Monitoring</c>.
/// </summary>
public enum PortalPrinterStatus
{
    Online = 0,
    Offline = 1,
    Unknown = 2,
    Disabled = 3,
    NoCommunication = 4,
}

/// <summary>
/// Status de um Chamado exposto pelo Portal do Cliente (Fase 10 — R4). Espelha
/// <c>Modules.Ticketing.TicketStatus</c> (mesmos valores) — sétimo enum
/// espelhado da plataforma.
/// </summary>
public enum PortalTicketStatus
{
    Aberto = 0,
    EmAndamento = 1,
    AguardandoCliente = 2,
    Resolvido = 3,
    Fechado = 4,
    Cancelado = 5,
}

/// <summary>
/// Status de uma Fatura exposto pelo Portal do Cliente (Fase 10 — R5). Espelha
/// <c>Modules.Billing.InvoiceStatus</c> (mesmos valores) — oitavo enum
/// espelhado da plataforma. O portal nunca expõe faturas em
/// <see cref="Rascunho"/> (R5.2); o valor existe aqui apenas para completude do
/// espelhamento.
/// </summary>
public enum PortalInvoiceStatus
{
    Rascunho = 0,
    Emitida = 1,
    Cancelada = 2,
}
