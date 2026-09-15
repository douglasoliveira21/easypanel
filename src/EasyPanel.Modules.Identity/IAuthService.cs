using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Fluxos de autenticação de sessão (R2, R4, R7.5): login, renovação (refresh)
/// e logout. A implementação orquestra o ASP.NET Core Identity
/// (validação de senha + lockout) e o <see cref="ITokenService"/> (emissão e
/// rotação de tokens), devolvendo sempre um <see cref="Result"/>/<see cref="Result{T}"/>
/// para que a camada de API mapeie falhas em códigos HTTP (401/423) sem vazar
/// informação sensível.
///
/// <para><b>Não-vazamento (R2.2).</b> Credenciais inválidas — seja por email
/// desconhecido, senha incorreta ou conta inativa — produzem o mesmo erro
/// genérico <see cref="AuthErrors.InvalidCredentials"/>. O estado de bloqueio
/// (<see cref="AuthErrors.LockedOut"/>) é o único distinto, pois R4.4 exige
/// informar o bloqueio.</para>
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Autentica por email e senha. Em caso de sucesso, zera o contador de falhas
    /// (R4.5), resolve os papéis do usuário e emite um <see cref="TokenPair"/>
    /// (JWT de acesso + refresh token). Falhas de credencial retornam
    /// <see cref="AuthErrors.InvalidCredentials"/> (genérico — R2.2) e contam para
    /// o lockout (R4.2/R4.3); conta bloqueada retorna
    /// <see cref="AuthErrors.LockedOut"/> (R4.4); conta inativa é recusada (R7.5).
    /// </summary>
    ///
    /// <para><b>Ramificação de MFA (R2.9/R2.10).</b> Após validar as credenciais e
    /// antes de emitir tokens, o fluxo verifica <see cref="ApplicationUser.MfaEnabled"/>:
    /// usuários sem MFA recebem o <see cref="TokenPair"/> normalmente (fluxo
    /// inalterado — R2.10); usuários com MFA <b>não</b> recebem tokens nesta etapa,
    /// e sim um <see cref="MfaChallenge"/> (ticket de curta duração) que deve ser
    /// completado via <see cref="VerifyMfaAsync"/> (R2.9). Ambos os desfechos são
    /// devolvidos como um <see cref="LoginResult"/> de sucesso.</para>
    /// <param name="email">Email informado (será normalizado).</param>
    /// <param name="password">Senha em texto claro para validação.</param>
    /// <param name="ip">Endereço IP de origem, para fins de auditoria (R4.1).</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<LoginResult>> LoginAsync(string email, string password, string? ip, CancellationToken ct);

    /// <summary>
    /// Completa a autenticação de segundo fator (R2.9): valida o <c>mfa_ticket</c>
    /// de curta duração emitido no login (assinatura, propósito e expiração),
    /// resolve o usuário desafiado e delega a verificação do código ao
    /// <see cref="IMfaValidator"/>. Em caso de sucesso — usuário ainda ativo e
    /// código aceito — emite o <see cref="TokenPair"/> pelo mesmo caminho do login
    /// normal. Ticket inválido/expirado retorna <see cref="AuthErrors.InvalidMfaTicket"/>;
    /// código rejeitado retorna <see cref="AuthErrors.InvalidMfaCode"/> (ambos → HTTP 401).
    /// </summary>
    /// <param name="mfaTicket">Ticket de segundo fator devolvido pelo login.</param>
    /// <param name="code">Código do segundo fator informado pelo usuário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<TokenPair>> VerifyMfaAsync(string mfaTicket, string code, CancellationToken ct);

    /// <summary>
    /// Renova a sessão a partir do valor bruto de um refresh token: valida e
    /// rotaciona o token (R2.4/R2.5) e, se o usuário dono ainda estiver ativo,
    /// emite um novo <see cref="TokenPair"/>. Token inválido/expirado/revogado, ou
    /// usuário inexistente/inativo, retorna <see cref="AuthErrors.InvalidRefreshToken"/>
    /// (mapeado para HTTP 401).
    /// </summary>
    Task<Result<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct);

    /// <summary>
    /// Encerra a sessão associada ao refresh token informado, revogando-o (R2.3).
    /// É idempotente: um token desconhecido, expirado ou já revogado ainda produz
    /// sucesso, sem revelar seu estado.
    /// </summary>
    Task<Result> LogoutAsync(string refreshToken, CancellationToken ct);

    /// <summary>
    /// Inicia a recuperação de senha para o email informado (R3.1/R3.2).
    ///
    /// <para><b>Não-vazamento (R3.2).</b> Retorna <b>sempre</b> sucesso,
    /// independentemente de o email existir ou de a conta estar ativa, para não
    /// revelar a existência da conta. Quando — e somente quando — o email pertence
    /// a uma conta existente e ativa, gera um token de redefinição de uso único com
    /// validade máxima de 60 minutos (R3.1) e o entrega via
    /// <see cref="IPasswordResetNotifier"/>. Para emails desconhecidos ou contas
    /// inativas, nenhum token é gerado e o notificador não é acionado, mas o
    /// resultado é idêntico.</para>
    /// </summary>
    /// <param name="email">Email informado na solicitação de recuperação.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> ForgotPasswordAsync(string email, CancellationToken ct);

    /// <summary>
    /// Conclui a redefinição de senha a partir de um token de redefinição (R3.3/R3.4).
    ///
    /// <para>Localiza a conta pelo email e valida o token; se válido, não expirado e
    /// a nova senha atender à política (R3.7), atualiza o hash da senha. O token é de
    /// uso único: uma redefinição bem-sucedida altera o <c>SecurityStamp</c> do
    /// usuário, invalidando o token apresentado para usos subsequentes (R3.3). Em
    /// caso de sucesso, revoga todos os refresh tokens ativos do usuário (R3.4).
    /// Token/senha inválidos retornam <see cref="AuthErrors.InvalidResetToken"/> ou
    /// <see cref="AuthErrors.WeakPassword"/> (HTTP 400), sem revelar existência de
    /// conta além do que o token pressupõe.</para>
    /// </summary>
    /// <param name="email">Email da conta cuja senha será redefinida.</param>
    /// <param name="resetToken">Token de redefinição de uso único.</param>
    /// <param name="newPassword">Nova senha, sujeita à política (R3.7).</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> ResetPasswordAsync(string email, string resetToken, string newPassword, CancellationToken ct);

    /// <summary>
    /// Altera a senha de um usuário autenticado que informa a senha atual (R3.5/R3.6).
    ///
    /// <para>Valida a senha atual e, se correta e a nova senha atender à política
    /// (R3.7), atualiza o hash. Senha atual incorreta retorna
    /// <see cref="AuthErrors.IncorrectCurrentPassword"/> (R3.6) e a senha permanece
    /// inalterada. Em caso de sucesso, revoga todos os refresh tokens ativos do
    /// usuário (R3.4).</para>
    /// </summary>
    /// <param name="userId">Identificador do usuário autenticado (do claim <c>sub</c>).</param>
    /// <param name="currentPassword">Senha atual, para confirmação (R3.6).</param>
    /// <param name="newPassword">Nova senha, sujeita à política (R3.7).</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
}
