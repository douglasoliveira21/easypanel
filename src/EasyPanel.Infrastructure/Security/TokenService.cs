using System.Security.Cryptography;
using System.Text;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ITokenService"/> sobre o <see cref="AppDbContext"/>
/// e a biblioteca IdentityModel (R2.1, R2.4, R2.5, R2.6).
///
/// <para><b>JWT de acesso.</b> Assinado com chave simétrica (HMAC-SHA256) lida de
/// <see cref="JwtOptions"/>, com expiração ≤ 15 min (R2.6). Claims: <c>sub</c>,
/// <c>tenant_id</c> (quando houver), <c>jti</c>, papéis (como claims de papel) e
/// <c>perm_version</c>. A lista completa de permissões <b>não</b> é embutida (ver
/// <see cref="ITokenService"/>).</para>
///
/// <para><b>Refresh token.</b> Valor opaco de 256 bits (base64url) devolvido em
/// bruto ao chamador; o banco guarda apenas seu hash SHA-256 (base64url). A cada
/// validação bem-sucedida o token é rotacionado: o corrente é revogado
/// (<c>RevokedAt</c> + <c>ReplacedByTokenHash</c>) e um sucessor é emitido
/// (R2.4). Tokens desconhecidos, expirados ou revogados falham (R2.5).</para>
/// </summary>
public sealed class TokenService : ITokenService
{
    /// <summary>Nome do claim de papel embutido no JWT.</summary>
    public const string RolesClaimType = "roles";

    /// <summary>Nome do claim de versão de permissões embutido no JWT.</summary>
    public const string PermVersionClaimType = "perm_version";

    /// <summary>Nome do claim de tenant embutido no JWT.</summary>
    public const string TenantIdClaimType = "tenant_id";

    /// <summary>
    /// Nome do claim de Cliente (Customer) embutido no JWT (Fase 10 — Portal do
    /// Cliente). Presente apenas quando o usuário tem um <c>CustomerId</c>
    /// vinculado (papel <c>Cliente</c>).
    /// </summary>
    public const string CustomerIdClaimType = "customer_id";

    /// <summary>
    /// Valor placeholder do claim <c>perm_version</c> na Fase 1. A resolução
    /// efetiva de versão de permissões pertence à tarefa 4.2; o claim é mantido
    /// presente para que 4.2 possa dele depender sem alterar o token.
    /// </summary>
    private const string DefaultPermVersion = "0";

    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly AppDbContext _dbContext;
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    /// <summary>
    /// Cria o serviço com o contexto de persistência e as opções de JWT já
    /// validadas. A credencial de assinatura é derivada uma vez da chave
    /// simétrica configurada.
    /// </summary>
    public TokenService(AppDbContext dbContext, IOptions<JwtOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public string CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        // permissions é intencionalmente não embutido (ver decisão de projeto);
        // mantido na assinatura por conformidade de contrato.
        _ = permissions;

        var now = Clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [PermVersionClaimType] = DefaultPermVersion,
        };

        if (user.TenantId is { } tenantId)
        {
            claims[TenantIdClaimType] = tenantId.ToString();
        }

        if (user.CustomerId is { } customerId)
        {
            claims[CustomerIdClaimType] = customerId.ToString();
        }

        var roleList = roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        if (roleList.Length > 0)
        {
            // Múltiplos papéis são serializados como um array JSON no claim.
            claims[RolesClaimType] = roleList;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials,
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(descriptor);
    }

    /// <inheritdoc />
    public async Task<IssuedRefreshToken> IssueRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var issued = await CreateAndPersistAsync(userId, ct).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return issued;
    }

    /// <inheritdoc />
    public async Task<Result<IssuedRefreshToken>> ValidateAndRotateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return InvalidTokenError;
        }

        var incomingHash = Hash(token);
        var now = Clock.GetUtcNow();

        var current = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == incomingHash, ct)
            .ConfigureAwait(false);

        // Desconhecido, revogado ou expirado → falha (R2.5).
        if (current is null || !current.IsActiveAt(now))
        {
            return InvalidTokenError;
        }

        // Rotação (R2.4): emite o sucessor e revoga o corrente encadeando o hash.
        var replacement = await CreateAndPersistAsync(current.UserId, ct).ConfigureAwait(false);

        current.RevokedAt = now;
        current.ReplacedByTokenHash = replacement.Token.TokenHash;
        current.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success(replacement);
    }

    /// <inheritdoc />
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = Clock.GetUtcNow();

        // Filtra por usuário e "não revogado" (predicados traduzíveis em qualquer
        // provider). Tokens já expirados mas não revogados também são marcados —
        // é inócuo (já estavam inativos) e evita comparar DateTimeOffset no banco,
        // o que nem todo provider traduz.
        var notRevoked = await _dbContext.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var t in notRevoked)
        {
            t.RevokedAt = now;
            t.UpdatedAt = now;
        }

        if (notRevoked.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        // Token vazio: nada a revogar (idempotente e silencioso — R2.3).
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var incomingHash = Hash(rawToken);

        var current = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == incomingHash && t.RevokedAt == null, ct)
            .ConfigureAwait(false);

        // Desconhecido ou já revogado: ignorado sem sinalizar estado.
        if (current is null)
        {
            return;
        }

        var now = Clock.GetUtcNow();
        current.RevokedAt = now;
        current.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gera um novo refresh token opaco, cria o registro (somente hash) e o
    /// adiciona ao contexto sem salvar. O <c>SaveChanges</c> é responsabilidade do
    /// chamador, permitindo compor a rotação (revogar + emitir) numa só transação.
    /// </summary>
    private Task<IssuedRefreshToken> CreateAndPersistAsync(Guid userId, CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        var rawToken = GenerateOpaqueToken();

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(rawToken),
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
            CreatedAt = now,
        };

        _dbContext.Set<RefreshToken>().Add(entity);

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new IssuedRefreshToken(rawToken, entity));
    }

    /// <summary>Gera um valor opaco criptograficamente aleatório de 256 bits (base64url).</summary>
    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    /// <summary>Calcula o hash SHA-256 (base64url) de um valor de token.</summary>
    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static readonly Error InvalidToken = Error.NotFound(
        "auth.refresh_token.invalid",
        "Refresh token inválido, expirado ou revogado.");

    private static Result<IssuedRefreshToken> InvalidTokenError => Result.Failure<IssuedRefreshToken>(InvalidToken);
}
