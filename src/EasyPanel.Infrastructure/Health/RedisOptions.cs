namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Opções de conexão do Redis usadas pelo health check de prontidão (R1.5).
/// Valores fornecidos via <c>IConfiguration</c> (variáveis de ambiente / arquivos
/// por ambiente); segredos nunca são versionados (R1.6).
/// </summary>
public sealed class RedisOptions
{
    /// <summary>Seção de configuração de onde estas opções são vinculadas.</summary>
    public const string SectionName = "Redis";

    /// <summary>
    /// String de conexão do Redis (formato StackExchange.Redis, ex.:
    /// <c>localhost:6379,password=...</c>). Quando vazia, o health check do Redis
    /// é considerado não configurado e reporta estado não saudável.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout, em milissegundos, para a verificação de saúde do Redis.</summary>
    public int TimeoutMilliseconds { get; set; } = 2000;
}
