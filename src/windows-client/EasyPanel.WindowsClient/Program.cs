using EasyPanel.WindowsClient;
using EasyPanel.WindowsClient.Snmp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Instância única por máquina (R2.1): uma segunda instância encerra graciosamente.
using var instance = SingleInstanceGuard.Acquire("default");
if (!instance.IsOwner)
{
    Console.Error.WriteLine("Já existe uma instância do agente em execução. Encerrando.");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

// Execução como Serviço Windows quando instalado como tal; como console em dev.
builder.Services.AddWindowsService(options => options.ServiceName = "EasyPanel Agent");

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

// Fila local offline (R5.4): SQLite no caminho configurado.
builder.Services.AddSingleton<ILocalQueue>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    return new SqliteLocalQueue($"Data Source={options.LocalQueuePath}");
});

// Cliente HTTP do backend (Fase 4 — R1.2/R1.6): HttpClient gerenciado pela
// factory (pooling de handler/socket), mas o IBackendClient é singleton para
// compartilhar o cache de token entre o Net_Monitoring_Service e o
// Communication_Service (evita autenticar duas vezes desnecessariamente).
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IBackendClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpBackendClient));
    if (!string.IsNullOrWhiteSpace(options.BackendBaseUrl))
    {
        httpClient.BaseAddress = new Uri(options.BackendBaseUrl);
    }

    return new HttpBackendClient(httpClient, sp.GetRequiredService<IOptions<AgentOptions>>(), sp.GetRequiredService<ILogger<HttpBackendClient>>());
});

// Descoberta/coleta SNMP real (Fase 4 — R8.1), sobre Lextm.SharpSnmpLib.
builder.Services.AddSingleton<ISnmpCollector, SnmpCollector>();
builder.Services.AddSingleton<PrinterDriverSelector>();
builder.Services.AddSingleton<PrinterDiscoveryService>();

// Reenvio da fila local (Fase 2, reaproveitado sem alteração pelo Communication_Service).
builder.Services.AddSingleton<QueueResender>();

// Serviços internos supervisionados (Fase 4 — R1.1/R1.4): substituem a lista
// vazia da Fase 2, que aguardava a materialização sobre transporte real.
builder.Services.AddSingleton<NetMonitoringService>();
builder.Services.AddSingleton<CommunicationService>();
builder.Services.AddSingleton<IEnumerable<ISupervisedService>>(sp =>
[
    sp.GetRequiredService<NetMonitoringService>(),
    sp.GetRequiredService<CommunicationService>(),
]);
builder.Services.AddSingleton<Guardian>();

builder.Services.AddHostedService<GuardianWorker>();

var host = builder.Build();
host.Run();
