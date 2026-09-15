using EasyPanel.Modules.Reporting;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Infrastructure.Reporting;

/// <summary>
/// Validação de intervalo de datas compartilhada pelos relatórios da Fase 9
/// (R2.3/R6.5): <c>EndDate &gt;= StartDate</c> e intervalo não maior que 366
/// dias.
/// </summary>
internal static class ReportDateRangeValidator
{
    private const int MaxRangeDays = 366;

    public static Result Validate(DateTimeOffset startDate, DateTimeOffset endDate)
    {
        if (endDate < startDate)
        {
            return Result.Failure(ReportingErrors.InvalidDateRange);
        }

        if ((endDate - startDate).TotalDays > MaxRangeDays)
        {
            return Result.Failure(ReportingErrors.DateRangeTooLarge);
        }

        return Result.Success();
    }
}
