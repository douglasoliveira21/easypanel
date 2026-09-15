namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Opções de conexão do armazenamento compatível com S3 (MinIO), usadas pelo
/// health check de prontidão (R1.5) e, desde a Fase 6, pelo <c>MinioFileStorage</c>
/// (primeiro uso funcional do storage — anexos de Chamado, R5). Valores
/// fornecidos via <c>IConfiguration</c>; segredos nunca são versionados (R1.6).
/// </summary>
public sealed class StorageOptions
{
    /// <summary>Seção de configuração de onde estas opções são vinculadas.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Endpoint HTTP(S) do serviço S3/MinIO (ex.: <c>http://localhost:9000</c>).
    /// Quando vazio, o health check é considerado não configurado e reporta
    /// estado não saudável.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Caminho, relativo ao endpoint, usado para a sondagem de liveness do
    /// serviço S3/MinIO. O MinIO expõe <c>/minio/health/live</c>.
    /// </summary>
    public string HealthPath { get; set; } = "/minio/health/live";

    /// <summary>Timeout, em milissegundos, para a verificação de saúde do storage.</summary>
    public int TimeoutMilliseconds { get; set; } = 2000;

    /// <summary>Chave de acesso do serviço S3/MinIO (Fase 6). Nunca logada.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Chave secreta do serviço S3/MinIO (Fase 6). Nunca logada.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Bucket usado para os anexos de Chamado (Fase 6 — R5).</summary>
    public string Bucket { get; set; } = string.Empty;
}
