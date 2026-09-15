using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EasyPanel.UnitTests.Identity;

/// <summary>
/// Unit tests do <b>branch de decisão MFA</b> do <see cref="AuthService"/>
/// (Task 3.4 / R2.9, R2.10). Exercitam diretamente a ramificação
/// <c>MfaEnabled</c> do login e o fluxo de verificação de segundo fator, com
/// dublês em memória para <see cref="UserManager{TUser}"/>,
/// <see cref="ITokenService"/>, <see cref="IMfaTicketService"/> e
/// <see cref="IMfaValidator"/> — sem depender de banco de dados.
///
/// Cobre:
/// <list type="bullet">
///   <item>Usuário sem MFA → <see cref="LoginResult"/> com <see cref="TokenPair"/>,
///   sem desafio (fluxo inalterado — R2.10).</item>
///   <item>Usuário com MFA → <see cref="LoginResult"/> com <see cref="MfaChallenge"/>,
///   sem tokens de sessão (R2.9).</item>
///   <item><see cref="AuthService.VerifyMfaAsync"/> com ticket válido e código
///   aceito → <see cref="TokenPair"/> emitido.</item>
///   <item><see cref="AuthService.VerifyMfaAsync"/> com ticket inválido, ou código
///   rejeitado → falha, sem tokens.</item>
/// </list>
/// </summary>
public class AuthServiceMfaBranchTests
{
    private const string Password = "Str0ng!Passw0rd";

    // ---- Login: usuário sem MFA segue o fluxo inalterado (R2.10) -----------

    [Fact]
    public async Task Login_NonMfaUser_ReturnsTokensWithoutChallenge()
    {
        var user = CreateUser(mfaEnabled: false);
        var sut = CreateSut(out _, users: user);

        var result = await sut.LoginAsync(user.Email!, Password, ip: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.MfaRequired);
        Assert.NotNull(result.Value.Tokens);
        Assert.Null(result.Value.Challenge);
    }

    // ---- Login: usuário com MFA recebe desafio, sem tokens (R2.9) ----------

    [Fact]
    public async Task Login_MfaUser_ReturnsChallengeAndNoTokens()
    {
        var user = CreateUser(mfaEnabled: true);
        var sut = CreateSut(out var doubles, users: user);

        var result = await sut.LoginAsync(user.Email!, Password, ip: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.MfaRequired);
        Assert.Null(result.Value.Tokens);
        Assert.NotNull(result.Value.Challenge);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Challenge!.MfaTicket));

        // Nenhum refresh token de sessão foi emitido nesta etapa (R2.9).
        Assert.Equal(0, doubles.TokenService.IssuedRefreshTokenCount);
    }

    // ---- VerifyMfa: ticket válido + código aceito → tokens -----------------

    [Fact]
    public async Task VerifyMfa_WithValidTicketAndAcceptedCode_IssuesTokens()
    {
        var user = CreateUser(mfaEnabled: true);
        var sut = CreateSut(out var doubles, users: user);
        doubles.MfaValidator.AcceptedCode = "123456";

        var login = await sut.LoginAsync(user.Email!, Password, ip: null, CancellationToken.None);
        var ticket = login.Value.Challenge!.MfaTicket;

        var result = await sut.VerifyMfaAsync(ticket, "123456", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));
        Assert.Equal(1, doubles.TokenService.IssuedRefreshTokenCount);
    }

    // ---- VerifyMfa: código rejeitado → falha, sem tokens -------------------

    [Fact]
    public async Task VerifyMfa_WithRejectedCode_FailsWithoutTokens()
    {
        var user = CreateUser(mfaEnabled: true);
        var sut = CreateSut(out var doubles, users: user);
        doubles.MfaValidator.AcceptedCode = "123456";

        var login = await sut.LoginAsync(user.Email!, Password, ip: null, CancellationToken.None);
        var ticket = login.Value.Challenge!.MfaTicket;

        var result = await sut.VerifyMfaAsync(ticket, "000000", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.InvalidMfaCode.Code, result.Error.Code);
        Assert.Equal(0, doubles.TokenService.IssuedRefreshTokenCount);
    }

    // ---- VerifyMfa: ticket inválido → falha --------------------------------

    [Fact]
    public async Task VerifyMfa_WithInvalidTicket_Fails()
    {
        var user = CreateUser(mfaEnabled: true);
        var sut = CreateSut(out var doubles, users: user);
        doubles.MfaValidator.AcceptedCode = "123456";

        var result = await sut.VerifyMfaAsync("not-a-valid-ticket", "123456", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.InvalidMfaTicket.Code, result.Error.Code);
        Assert.Equal(0, doubles.TokenService.IssuedRefreshTokenCount);
    }

    // ---- Fábricas e dublês -------------------------------------------------

    private static ApplicationUser CreateUser(bool mfaEnabled) => new()
    {
        Id = Guid.NewGuid(),
        UserName = "user@tenant-a.example.com",
        Email = "user@tenant-a.example.com",
        TenantId = Guid.NewGuid(),
        IsActive = true,
        MfaEnabled = mfaEnabled,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static AuthService CreateSut(out TestDoubles doubles, params ApplicationUser[] users)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "unit-tests",
            Audience = "unit-tests",
            SigningKey = "unit-tests-signing-key-not-a-secret-32bytes!!",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14,
        });

        var userManager = FakeUserManager.Create(Password, users);
        var tokenService = new FakeTokenService();
        var ticketService = new MfaTicketService(jwtOptions);
        var mfaValidator = new FakeMfaValidator();
        var resetNotifier = new NoopPasswordResetNotifier();
        var auditLogger = new CapturingAuditLogger();

        doubles = new TestDoubles(tokenService, mfaValidator, auditLogger);

        return new AuthService(
            userManager,
            tokenService,
            ticketService,
            mfaValidator,
            resetNotifier,
            auditLogger,
            jwtOptions);
    }

    private sealed record TestDoubles(
        FakeTokenService TokenService,
        FakeMfaValidator MfaValidator,
        CapturingAuditLogger AuditLogger);

    /// <summary>Logger de auditoria em memória; captura as entradas registradas.</summary>
    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task LogAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Notificador de redefinição de senha no-op; não exercitado neste conjunto.</summary>
    private sealed class NoopPasswordResetNotifier : IPasswordResetNotifier
    {
        public Task SendAsync(ApplicationUser user, string resetToken, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>Validador de MFA que aceita apenas um código conhecido (default: nenhum).</summary>
    private sealed class FakeMfaValidator : IMfaValidator
    {
        public string? AcceptedCode { get; set; }

        public Task<bool> ValidateAsync(ApplicationUser user, string code, CancellationToken ct) =>
            Task.FromResult(AcceptedCode is not null && string.Equals(code, AcceptedCode, StringComparison.Ordinal));
    }

    /// <summary>Serviço de tokens em memória; conta emissões de refresh token.</summary>
    private sealed class FakeTokenService : ITokenService
    {
        public int IssuedRefreshTokenCount { get; private set; }

        public string CreateAccessToken(
            ApplicationUser user,
            IEnumerable<string> roles,
            IEnumerable<string> permissions) => $"access-token-{user.Id}";

        public Task<IssuedRefreshToken> IssueRefreshTokenAsync(Guid userId, CancellationToken ct)
        {
            IssuedRefreshTokenCount++;
            var entity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = "hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            return Task.FromResult(new IssuedRefreshToken($"refresh-{userId}", entity));
        }

        public Task<Result<IssuedRefreshToken>> ValidateAndRotateAsync(string token, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;

        public Task RevokeAsync(string rawToken, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="UserManager{TUser}"/> em memória para exercitar o branch de MFA
    /// sem banco: resolve usuários por email/id e valida a senha conhecida.
    /// </summary>
    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        private readonly Dictionary<string, ApplicationUser> _byEmail;
        private readonly Dictionary<string, ApplicationUser> _byId;
        private readonly string _knownPassword;

        private FakeUserManager(
            string knownPassword,
            IEnumerable<ApplicationUser> users,
            IUserStore<ApplicationUser> store)
            : base(
                store,
                optionsAccessor: null!,
                passwordHasher: null!,
                userValidators: Array.Empty<IUserValidator<ApplicationUser>>(),
                passwordValidators: Array.Empty<IPasswordValidator<ApplicationUser>>(),
                keyNormalizer: null!,
                errors: null!,
                services: null!,
                logger: null!)
        {
            _knownPassword = knownPassword;
            _byEmail = users.ToDictionary(u => u.Email!, StringComparer.OrdinalIgnoreCase);
            _byId = users.ToDictionary(u => u.Id.ToString(), StringComparer.Ordinal);
        }

        public static FakeUserManager Create(string knownPassword, IEnumerable<ApplicationUser> users) =>
            new(knownPassword, users, new NoopUserStore());

        public override Task<ApplicationUser?> FindByEmailAsync(string email) =>
            Task.FromResult(_byEmail.TryGetValue(email, out var user) ? user : null);

        public override Task<ApplicationUser?> FindByIdAsync(string userId) =>
            Task.FromResult(_byId.TryGetValue(userId, out var user) ? user : null);

        public override Task<bool> IsLockedOutAsync(ApplicationUser user) => Task.FromResult(false);

        public override Task<bool> CheckPasswordAsync(ApplicationUser user, string password) =>
            Task.FromResult(string.Equals(password, _knownPassword, StringComparison.Ordinal));

        public override Task<IdentityResult> AccessFailedAsync(ApplicationUser user) =>
            Task.FromResult(IdentityResult.Success);

        public override Task<IdentityResult> ResetAccessFailedCountAsync(ApplicationUser user) =>
            Task.FromResult(IdentityResult.Success);

        public override Task<IList<string>> GetRolesAsync(ApplicationUser user) =>
            Task.FromResult<IList<string>>(new List<string>());
    }

    /// <summary>Store mínimo exigido pela base <see cref="UserManager{TUser}"/>; não é exercitado.</summary>
    private sealed class NoopUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(
            ApplicationUser user,
            string? normalizedName,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
    }
}
