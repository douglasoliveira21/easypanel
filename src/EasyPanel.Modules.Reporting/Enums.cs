namespace EasyPanel.Modules.Reporting;

/// <summary>
/// Tipo de contador usado no relatório de consumo (Fase 9 — R2). Espelha
/// <c>Modules.Monitoring.CounterType</c> (mesmos valores inteiros), quarto
/// enum espelhado da plataforma (depois de <c>ContractCounterType</c> e
/// <c>BillingCounterType</c>), pelo mesmo motivo: <c>Modules.Reporting</c>
/// não referencia <c>Modules.Monitoring</c>.
/// </summary>
public enum ReportingCounterType
{
    BlackAndWhite = 0,
    Color = 1,
    A3 = 2,
    A4 = 3,
    Scan = 4,
    Other = 99,
}

/// <summary>
/// Status de fatura usado como filtro do relatório de faturamento (Fase 9 —
/// R3). Espelha <c>Modules.Billing.InvoiceStatus</c> (mesmos valores
/// inteiros), quinto enum espelhado da plataforma, pelo mesmo motivo:
/// <c>Modules.Reporting</c> não referencia <c>Modules.Billing</c>.
/// </summary>
public enum ReportingInvoiceStatus
{
    Rascunho = 0,
    Emitida = 1,
    Cancelada = 2,
}
