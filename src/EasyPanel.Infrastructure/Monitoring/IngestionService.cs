using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IIngestionService"/> (R13/R14.1).
///
/// Recebe uma submissão de coleta do agente autenticado, aplica idempotência pela
/// <c>IdempotencyKey</c> (índice único em <see cref="Collection"/> + backstop em
/// <see cref="IngestionDedup"/>) e persiste a coleta em estado pendente de
/// processamento, retornando um ack imediato sem aguardar a persistência final dos
/// contadores (R14.1). O processamento assíncrono é responsabilidade do worker
/// (Task 4.2), que consome as coletas registradas.
///
/// <para>Idempotência (R13.4): uma chave já vista retorna o ack equivalente
/// (mesmo <c>CollectionId</c>) sem recriar dados.</para>
/// </summary>
public sealed class IngestionService : IIngestionService
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly AppDbContext _dbContext;
    private readonly IClientContext _clientContext;

    public IngestionService(AppDbContext dbContext, IClientContext clientContext)
    {
        _dbContext = dbContext;
        _clientContext = clientContext;
    }

    /// <inheritdoc />
    public async Task<Result<IngestionAck>> SubmitAsync(
        ClientCollectionSubmission submission,
        CancellationToken ct)
    {
        if (submission is null
            || string.IsNullOrWhiteSpace(submission.IdempotencyKey)
            || !_clientContext.IsAuthenticated
            || _clientContext.ClientId is not { } clientId)
        {
            return Result.Failure<IngestionAck>(MonitoringErrors.Unauthorized);
        }

        // Chave já processada → ack equivalente (R13.4).
        var existing = await _dbContext.Set<Collection>()
            .FirstOrDefaultAsync(c => c.IdempotencyKey == submission.IdempotencyKey, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Success(new IngestionAck(existing.Id, Duplicate: true));
        }

        var now = Clock.GetUtcNow();

        // Serializa os dados coletados (contadores + suprimentos — Fase 4) para
        // processamento posterior pelo worker.
        var payload = JsonSerializer.Serialize(
            new CollectionPayload(submission.Counters, submission.Supplies ?? []));

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = submission.IdempotencyKey,
            WindowsClientId = clientId,
            PrinterId = submission.PrinterId,
            StartedAt = submission.StartedAt,
            FinishedAt = submission.FinishedAt,
            Result = submission.Success ? CollectionResult.Success : CollectionResult.Failure,
            Errors = submission.Errors,
            CollectedData = payload,
            AttemptCount = 0,
            CreatedAt = now,
        };

        _dbContext.Set<Collection>().Add(collection);

        try
        {
            await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Corrida de idempotência: outra requisição concorrente persistiu a
            // mesma chave primeiro (índice único). Recarrega e devolve ack
            // equivalente (R13.4), sem duplicar.
            _dbContext.ChangeTracker.Clear();
            var winner = await _dbContext.Set<Collection>()
                .FirstOrDefaultAsync(c => c.IdempotencyKey == submission.IdempotencyKey, ct)
                .ConfigureAwait(false);

            return winner is not null
                ? Result.Success(new IngestionAck(winner.Id, Duplicate: true))
                : Result.Failure<IngestionAck>(MonitoringErrors.NotFound);
        }

        return Result.Success(new IngestionAck(collection.Id, Duplicate: false));
    }
}
