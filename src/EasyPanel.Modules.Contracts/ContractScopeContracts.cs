using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Contracts;

/// <summary>Serviço de escopo (Local/Impressora) de um <see cref="Contract"/> (R2).</summary>
public interface IContractScopeService
{
    /// <summary>Vincula um Local ao contrato, validando mesmo Cliente (R2.3) e ausência de sobreposição (R2.4).</summary>
    Task<Result> AddLocationAsync(Guid contractId, Guid locationId, CancellationToken ct);

    /// <summary>Remove o vínculo de um Local ao contrato.</summary>
    Task<Result> RemoveLocationAsync(Guid contractId, Guid locationId, CancellationToken ct);

    /// <summary>Lista os Locais vinculados ao contrato, restrito ao tenant.</summary>
    Task<Result<IReadOnlyList<Guid>>> ListLocationsAsync(Guid contractId, CancellationToken ct);

    /// <summary>Vincula uma Impressora ao contrato, validando mesmo Cliente (R2.3) e ausência de sobreposição (R2.4).</summary>
    Task<Result> AddPrinterAsync(Guid contractId, Guid printerId, CancellationToken ct);

    /// <summary>Remove o vínculo de uma Impressora ao contrato.</summary>
    Task<Result> RemovePrinterAsync(Guid contractId, Guid printerId, CancellationToken ct);

    /// <summary>Lista as Impressoras vinculadas ao contrato, restrito ao tenant.</summary>
    Task<Result<IReadOnlyList<Guid>>> ListPrintersAsync(Guid contractId, CancellationToken ct);
}
