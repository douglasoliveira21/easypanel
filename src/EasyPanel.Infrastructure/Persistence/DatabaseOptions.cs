namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Opções de configuração do banco de dados, vinculadas a partir de
/// <c>IConfiguration</c> (variáveis de ambiente / arquivos por ambiente).
/// Segredos nunca são versionados (R1.6).
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Seção de configuração de onde estas opções são vinculadas.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Cadeia de conexão do PostgreSQL. Deve ser fornecida via variável de
    /// ambiente ou cofre de configuração (nunca commitada).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Se verdadeiro, o <c>MigrationHostedService</c> aplica migrações pendentes
    /// na inicialização (R1.4). Padrão: verdadeiro.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>
    /// Timeout, em segundos, para aquisição do advisory lock do PostgreSQL
    /// que serializa a aplicação de migrações entre múltiplas instâncias.
    /// </summary>
    public int MigrationLockTimeoutSeconds { get; set; } = 120;
}
