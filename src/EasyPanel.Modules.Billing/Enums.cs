namespace EasyPanel.Modules.Billing;

/// <summary>Estado do ciclo de vida de uma <see cref="Invoice"/> (Fase 8 — R5.1). Transições
/// permitidas são impostas pelo serviço, não pelo enum — ver <c>design.md</c>.</summary>
public enum InvoiceStatus
{
    Rascunho = 0,
    Emitida = 1,
    Cancelada = 2,
}

/// <summary>
/// Tipo de contador de um <see cref="InvoiceLineItem"/> (Fase 8 — R2/R3).
/// Espelha <c>Modules.Monitoring.CounterType</c>/
/// <c>Modules.Contracts.ContractCounterType</c> (mesmos valores inteiros),
/// mantido separado por não referenciar nenhum dos dois módulos —
/// <c>Infrastructure.Billing</c> é quem sabe que os três correspondem à
/// mesma semântica de contador.
/// </summary>
public enum BillingCounterType
{
    BlackAndWhite = 0,
    Color = 1,
    A3 = 2,
    A4 = 3,
    Scan = 4,
    Other = 99,
}
