namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Opções de armazenamento de arquivos, usadas pelo health check de prontidão
/// (R1.5) e pela implementação de <c>IFileStorage</c> (anexos de Chamado,
/// Fase 6 — R5). Dois provedores são suportados via <see cref="Provider"/>:
/// <c>Minio</c> (S3-compatible, padrão) e <c>Local</c> (disco local — útil
/// para deploys sem um serviço S3 dedicado, ex. atrás de um painel que já
/// gerencia volumes persistentes). Valores fornecidos via
/// <c>IConfiguration</c>; segredos nunca são versionados (R1.6).
/// </summary>
public sealed class StorageOptions
{
    /// <summary>Seção de configuração de onde estas opções são vinculadas.</summary>
    public const string SectionName = "Storage";

    /// <summary>Nome do provedor <c>Local</c> (disco local, sem S3).</summary>
    public const string ProviderLocal = "Local";

    /// <summary>Nome do provedor <c>Minio</c> (S3-compatible) — padrão.</summary>
    public const string ProviderMinio = "Minio";

    /// <summary>
    /// Provedor de armazenamento: <c>Minio</c> (padrão, S3-compatible) ou
    /// <c>Local</c> (disco local, ver <see cref="LocalBasePath"/>).
    /// </summary>
    public string Provider { get; set; } = ProviderMinio;

    /// <summary>
    /// Endpoint HTTP(S) do serviço S3/MinIO (ex.: <c>http://localhost:9000</c>).
    /// Aplicável apenas ao provedor <c>Minio</c>. Quando vazio, o health check
    /// desse provedor é considerado não configurado e reporta estado não
    /// saudável.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Caminho, relativo ao endpoint, usado para a sondagem de liveness do
    /// serviço S3/MinIO. O MinIO expõe <c>/minio/health/live</c>. Aplicável
    /// apenas ao provedor <c>Minio</c>.
    /// </summary>
    public string HealthPath { get; set; } = "/minio/health/live";

    /// <summary>Timeout, em milissegundos, para a verificação de saúde do storage.</summary>
    public int TimeoutMilliseconds { get; set; } = 2000;

    /// <summary>Chave de acesso do serviço S3/MinIO (Fase 6). Aplicável apenas ao provedor <c>Minio</c>. Nunca logada.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Chave secreta do serviço S3/MinIO (Fase 6). Aplicável apenas ao provedor <c>Minio</c>. Nunca logada.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Bucket usado para os anexos de Chamado (Fase 6 — R5). Aplicável apenas ao provedor <c>Minio</c>.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Diretório base no disco do container onde os arquivos são gravados.
    /// Aplicável apenas ao provedor <c>Local</c> — deve apontar para um
    /// volume persistente (ex.: <c>/data/storage</c>), nunca para uma camada
    /// efêmera do container.
    /// </summary>
    public string LocalBasePath { get; set; } = "/data/storage";
}
