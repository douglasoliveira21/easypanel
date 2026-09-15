using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EasyPanel.WindowsClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes do <see cref="HttpBackendClient"/> (Task 6.1 — R1.2/R1.6): autenticação
/// inicial, renovação de token antes de expirar, reautenticação após 401 com
/// retry, e falha de rede tratada sem lançar.
/// </summary>
public sealed class HttpBackendClientTests
{
    private static readonly AgentOptions Options = new()
    {
        BackendBaseUrl = "https://backend.example.com/",
        ClientId = "client-1",
        ClientSecret = "s3cr3t",
    };

    [Fact]
    public async Task GetConfig_AuthenticatesFirst_ThenCallsConfig_WithBearerToken()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(req =>
        {
            Assert.EndsWith("client/token", req.RequestUri!.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, TokenPair("access-1", "refresh-1", 300));
        });
        handler.Enqueue(req =>
        {
            Assert.EndsWith("client/config", req.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("access-1", req.Headers.Authorization!.Parameter);
            return JsonResponse(HttpStatusCode.OK, ConfigBody());
        });

        var client = CreateClient(handler);
        var config = await client.GetConfigAsync(CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal(300, config!.CollectionIntervalSeconds);
        Assert.Single(config.MonitoredPrinters);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetConfig_TokenNearExpiry_RefreshesBeforeNextCall()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK, TokenPair("access-1", "refresh-1", 5))); // quase expirado
        handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK, ConfigBody()));
        handler.Enqueue(req =>
        {
            Assert.EndsWith("client/token/refresh", req.RequestUri!.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, TokenPair("access-2", "refresh-2", 300));
        });
        handler.Enqueue(req =>
        {
            Assert.Equal("access-2", req.Headers.Authorization!.Parameter);
            return JsonResponse(HttpStatusCode.OK, ConfigBody());
        });

        var client = CreateClient(handler);
        await client.GetConfigAsync(CancellationToken.None);
        var second = await client.GetConfigAsync(CancellationToken.None);

        Assert.NotNull(second);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task SubmitCollection_Returns401_Reauthenticates_AndRetries()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK, TokenPair("access-1", "refresh-1", 300)));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK, TokenPair("access-2", "refresh-2", 300)));
        handler.Enqueue(req =>
        {
            Assert.Equal("access-2", req.Headers.Authorization!.Parameter);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        var client = CreateClient(handler);
        var ok = await client.SubmitCollectionAsync("key-1", "{}", CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task SubmitCollection_NetworkFailure_ReturnsFalse_WithoutThrowing()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK, TokenPair("access-1", "refresh-1", 300)));
        handler.Enqueue(_ => throw new HttpRequestException("connection refused"));

        var client = CreateClient(handler);
        var ok = await client.SubmitCollectionAsync("key-1", "{}", CancellationToken.None);

        Assert.False(ok);
    }

    private static HttpBackendClient CreateClient(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Options.BackendBaseUrl) };
        return new HttpBackendClient(httpClient, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<HttpBackendClient>.Instance);
    }

    private static object TokenPair(string access, string refresh, int expiresInSeconds) => new
    {
        accessToken = access,
        refreshToken = refresh,
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
    };

    private static object ConfigBody() => new
    {
        collectionIntervalSeconds = 300,
        discoveryTargets = Array.Empty<string>(),
        ignoredPrinters = Array.Empty<string>(),
        monitoredPrinters = new[]
        {
            new { printerId = Guid.NewGuid(), ip = "10.0.0.5", protocolo = (string?)null, porta = (int?)null, fabricante = (string?)null },
        },
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();

        public int CallCount { get; private set; }

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handlers.Enqueue(handler);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_handlers.Count == 0)
            {
                throw new InvalidOperationException("Nenhuma resposta programada para a chamada.");
            }

            return Task.FromResult(_handlers.Dequeue()(request));
        }
    }
}
