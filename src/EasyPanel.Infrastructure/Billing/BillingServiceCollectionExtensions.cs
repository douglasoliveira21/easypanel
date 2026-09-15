using EasyPanel.Modules.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Billing;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 8 (Fechamento e
/// Faturamento): <see cref="IBillingClosingService"/> e
/// <see cref="IInvoiceService"/>, ambos <c>scoped</c> (dependem do
/// <c>AppDbContext</c> e do <see cref="EasyPanel.Modules.Identity.ICurrentUserAccessor"/>,
/// ambos scoped). Pressupõe que a persistência (<c>AddPersistence</c>) e os
/// serviços de Contratos (<c>AddContractServices</c>, Fase 7) já estejam
/// registrados.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary>Registra os serviços de domínio de fechamento e fatura.</summary>
    public static IServiceCollection AddBillingServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IBillingClosingService, BillingClosingService>();
        services.AddScoped<IInvoiceService, InvoiceService>();

        return services;
    }
}
