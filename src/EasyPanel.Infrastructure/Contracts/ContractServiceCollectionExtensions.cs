using EasyPanel.Modules.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Contracts;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 7 (Contratos):
/// <see cref="IContractService"/>, <see cref="IContractScopeService"/> e
/// <see cref="IContractFranchiseService"/>, todos <c>scoped</c> (dependem do
/// <c>AppDbContext</c> e do <see cref="EasyPanel.Modules.Identity.ICurrentUserAccessor"/>,
/// ambos scoped). Pressupõe que a persistência (<c>AddPersistence</c>) já
/// esteja registrada.
/// </summary>
public static class ContractServiceCollectionExtensions
{
    /// <summary>Registra os serviços de domínio de contrato/escopo/franquia.</summary>
    public static IServiceCollection AddContractServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IContractService, ContractService>();
        services.AddScoped<IContractScopeService, ContractScopeService>();
        services.AddScoped<IContractFranchiseService, ContractFranchiseService>();

        return services;
    }
}
