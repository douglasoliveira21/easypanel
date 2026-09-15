using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient;

/// <summary>Requisição de autenticação do agente (espelha o contrato do backend — R4.1).</summary>
internal sealed record TokenRequest(string ClientId, string ClientSecret);

/// <summary>Requisição de renovação de token (espelha o contrato do backend — R4.4).</summary>
internal sealed record RefreshRequest(string RefreshToken);

/// <summary>Par de tokens retornado pelo backend (espelha o contrato — R4.1/R4.4).</summary>
internal sealed record TokenPairResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

/// <summary>Impressora monitorável na resposta de configuração (espelha o contrato do backend — Fase 4).</summary>
internal sealed record MonitoredPrinterResponse(Guid PrinterId, string Ip, string? Protocolo, int? Porta, string? Fabricante);

/// <summary>Resposta de configuração do agente (espelha o contrato do backend — R15.2; Fase 4).</summary>
internal sealed record ConfigResponse(
    int CollectionIntervalSeconds,
    IReadOnlyList<string> DiscoveryTargets,
    IReadOnlyList<string> IgnoredPrinters,
    IReadOnlyList<MonitoredPrinterResponse> MonitoredPrinters);

/// <summary>
/// Implementação de <see cref="IBackendClient"/> sobre <see cref="HttpClient"/>
/// (Fase 4 — R1.2/R1.6). Gerencia o par de tokens do agente internamente: autentica
/// por <c>client_id</c>/<c>client_secret</c> (<see cref="AgentOptions"/>, já
/// assumidos provisionados — o fluxo de registro não faz parte desta fase), renova
/// antes de expirar, e reautentica do zero se a renovação falhar. Uma única
/// tentativa de reautenticação + retry acontece quando uma chamada de negócio
/// recebe 401. Erros de rede/5xx retornam falha (<c>false</c>/<c>null</c>) sem
/// lançar, para o chamador decidir retry (R5.4/R5.5, já vigentes). Nenhum segredo é
/// logado (<see cref="SecretRedactor"/>).
/// </summary>
public sealed class HttpBackendClient : IBackendClient
{
    // A API usa a convenção camelCase padrão do ASP.NET Core (System.Text.Json);
    // sem isto, o deserializador (case-sensitive por padrão) não preencheria os
    // registros locais (PascalCase) a partir do JSON da resposta.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AgentOptions _options;
    private readonly ILogger<HttpBackendClient> _logger;
    private readonly SemaphoreSlim _authGate = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public HttpBackendClient(HttpClient http, IOptions<AgentOptions> options, ILogger<HttpBackendClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct)
    {
        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            using var request = BuildRequest(HttpMethod.Post, "api/v1/client/collect", payloadJson);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized && await ReauthenticateAsync(ct).ConfigureAwait(false))
            {
                using var retryRequest = BuildRequest(HttpMethod.Post, "api/v1/client/collect", payloadJson);
                using var retryResponse = await _http.SendAsync(retryRequest, ct).ConfigureAwait(false);
                return retryResponse.IsSuccessStatusCode;
            }

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de rede ao submeter coleta {Key}.", idempotencyKey);
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout ao submeter coleta {Key}.", idempotencyKey);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<AgentConfig?> GetConfigAsync(CancellationToken ct)
    {
        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            var response = await GetConfigResponseAsync(ct).ConfigureAwait(false);
            if (response is { StatusCode: HttpStatusCode.Unauthorized } && await ReauthenticateAsync(ct).ConfigureAwait(false))
            {
                response = await GetConfigResponseAsync(ct).ConfigureAwait(false);
            }

            if (response is not { IsSuccessStatusCode: true })
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ConfigResponse>(JsonOptions, ct).ConfigureAwait(false);
            return body is null
                ? null
                : new AgentConfig(
                    body.CollectionIntervalSeconds,
                    body.DiscoveryTargets,
                    body.IgnoredPrinters,
                    body.MonitoredPrinters
                        .Select(p => new AgentMonitoredPrinter(p.PrinterId, p.Ip, p.Protocolo, p.Porta, p.Fabricante))
                        .ToList());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de rede ao obter configuração.");
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout ao obter configuração.");
            return null;
        }
    }

    private async Task<HttpResponseMessage> GetConfigResponseAsync(CancellationToken ct)
    {
        using var request = BuildRequest(HttpMethod.Get, "api/v1/client/config", body: null);
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    /// <summary>
    /// Garante um token de acesso válido: renova (ou autentica do zero) quando
    /// ausente ou a menos de 30s de expirar. Protegido por semáforo — chamado
    /// concorrentemente pelo <c>Net_Monitoring_Service</c> e pelo
    /// <c>Communication_Service</c>.
    /// </summary>
    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return true;
        }

        await _authGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return true;
            }

            if (_refreshToken is not null && await TryRefreshAsync(ct).ConfigureAwait(false))
            {
                return true;
            }

            return await TryAuthenticateAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _authGate.Release();
        }
    }

    private async Task<bool> ReauthenticateAsync(CancellationToken ct)
    {
        await _authGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _accessToken = null;
            return await TryAuthenticateAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _authGate.Release();
        }
    }

    private async Task<bool> TryAuthenticateAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http
                .PostAsJsonAsync(
                    "api/v1/client/token",
                    new TokenRequest(_options.ClientId, _options.ClientSecret),
                    JsonOptions,
                    ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Falha ao autenticar o agente (status {Status}).", (int)response.StatusCode);
                return false;
            }

            var pair = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, ct).ConfigureAwait(false);
            return ApplyTokenPair(pair);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de rede ao autenticar o agente.");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Resposta de autenticação inválida.");
            return false;
        }
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http
                .PostAsJsonAsync("api/v1/client/token/refresh", new RefreshRequest(_refreshToken!), JsonOptions, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var pair = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, ct).ConfigureAwait(false);
            return ApplyTokenPair(pair);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool ApplyTokenPair(TokenPairResponse? pair)
    {
        if (pair is null)
        {
            return false;
        }

        _accessToken = pair.AccessToken;
        _refreshToken = pair.RefreshToken;
        _accessTokenExpiresAt = pair.AccessTokenExpiresAt;
        return true;
    }
}
