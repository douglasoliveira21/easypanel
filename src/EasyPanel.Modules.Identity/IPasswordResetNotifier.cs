namespace EasyPanel.Modules.Identity;

/// <summary>
/// Seam de entrega do token de redefinição de senha (R3.1).
///
/// <para>O fluxo de recuperação de senha (<see cref="IAuthService.ForgotPasswordAsync"/>)
/// gera um token de redefinição de uso único e o entrega ao usuário por um canal
/// fora de banda (tipicamente email). Como a Fase 1 ainda não possui um subsistema
/// de notificação/email (pertence a uma fase futura), este contrato isola essa
/// responsabilidade: o serviço de autenticação apenas invoca o notificador quando
/// o usuário existe e está ativo, sem conhecer o mecanismo concreto de entrega.</para>
///
/// <para><b>Não-vazamento (R3.2).</b> O notificador é chamado <b>somente</b> para
/// contas existentes e ativas; para emails desconhecidos ou contas inativas ele
/// não é acionado, mas a resposta ao chamador permanece idêntica — o notificador
/// não participa da decisão de resposta.</para>
///
/// <para><b>Proteção de segredos (R11.2).</b> Implementações não devem registrar o
/// valor do <c>resetToken</c> em logs de produção; devem, no máximo, registrar que
/// uma redefinição foi solicitada, sem o token nem dados sensíveis.</para>
/// </summary>
public interface IPasswordResetNotifier
{
    /// <summary>
    /// Entrega o token de redefinição ao usuário (ex.: por email). Invocado apenas
    /// para contas existentes e ativas (R3.1/R3.2).
    /// </summary>
    /// <param name="user">Usuário-alvo da redefinição (conta existente e ativa).</param>
    /// <param name="resetToken">Token de redefinição de uso único; nunca deve ser logado (R11.2).</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task SendAsync(ApplicationUser user, string resetToken, CancellationToken ct);
}
