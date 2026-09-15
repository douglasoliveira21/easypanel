using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Billing;

/// <summary>Projeção de leitura de um <see cref="BillingClosing"/> (R1, R6.3).</summary>
public sealed record BillingClosingDto(
    Guid Id,
    int Year,
    int Month,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset ExecutedAt,
    Guid? ExecutedByUserId,
    int InvoiceCount);

/// <summary>Dados de execução de um fechamento mensal (R1.1). Tenant/ator vêm do contexto.</summary>
public sealed record ExecuteBillingClosingRequest(int Year, int Month);

/// <summary>Serviço de fechamento mensal (R1/R2/R3/R4).</summary>
public interface IBillingClosingService
{
    /// <summary>
    /// Executa o fechamento do período informado: consolida consumo por
    /// Impressora (R2), resolve o Contrato aplicável (R3.1/R3.2), calcula
    /// excedente contra a Franquia (R3.3–R3.5) e gera Faturas com Itens
    /// (R4). Período corrente/futuro → 400 (R1.2); já fechado → 409 (R1.3).
    /// </summary>
    Task<Result<BillingClosingDto>> ExecuteAsync(ExecuteBillingClosingRequest request, CancellationToken ct);

    /// <summary>Lista o histórico de fechamentos executados, restrito ao tenant (R6.3).</summary>
    Task<Result<PagedResult<BillingClosingDto>>> ListAsync(PageRequest page, CancellationToken ct);
}
