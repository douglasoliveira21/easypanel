using Microsoft.Data.Sqlite;

namespace EasyPanel.WindowsClient;

/// <summary>
/// Implementação de <see cref="ILocalQueue"/> sobre SQLite local (R5.4).
///
/// Cada coleta é persistida com sua chave de idempotência (única — reenfileirar a
/// mesma chave não duplica) e o payload JSON. A fila sobrevive a reinícios do
/// agente. Somente dados de negócio da coleta são gravados; credenciais, tokens e
/// segredos SNMP nunca são persistidos aqui (R5.6) — cabe ao chamador construir o
/// payload já livre de segredos (ver <see cref="SecretRedactor"/>).
/// </summary>
public sealed class SqliteLocalQueue : ILocalQueue, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Cria a fila sobre a cadeia de conexão informada (ex.: <c>Data Source=queue.db</c>)
    /// e garante o schema. Aceita conexões in-memory compartilhadas nos testes.
    /// </summary>
    public SqliteLocalQueue(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS queued_collections (
                idempotency_key TEXT PRIMARY KEY,
                payload TEXT NOT NULL,
                enqueued_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(QueuedCollection item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            // INSERT OR IGNORE: reenfileirar a mesma chave é no-op (idempotente).
            cmd.CommandText =
                """
                INSERT OR IGNORE INTO queued_collections (idempotency_key, payload, enqueued_at)
                VALUES ($key, $payload, $enqueuedAt);
                """;
            cmd.Parameters.AddWithValue("$key", item.IdempotencyKey);
            cmd.Parameters.AddWithValue("$payload", item.Payload);
            cmd.Parameters.AddWithValue("$enqueuedAt", item.EnqueuedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedCollection>> PeekAsync(int max, CancellationToken ct)
    {
        if (max <= 0)
        {
            return [];
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT idempotency_key, payload, enqueued_at
                FROM queued_collections
                ORDER BY enqueued_at ASC
                LIMIT $max;
                """;
            cmd.Parameters.AddWithValue("$max", max);

            var items = new List<QueuedCollection>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new QueuedCollection(
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)));
            }

            return items;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(string idempotencyKey, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM queued_collections WHERE idempotency_key = $key;";
            cmd.Parameters.AddWithValue("$key", idempotencyKey);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM queued_collections;";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        _connection.Dispose();
    }
}
