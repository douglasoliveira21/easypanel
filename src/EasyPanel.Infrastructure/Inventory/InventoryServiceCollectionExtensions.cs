using EasyPanel.Modules.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Inventory;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 5 (Estoque):
/// <see cref="IInventoryItemService"/> e <see cref="IInventoryMovementService"/>,
/// ambos <c>scoped</c> (dependem do <c>AppDbContext</c> e do
/// <see cref="EasyPanel.Modules.Identity.ICurrentUserAccessor"/>, ambos scoped).
/// Pressupõe que a persistência (<c>AddPersistence</c>) já esteja registrada.
/// </summary>
public static class InventoryServiceCollectionExtensions
{
    /// <summary>Registra os serviços de domínio de item/movimentação de estoque.</summary>
    public static IServiceCollection AddInventoryServices(this IServiceCollection services)
    {
        // TimeProvider injetável (idempotente caso já registrado por outro módulo).
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IInventoryItemService, InventoryItemService>();
        services.AddScoped<IInventoryMovementService, InventoryMovementService>();

        return services;
    }
}
