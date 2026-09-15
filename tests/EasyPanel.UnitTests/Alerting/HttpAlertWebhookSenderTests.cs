using System.Net;
using System.Security.Cryptography;
using System.Text;
using EasyPanel.Infrastructure.Alerting;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.UnitTests.Alerting;

/// <summary>
/// Testes do <see cref="HttpAlertWebhookSender"/> (Task 6.2 — R5.2, R5.4, R5.6):
/// assinatura HMAC correta, tratamento de resposta não-2xx como falha, e ausência
/// do segredo em qualquer saída observável (mensagem de erro).
/// </summary>
public sealed class HttpAlertWebhookSenderTests
{
    private static readonly AlertWebhookMessage Message = new(
        "https://example.com/hook",
        "segredo-super-secreto",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Critica",
        Guid.NewGuid(),
        null,
        DateTimeOffset.UtcNow,
        "Falha de coleta na impressora X.");

    [Fact]
    public async Task SendAsync_Success_SignsBody_AndReturnsOk()
    {
        string? capturedSignature = null;
        string? capturedBody = null;

        var handler = new FakeHttpMessageHandler(async request =>
        {
            capturedSignature = request.Headers.GetValues("X-EasyPanel-Signature").Single();
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var sender = new HttpAlertWebhookSender(new HttpClient(handler), NullLogger<HttpAlertWebhookSender>.Instance);

        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.NotNull(capturedBody);

        var expectedSignature = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Message.Secret), Encoding.UTF8.GetBytes(capturedBody!)));
        Assert.Equal(expectedSignature, capturedSignature);

        // O segredo nunca aparece no corpo enviado.
        Assert.DoesNotContain(Message.Secret, capturedBody);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatusCode_IsFailure_WithoutLeakingSecret()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var sender = new HttpAlertWebhookSender(new HttpClient(handler), NullLogger<HttpAlertWebhookSender>.Instance);

        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(500, result.HttpStatusCode);
        Assert.DoesNotContain(Message.Secret, result.ErrorSummary);
    }

    [Fact]
    public async Task SendAsync_ConnectionFailure_IsFailure_WithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var sender = new HttpAlertWebhookSender(new HttpClient(handler), NullLogger<HttpAlertWebhookSender>.Instance);

        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorSummary);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
