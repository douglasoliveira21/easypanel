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
/// ambos scoped). <see cref="IFileStorage"/> (via <see cref="MinioFileStorage"/>)
/// é registrado como singleton (cliente MinIO reutilizável, mesmo padrão de
/// <c>HttpClient</c>). Pressupõe que a persistência (<c>AddPersistence</c>) já
/// esteja registrada.
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

        services.AddSingleton<IFileStorage, MinioFileStorage>();

        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ISlaPolicyService, SlaPolicyService>();
        services.AddScoped<ITicketAttachmentService, TicketAttachmentService>();

        return services;
    }
}
