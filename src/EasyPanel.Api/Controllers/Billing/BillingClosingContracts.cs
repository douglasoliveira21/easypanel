namespace EasyPanel.Api.Controllers.Billing;

/// <summary>Projeção de saída de um fechamento executado (Fase 8 — R1, R6.3).</summary>
public sealed record BillingClosingResponse(
    Guid Id,
    int Year,
    int Month,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset ExecutedAt,
    Guid? ExecutedByUserId,
    int InvoiceCount);

/// <summary>Página de resultados do histórico de fechamentos (R6.3, R12.4).</summary>
public sealed record BillingClosingPageResponse(IReadOnlyList<BillingClosingResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>Corpo da requisição de execução de um fechamento mensal (R1.1).</summary>
public sealed record ExecuteBillingClosingApiRequest(int Year, int Month);
