using System.Text.Json;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="ICollectionProcessor"/> (R14.2–R14.4, R12).
///
/// Consome uma <see cref="Collection"/> registrada e persiste, de forma atômica e
/// idempotente, os <see cref="PrinterCounter"/>s lidos, o status da
/// <see cref="Printer"/> e eventuais <see cref="PrinterEvent"/>s. A idempotência
/// (R14.4) é garantida pelo backstop <see cref="IngestionDedup"/>: uma coleta já
/// processada não reaplica dados. Falhas de coleta (R12.3) registram um evento de
/// falha e incrementam o contador de tentativas para reprocessamento posterior.
///
/// <para>Opera com contexto de sistema (Super Admin) por ser uma tarefa de
/// plataforma; o <c>TenantId</c> de cada linha vem sempre da própria coleta.</para>
/// </summary>
public sealed class CollectionProcessor : ICollectionProcessor
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly ISystemDbContextFactory _contextFactory;
    private readonly ILogger<CollectionProcessor> _logger;
    private readonly MonitoringOptions _options;

    public CollectionProcessor(
        ISystemDbContextFactory contextFactory,
        ILogger<CollectionProcessor> logger,
        IOptions<MonitoringOptions> options)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task ProcessAsync(Guid collectionId, CancellationToken ct)
    {
        await using var context = _contextFactory.Create();

        var collection = await context.Set<Collection>()
            .FirstOrDefaultAsync(c => c.Id == collectionId, ct)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return;
        }

        // Idempotência (R14.4): se já há dedup para esta chave, nada a fazer.
        var alreadyProcessed = await context.Set<IngestionDedup>()
            .AnyAsync(d => d.TenantId == collection.TenantId
                && d.IdempotencyKey == collection.IdempotencyKey, ct)
            .ConfigureAwait(false);

        if (alreadyProcessed)
        {
            return;
        }

        var now = Clock.GetUtcNow();

        try
        {
            if (collection.Result == CollectionResult.Failure)
            {
                await HandleFailureAsync(context, collection, now, ct).ConfigureAwait(false);
            }
            else
            {
                await HandleSuccessAsync(context, collection, now, ct).ConfigureAwait(false);
            }

            // Marca a coleta como processada (backstop de idempotência — R13.3/R14.4).
            context.Set<IngestionDedup>().Add(new IngestionDedup
            {
                Id = Guid.NewGuid(),
                TenantId = collection.TenantId,
                IdempotencyKey = collection.IdempotencyKey,
                CollectionId = collection.Id,
                CreatedAt = now,
            });

            // Atualiza a última coleta do agente (R12.5).
            var client = await context.Set<WindowsClient>()
                .FirstOrDefaultAsync(c => c.Id == collection.WindowsClientId, ct)
                .ConfigureAwait(false);
            if (client is not null)
            {
                client.LastCollectionAt = collection.FinishedAt ?? now;
                client.UpdatedAt = now;
            }

            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Não engole a falha: incrementa tentativas para reprocessamento e
            // propaga para o mecanismo de retry do worker (R14.3).
            _logger.LogError(ex, "Falha ao processar coleta {CollectionId}.", collectionId);
            await IncrementAttemptAsync(collectionId, ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task HandleSuccessAsync(
        Persistence.AppDbContext context,
        Collection collection,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var payload = DeserializePayload(collection.CollectedData);

        foreach (var counter in payload.Counters)
        {
            var type = ParseCounterType(counter.CounterType);

            // Não-decréscimo (R11.5): ignora leitura inferior ao último valor do
            // mesmo (impressora, tipo). Coletas automáticas não reduzem contador.
            if (collection.PrinterId is { } printerId)
            {
                // Maior valor já registrado para (impressora, tipo). Compara long
                // (portável em qualquer provider, ao contrário de ordenar por
                // DateTimeOffset). Ausência de leituras → null → aceita a primeira.
                var maxValue = await context.Set<PrinterCounter>()
                    .Where(c => c.PrinterId == printerId && c.CounterType == type)
                    .Select(c => (long?)c.Value)
                    .MaxAsync(ct)
                    .ConfigureAwait(false);

                if (maxValue is { } max && counter.Value < max)
                {
                    continue;
                }

                var timestamp = collection.FinishedAt ?? now;
                context.Set<PrinterCounter>().Add(new PrinterCounter
                {
                    Id = Guid.NewGuid(),
                    TenantId = collection.TenantId,
                    PrinterId = printerId,
                    Timestamp = timestamp,
                    TimestampTicks = timestamp.UtcTicks,
                    CounterType = type,
                    CounterTypeLabel = type == CounterType.Other ? counter.CounterTypeLabel : null,
                    Value = counter.Value,
                    Source = CounterSource.Automatic,
                    WindowsClientId = collection.WindowsClientId,
                    CollectionId = collection.Id,
                    CreatedAt = now,
                });
            }
        }

        // Atualiza o status da impressora para online em coleta bem-sucedida.
        if (collection.PrinterId is { } pid)
        {
            var printer = await context.Set<Printer>()
                .FirstOrDefaultAsync(p => p.Id == pid, ct)
                .ConfigureAwait(false);
            if (printer is not null && printer.Status != PrinterStatus.Online)
            {
                printer.Status = PrinterStatus.Online;
                printer.UpdatedAt = now;
                AddStatusEvent(context, collection.TenantId, pid, "online", now);
            }

            // Suprimentos (Fase 4 — R2.4/R4.3/R4.4): persiste cada leitura e
            // detecta a transição de cruzamento do limiar vigente.
            foreach (var supply in payload.Supplies)
            {
                await ProcessSupplyReadingAsync(context, collection, pid, supply, now, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Persiste uma <see cref="SupplyReading"/> e, na transição de cruzamento
    /// (leitura anterior acima do limiar, leitura atual igual/abaixo), grava um
    /// <see cref="PrinterEvent"/> do tipo <see cref="PrinterEventType.SupplyLow"/>
    /// para consumo do motor de alertas da Fase 3 — sem nenhuma alteração em
    /// <c>Modules.Alerting</c>.
    /// </summary>
    private async Task ProcessSupplyReadingAsync(
        Persistence.AppDbContext context,
        Collection collection,
        Guid printerId,
        SubmittedSupply supply,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Última leitura anterior do mesmo (impressora, rótulo), para detectar a
        // transição de cruzamento. Comparação por long (TimestampTicks), portável.
        var previous = await context.Set<SupplyReading>()
            .Where(r => r.PrinterId == printerId && r.Label == supply.Label)
            .OrderByDescending(r => r.TimestampTicks)
            .Select(r => (int?)r.Percent)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var timestamp = collection.FinishedAt ?? now;
        context.Set<SupplyReading>().Add(new SupplyReading
        {
            Id = Guid.NewGuid(),
            TenantId = collection.TenantId,
            PrinterId = printerId,
            Label = supply.Label,
            Percent = supply.Percent,
            Timestamp = timestamp,
            TimestampTicks = timestamp.UtcTicks,
            WindowsClientId = collection.WindowsClientId,
            CollectionId = collection.Id,
            CreatedAt = now,
        });

        var threshold = await ResolveThresholdAsync(context, collection.TenantId, printerId, supply.Label, ct)
            .ConfigureAwait(false);

        var crossedNow = supply.Percent <= threshold;
        var wasAboveBefore = previous is null || previous > threshold;

        if (crossedNow && wasAboveBefore)
        {
            context.Set<PrinterEvent>().Add(new PrinterEvent
            {
                Id = Guid.NewGuid(),
                TenantId = collection.TenantId,
                PrinterId = printerId,
                Type = PrinterEventType.SupplyLow,
                Detail = $"{supply.Label}: {supply.Percent}% (limiar {threshold}%)",
                OccurredAt = now,
                CreatedAt = now,
                CreatedAtTicks = now.UtcTicks,
            });
        }
    }

    /// <summary>
    /// Resolve o limiar vigente em cascata (Fase 4 — R4.1/R4.2):
    /// (Impressora, Rótulo) → (Tenant, Rótulo) → (Tenant geral) → padrão de
    /// plataforma (<see cref="MonitoringOptions.DefaultSupplyThresholdPercent"/>).
    /// </summary>
    private async Task<int> ResolveThresholdAsync(
        Persistence.AppDbContext context,
        Guid tenantId,
        Guid printerId,
        string label,
        CancellationToken ct)
    {
        var candidates = await context.Set<SupplyThreshold>()
            .Where(t => t.TenantId == tenantId
                && ((t.PrinterId == printerId && t.Label == label)
                    || (t.PrinterId == null && t.Label == label)
                    || (t.PrinterId == null && t.Label == null)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var specific = candidates.FirstOrDefault(t => t.PrinterId == printerId && t.Label == label);
        if (specific is not null)
        {
            return specific.ThresholdPercent;
        }

        var tenantLabel = candidates.FirstOrDefault(t => t.PrinterId == null && t.Label == label);
        if (tenantLabel is not null)
        {
            return tenantLabel.ThresholdPercent;
        }

        var tenantGeneral = candidates.FirstOrDefault(t => t.PrinterId == null && t.Label == null);
        return tenantGeneral?.ThresholdPercent ?? _options.DefaultSupplyThresholdPercent;
    }

    private static async Task HandleFailureAsync(
        Persistence.AppDbContext context,
        Collection collection,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Falha de coleta (R12.3): marca a impressora como sem comunicação e gera
        // um evento de falha para consumo do motor de alertas da Fase 3.
        if (collection.PrinterId is { } pid)
        {
            var printer = await context.Set<Printer>()
                .FirstOrDefaultAsync(p => p.Id == pid, ct)
                .ConfigureAwait(false);
            if (printer is not null)
            {
                printer.Status = PrinterStatus.NoCommunication;
                printer.UpdatedAt = now;
            }
        }

        context.Set<PrinterEvent>().Add(new PrinterEvent
        {
            Id = Guid.NewGuid(),
            TenantId = collection.TenantId,
            PrinterId = collection.PrinterId,
            WindowsClientId = collection.WindowsClientId,
            Type = PrinterEventType.CollectionFailure,
            Detail = collection.Errors,
            OccurredAt = now,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
        });
    }

    private static void AddStatusEvent(
        Persistence.AppDbContext context,
        Guid tenantId,
        Guid printerId,
        string detail,
        DateTimeOffset now)
    {
        context.Set<PrinterEvent>().Add(new PrinterEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PrinterId = printerId,
            Type = PrinterEventType.StatusChanged,
            Detail = detail,
            OccurredAt = now,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
        });
    }

    private async Task IncrementAttemptAsync(Guid collectionId, CancellationToken ct)
    {
        try
        {
            await using var context = _contextFactory.Create();
            var collection = await context.Set<Collection>()
                .FirstOrDefaultAsync(c => c.Id == collectionId, ct)
                .ConfigureAwait(false);
            if (collection is not null)
            {
                collection.AttemptCount++;
                collection.UpdatedAt = Clock.GetUtcNow();
                await context.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao incrementar tentativas da coleta {CollectionId}.", collectionId);
        }
    }

    private static CollectionPayload DeserializePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CollectionPayload([], []);
        }

        try
        {
            // Dado gravado antes da Fase 4 é um array de contadores solto, não um
            // envelope {Counters, Supplies}. Tenta o envelope primeiro; se o JSON
            // for um array puro, cai para o formato legado (só contadores).
            if (json.TrimStart().StartsWith('['))
            {
                return new CollectionPayload(JsonSerializer.Deserialize<List<SubmittedCounter>>(json) ?? [], []);
            }

            var payload = JsonSerializer.Deserialize<CollectionPayload>(json);
            return payload is null
                ? new CollectionPayload([], [])
                : new CollectionPayload(payload.Counters ?? [], payload.Supplies ?? []);
        }
        catch (JsonException)
        {
            return new CollectionPayload([], []);
        }
    }

    private static CounterType ParseCounterType(string value) =>
        Enum.TryParse<CounterType>(value, ignoreCase: true, out var type) ? type : CounterType.Other;
}
