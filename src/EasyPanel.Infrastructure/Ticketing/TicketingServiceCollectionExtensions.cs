using EasyPanel.Infrastructure.Health;
using EasyPanel.Infrastructure.Storage;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Ticketing;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 6 (Chamados/Helpdesk e SLA):
/// <see cref="ITicketService"/>, <see cref="ISlaPolicyService"/> e
/// <see cref="ITicketAttachmentService"/>, todos <c>scoped</c> (dependem do
/// <c>AppDbContext</c> e do <see cref="EasyPanel.Modules.Identity.ICurrentUserAccessor"/>,
/// ambos scoped). <see cref="IFileStorage"/> é registrado como singleton
/// (cliente/handle reutilizável, mesmo padrão de <c>HttpClient</c>); a
/// implementação depende de <c>Storage:Provider</c> (<see cref="MinioFileStorage"/>,
/// padrão, ou <see cref="LocalFileStorage"/> — disco local). Pressupõe que a
/// persistência (<c>AddPersistence</c>) já esteja registrada.
/// </summary>
public static class TicketingServiceCollectionExtensions
{
    /// <summary>Vincula as <see cref="TicketingOptions"/> e registra os serviços de domínio e o storage de anexos.</summary>
    public static IServiceCollection AddTicketingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<TicketingOptions>()
            .Bind(configuration.GetSection(TicketingOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        // Provedor de storage: lido diretamente da configuração (não via DI)
        // porque a escolha da implementação precisa ser conhecida no momento
        // do registro, antes do container resolver IOptions<StorageOptions>.
        var storageProvider = configuration[$"{StorageOptions.SectionName}:{nameof(StorageOptions.Provider)}"];
        if (string.Equals(storageProvider, StorageOptions.ProviderLocal, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, LocalFileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, MinioFileStorage>();
        }

        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ISlaPolicyService, SlaPolicyService>();
        services.AddScoped<ITicketAttachmentService, TicketAttachmentService>();

        return services;
    }
}
