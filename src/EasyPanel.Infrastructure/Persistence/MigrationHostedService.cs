using EasyPanel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Serviço hospedado que aplica as migrações pendentes do banco na inicialização
/// do host, antes de o backend aceitar tráfego de negócio (R1.4).
///
/// Em produção com múltiplas instâncias, a aplicação de migrações é serializada
/// por um <em>advisory lock</em> de sessão do PostgreSQL (<c>pg_advisory_lock</c>),
/// garantindo que apenas uma instância aplique migrações por vez e evitando
/// condições de corrida. As demais instâncias aguardam o lock e, ao adquiri-lo,
/// não encontram migrações pendentes.
/// </summary>
public sealed class MigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    // Chave arbitrária, porém estável, do advisory lock. Deve ser a mesma em
    // todas as instâncias para que o lock seja mutuamente exclusivo.
    private const long MigrationAdvisoryLockKey = 5_217_045_130_001;

    private readonly DatabaseOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ApplyMigrationsOnStartup)
        {
            logger.LogInformation(
                "Aplicação de migrações na inicialização desabilitada (ApplyMigrationsOnStartup=false).");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await AcquireAdvisoryLockAsync(connection, cancellationToken).ConfigureAwait(false);

            try
            {
                var pending = (await dbContext.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false)).ToList();

                if (pending.Count == 0)
                {
                    logger.LogInformation("Nenhuma migração pendente. Banco atualizado.");
                    return;
                }

                logger.LogInformation(
                    "Aplicando {Count} migração(ões) pendente(s): {Migrations}",
                    pending.Count,
                    string.Join(", ", pending));

                await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

                logger.LogInformation("Migrações aplicadas com sucesso.");
            }
            finally
            {
                await ReleaseAdvisoryLockAsync(connection).ConfigureAwait(false);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", MigrationAdvisoryLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReleaseAdvisoryLockAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key)";
        command.Parameters.AddWithValue("key", MigrationAdvisoryLockKey);
        await command.ExecuteScalarAsync().ConfigureAwait(false);
    }
}
