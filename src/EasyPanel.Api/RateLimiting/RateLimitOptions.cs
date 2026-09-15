using System.ComponentModel.DataAnnotations;

namespace EasyPanel.Api.RateLimiting;

/// <summary>
/// Parâmetros do rate limiting (R4.6, R4.7), vinculados da seção
/// <c>RateLimiting</c> da configuração. Uma janela fixa (fixed window) limita o
/// número de requisições por partição em cada intervalo.
///
/// <para><b>Partição.</b> A chave de partição prioriza, nesta ordem, o usuário
/// autenticado (claim <c>sub</c>), o tenant, e por fim o endereço IP de origem —
/// combinada com o endpoint (método + caminho) — de modo que o limite seja
/// aplicado por usuário, por tenant, por IP e por endpoint (R4.6), nunca apenas
/// por IP.</para>
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Número máximo de requisições permitidas por partição na janela.</summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    /// <summary>Duração da janela fixa, em segundos.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Tamanho da fila de espera. 0 desabilita o enfileiramento: ao exceder o
    /// limite, a requisição é imediatamente recusada com 429 (R4.7).
    /// </summary>
    [Range(0, int.MaxValue)]
    public int QueueLimit { get; set; }
}
