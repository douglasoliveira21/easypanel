using EasyPanel.Modules.Identity;
using Microsoft.Extensions.Logging;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação padrão de <see cref="IPasswordResetNotifier"/> para a Fase 1, na
/// ausência de um subsistema de email/notificação (pertence a uma fase futura).
///
/// <para><b>Proteção de segredos (R11.2).</b> Registra apenas o fato de que uma
/// redefinição de senha foi solicitada para um usuário (por Id), <b>sem</b> incluir
/// o valor do token de redefinição nem qualquer dado sensível. O parâmetro do
/// token é intencionalmente ignorado para o log; a entrega efetiva por email será
/// implementada quando o canal de notificação existir.</para>
/// </summary>
public sealed class LogOnlyPasswordResetNotifier : IPasswordResetNotifier
{
    private readonly ILogger<LogOnlyPasswordResetNotifier> _logger;

    /// <summary>Cria o notificador com o logger da aplicação.</summary>
    public LogOnlyPasswordResetNotifier(ILogger<LogOnlyPasswordResetNotifier> logger) =>
        _logger = logger;

    /// <inheritdoc />
    public Task SendAsync(ApplicationUser user, string resetToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        // NUNCA logar o token de redefinição (R11.2). Apenas o fato do pedido.
        _logger.LogInformation(
            "Redefinição de senha solicitada para o usuário {UserId}.",
            user.Id);

        return Task.CompletedTask;
    }
}
