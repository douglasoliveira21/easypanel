using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IAuthService"/> sobre o ASP.NET Core Identity
/// (<see cref="UserManager{TUser}"/>) e o <see cref="ITokenService"/> (R2, R4, R7.5).
///
/// <para><b>Lockout explícito (R4.2–R4.5).</b> Como o registro de Identity usa
/// <c>AddIdentityCore</c> sem <c>SignInManager</c>, o fluxo de senha/lockout é
/// conduzido diretamente pelo <see cref="UserManager{TUser}"/>:
/// <see cref="UserManager{TUser}.IsLockedOutAsync"/> antes de validar,
/// <see cref="UserManager{TUser}.CheckPasswordAsync"/> para conferir a senha,
/// <see cref="UserManager{TUser}.AccessFailedAsync"/> em cada falha (incrementa o
/// contador e bloqueia ao atingir 5 — R4.3) e
/// <see cref="UserManager{TUser}.ResetAccessFailedCountAsync"/> no sucesso (R4.5).</para>
///
/// <para><b>Não-vazamento (R2.2/R7.5).</b> Email desconhecido, senha incorreta e
/// conta inativa retornam o mesmo <see cref="AuthErrors.InvalidCredentials"/>. Só
/// o bloqueio é distinto (<see cref="AuthErrors.LockedOut"/>), pois R4.4 exige
/// informá-lo.</para>
///
/// <para><b>MFA (tarefa 3.4 / R2.9, R2.10).</b> Após validar as credenciais e
/// antes de emitir tokens, <see cref="LoginAsync"/> ramifica em
/// <see cref="ApplicationUser.MfaEnabled"/>: usuários sem MFA seguem o fluxo
/// inalterado e recebem os tokens (R2.10); usuários com MFA recebem um
/// <see cref="MfaChallenge"/> (ticket de curta duração via
/// <see cref="IMfaTicketService"/>) sem token de sessão, que é completado por
/// <see cref="VerifyMfaAsync"/> com o auxílio do <see cref="IMfaValidator"/>
/// (R2.9). Na Fase 1 o validador padrão falha fechado, de modo que a estrutura
/// existe sem abrir bypass.</para>
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IMfaTicketService _mfaTicketService;
    private readonly IMfaValidator _mfaValidator;
    private readonly IPasswordResetNotifier _passwordResetNotifier;
    private readonly IAuditLogger _auditLogger;
    private readonly JwtOptions _jwtOptions;

    /// <summary>
    /// Cria o serviço com o gerenciador de usuários, o serviço de tokens, o serviço
    /// de tickets de MFA, o validador de segundo fator, o notificador de redefinição
    /// de senha, o logger de auditoria e as opções de JWT.
    /// </summary>
    public AuthService(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IMfaTicketService mfaTicketService,
        IMfaValidator mfaValidator,
        IPasswordResetNotifier passwordResetNotifier,
        IAuditLogger auditLogger,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _mfaTicketService = mfaTicketService;
        _mfaValidator = mfaValidator;
        _passwordResetNotifier = passwordResetNotifier;
        _auditLogger = auditLogger;
        _jwtOptions = jwtOptions.Value;
    }

    /// <inheritdoc />
    public async Task<Result<LoginResult>> LoginAsync(
        string email,
        string password,
        string? ip,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Email desconhecido → erro genérico (não revela ausência de conta — R2.2).
        // Registra a tentativa como falha sem ator atribuível (R4.1/R10.2): não há
        // conta a que vincular o evento. A senha jamais é incluída no registro
        // (R11.2).
        var user = email is null ? null : await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            await AuditLoginAsync(actorUserId: null, tenantId: null, AuditResult.Failure, ip, ct)
                .ConfigureAwait(false);
            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        // Conta já bloqueada por tentativas anteriores (R4.4). A tentativa é
        // atribuível ao usuário conhecido (R4.1).
        if (await _userManager.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            await AuditLoginAsync(user.Id, user.TenantId, AuditResult.Failure, ip, ct).ConfigureAwait(false);
            return Result.Failure<LoginResult>(AuthErrors.LockedOut);
        }

        // Conta inativa: recusa autenticação (R7.5) sem distinguir de credencial
        // inválida (não-vazamento).
        if (!user.IsActive)
        {
            await AuditLoginAsync(user.Id, user.TenantId, AuditResult.Failure, ip, ct).ConfigureAwait(false);
            return Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        // Validação de senha com contagem de falhas e lockout (R4.2/R4.3).
        if (!await _userManager.CheckPasswordAsync(user, password).ConfigureAwait(false))
        {
            await _userManager.AccessFailedAsync(user).ConfigureAwait(false);

            // Registra a falha atribuída à conta conhecida (R4.1/R10.2).
            await AuditLoginAsync(user.Id, user.TenantId, AuditResult.Failure, ip, ct).ConfigureAwait(false);

            // Se a falha atual disparou o bloqueio, informa o estado (R4.4);
            // caso contrário, erro genérico (R2.2).
            return await _userManager.IsLockedOutAsync(user).ConfigureAwait(false)
                ? Result.Failure<LoginResult>(AuthErrors.LockedOut)
                : Result.Failure<LoginResult>(AuthErrors.InvalidCredentials);
        }

        // Sucesso na etapa de senha: zera o contador de falhas (R4.5).
        await _userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false);

        // Login bem-sucedido em nível de credenciais é auditado como sucesso, ainda
        // que um segundo fator seja exigido em seguida (R4.1/R10.2): a etapa de
        // senha foi concluída e o ator/tenant são conhecidos.
        await AuditLoginAsync(user.Id, user.TenantId, AuditResult.Success, ip, ct).ConfigureAwait(false);

        // Ramificação de segundo fator (R2.9/R2.10): usuários com MFA habilitado
        // NÃO recebem tokens agora; recebem um desafio (mfa_ticket de curta
        // duração) a ser completado em VerifyMfaAsync. Usuários sem MFA seguem o
        // fluxo inalterado e recebem o par de tokens (R2.10).
        if (user.MfaEnabled)
        {
            var challenge = _mfaTicketService.IssueTicket(user.Id);
            return Result.Success(LoginResult.ForMfaChallenge(challenge));
        }

        var tokens = await IssueTokensAsync(user, ct).ConfigureAwait(false);
        return Result.Success(LoginResult.ForTokens(tokens));
    }

    /// <inheritdoc />
    public async Task<Result<TokenPair>> VerifyMfaAsync(string mfaTicket, string code, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Valida o ticket (assinatura, propósito e expiração) e resolve o usuário
        // desafiado (R2.9). Ticket inválido/expirado → 401.
        var ticket = await _mfaTicketService.ValidateTicketAsync(mfaTicket, ct).ConfigureAwait(false);
        if (ticket.IsFailure)
        {
            return Result.Failure<TokenPair>(AuthErrors.InvalidMfaTicket);
        }

        var user = await _userManager.FindByIdAsync(ticket.Value.ToString()).ConfigureAwait(false);

        // Usuário inexistente ou inativo desde a emissão do ticket: recusa sem
        // revelar a causa (não-vazamento; R7.5).
        if (user is null || !user.IsActive)
        {
            return Result.Failure<TokenPair>(AuthErrors.InvalidMfaTicket);
        }

        // Verificação do segundo fator delegada ao validador. Na Fase 1 o padrão
        // falha fechado, mantendo R2.9 honesto até haver um provedor TOTP real.
        if (!await _mfaValidator.ValidateAsync(user, code, ct).ConfigureAwait(false))
        {
            return Result.Failure<TokenPair>(AuthErrors.InvalidMfaCode);
        }

        // Segundo fator aceito: emite os tokens pelo mesmo caminho do login normal.
        var tokens = await IssueTokensAsync(user, ct).ConfigureAwait(false);
        return Result.Success(tokens);
    }

    /// <inheritdoc />
    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Valida e rotaciona o refresh token (R2.4/R2.5). Falha → 401.
        var rotation = await _tokenService.ValidateAndRotateAsync(refreshToken, ct).ConfigureAwait(false);
        if (rotation.IsFailure)
        {
            return Result.Failure<TokenPair>(AuthErrors.InvalidRefreshToken);
        }

        var rotated = rotation.Value;

        // O usuário dono do token precisa continuar existindo e ativo (R7.5).
        var user = await _userManager
            .FindByIdAsync(rotated.Token.UserId.ToString())
            .ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            // Revoga o token rotacionado recém-emitido: sessão não deve continuar.
            await _tokenService.RevokeAllForUserAsync(rotated.Token.UserId, ct).ConfigureAwait(false);
            return Result.Failure<TokenPair>(AuthErrors.InvalidRefreshToken);
        }

        var roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
        var accessToken = _tokenService.CreateAccessToken(user, roles, Array.Empty<string>());

        return Result.Success(BuildPair(accessToken, rotated.RawToken, rotated.ExpiresAt));
    }

    /// <inheritdoc />
    public async Task<Result> LogoutAsync(string refreshToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Revoga apenas o token da sessão (R2.3); idempotente e silencioso.
        await _tokenService.RevokeAsync(refreshToken, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ForgotPasswordAsync(string email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Não-vazamento (R3.2): a resposta é sempre sucesso, exista ou não a conta.
        // O token só é gerado/entregue para contas existentes e ativas.
        var user = email is null ? null : await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is not null && user.IsActive)
        {
            // Token de uso único com validade ≤ 60 min (R3.1). A validade é
            // configurada globalmente no provedor de tokens padrão do Identity
            // (DataProtectionTokenProviderOptions.TokenLifespan = 60 min); a
            // unicidade de uso decorre da mudança do SecurityStamp na redefinição.
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

            // Entrega fora de banda (email). O notificador nunca loga o token (R11.2).
            await _passwordResetNotifier.SendAsync(user, resetToken, ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ResetPasswordAsync(
        string email,
        string resetToken,
        string newPassword,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Conta inexistente: falha genérica (progredir exigiria um token válido).
        var user = email is null ? null : await _userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(AuthErrors.InvalidResetToken);
        }

        // ResetPasswordAsync valida o token (assinatura/propósito/expiração e o
        // SecurityStamp atual — daí o uso único) e aplica a política de senha (R3.7).
        var result = await _userManager
            .ResetPasswordAsync(user, resetToken, newPassword)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Result.Failure(MapIdentityFailure(result));
        }

        // Toda troca de senha revoga as sessões ativas do usuário (R3.4).
        await _tokenService.RevokeAllForUserAsync(user.Id, ct).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            // Sem usuário para o id autenticado: trata como senha atual incorreta,
            // sem revelar detalhes (R3.6).
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
        }

        // ChangePasswordAsync confere a senha atual (R3.6) e aplica a política à
        // nova senha (R3.7). Em sucesso, o Identity atualiza o SecurityStamp.
        var result = await _userManager
            .ChangePasswordAsync(user, currentPassword, newPassword)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Result.Failure(MapIdentityFailure(result));
        }

        // Toda troca de senha revoga as sessões ativas do usuário (R3.4).
        await _tokenService.RevokeAllForUserAsync(user.Id, ct).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Traduz uma falha do Identity em um <see cref="Error"/> de domínio, sem
    /// vazar detalhes sensíveis. Falhas de política de senha viram
    /// <see cref="AuthErrors.WeakPassword"/> (R3.7); senha atual incorreta vira
    /// <see cref="AuthErrors.IncorrectCurrentPassword"/> (R3.6); as demais (token
    /// inválido/expirado) viram <see cref="AuthErrors.InvalidResetToken"/> (R3.3).
    /// </summary>
    private static Error MapIdentityFailure(IdentityResult result)
    {
        var codes = result.Errors.Select(e => e.Code).ToArray();

        if (codes.Contains("PasswordMismatch", StringComparer.Ordinal))
        {
            return AuthErrors.IncorrectCurrentPassword;
        }

        // Códigos de política de senha do Identity começam com "Password"
        // (PasswordTooShort, PasswordRequiresDigit, PasswordRequiresUpper, etc.).
        if (codes.Any(c => c.StartsWith("Password", StringComparison.Ordinal)))
        {
            return AuthErrors.WeakPassword;
        }

        // Restante (InvalidToken e afins): token de redefinição inválido/expirado.
        return AuthErrors.InvalidResetToken;
    }

    /// <summary>
    /// Registra um evento de login na trilha de auditoria (R4.1/R10.2): resultado
    /// (sucesso/falha), horário (carimbado pelo logger), IP de origem e — quando a
    /// tentativa é atribuível a uma conta conhecida — o ator e seu tenant. A senha
    /// e quaisquer tokens jamais são incluídos no registro (R11.2).
    /// </summary>
    private Task AuditLoginAsync(
        Guid? actorUserId,
        Guid? tenantId,
        AuditResult result,
        string? ip,
        CancellationToken ct)
    {
        var entry = new AuditEntry
        {
            ActorUserId = actorUserId,
            TenantId = tenantId,
            Action = AuditActions.AuthLogin,
            ResourceType = AuditActions.AuthResourceType,
            ResourceId = actorUserId?.ToString(),
            Ip = ip,
            Result = result,
        };

        return _auditLogger.LogAsync(entry, ct);
    }

    private async Task<TokenPair> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
        var accessToken = _tokenService.CreateAccessToken(user, roles, Array.Empty<string>());
        var issued = await _tokenService.IssueRefreshTokenAsync(user.Id, ct).ConfigureAwait(false);

        return BuildPair(accessToken, issued.RawToken, issued.ExpiresAt);
    }

    private TokenPair BuildPair(string accessToken, string rawRefreshToken, DateTimeOffset refreshExpiresAt) =>
        new(
            accessToken,
            rawRefreshToken,
            // Espelha a validade configurada do access token (R2.6). A expiração
            // efetiva é gravada no próprio JWT pelo ITokenService.
            DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
            refreshExpiresAt);
}
