using System.Security.Cryptography;
using System.Text;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IClientTokenService"/> (R4.1/R4.4).
///
/// Emite um JWT de acesso curto assinado com a mesma chave simétrica das
/// <see cref="JwtOptions"/>, porém com audience dedicado
/// (<see cref="ClientAuthConstants.Audience"/>) e claims de identidade do agente
/// (client/tenant/local). O refresh é um valor opaco de 256 bits, persistido
/// apenas hasheado (SHA-256) em <see cref="ClientRefreshToken"/> e rotacionado a
/// cada uso, análogo ao do usuário (R2.4).
/// </summary>
public sealed class ClientTokenService : IClientTokenService
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly AppDbContext _dbContext;
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public ClientTokenService(AppDbContext dbContext, IOptions<JwtOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public Task<ClientTokenPair> IssueAsync(
        Guid clientId,
        Guid tenantId,
        Guid locationId,
        CancellationToken ct)
        => IssueInAsync(_dbContext, clientId, tenantId, locationId, ct);

    /// <inheritdoc />
    public async Task<ClientTokenPair> IssueInAsync(
        AppDbContext context,
        Guid clientId,
        Guid tenantId,
        Guid locationId,
        CancellationToken ct)
    {
        var pair = CreateAccessToken(clientId, tenantId, locationId);

        var raw = GenerateOpaqueToken();
        var entity = new ClientRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WindowsClientId = clientId,
            TokenHash = Hash(raw),
            ExpiresAt = Clock.GetUtcNow().AddDays(_options.RefreshTokenDays),
            CreatedAt = Clock.GetUtcNow(),
        };

        context.Set<ClientRefreshToken>().Add(entity);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        return pair with { RefreshToken = raw };
    }

    /// <inheritdoc />
    public async Task<ClientTokenPair?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var incomingHash = Hash(refreshToken);
        var now = Clock.GetUtcNow();

        // IgnoreQueryFilters: a renovação ocorre antes de haver contexto de tenant
        // resolvido; a identidade é validada pela posse do token (hash) e o tenant
        // é recuperado do próprio registro.
        var current = await _dbContext.Set<ClientRefreshToken>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == incomingHash, ct)
            .ConfigureAwait(false);

        if (current is null || !current.IsActiveAt(now))
        {
            return null;
        }

        var pair = CreateAccessToken(current.WindowsClientId, current.TenantId, LocationOf(current));

        var raw = GenerateOpaqueToken();
        var replacement = new ClientRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = current.TenantId,
            WindowsClientId = current.WindowsClientId,
            TokenHash = Hash(raw),
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
            CreatedAt = now,
        };

        current.RevokedAt = now;
        current.ReplacedByTokenHash = replacement.TokenHash;
        current.UpdatedAt = now;

        _dbContext.Set<ClientRefreshToken>().Add(replacement);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return pair with { RefreshToken = raw };
    }

    /// <summary>
    /// Recupera o local do agente para reembutir no novo token de acesso. O local
    /// é lido do <see cref="WindowsClient"/> (fonte de verdade), não do refresh.
    /// </summary>
    private Guid LocationOf(ClientRefreshToken token)
    {
        var client = _dbContext.Set<WindowsClient>()
            .IgnoreQueryFilters()
            .First(c => c.Id == token.WindowsClientId);
        return client.LocationId;
    }

    private ClientTokenPair CreateAccessToken(Guid clientId, Guid tenantId, Guid locationId)
    {
        var now = Clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = clientId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [ClientAuthConstants.ClientIdClaimType] = clientId.ToString(),
            [ClientAuthConstants.TenantIdClaimType] = tenantId.ToString(),
            [ClientAuthConstants.LocationIdClaimType] = locationId.ToString(),
            [ClientAuthConstants.TokenKindClaimType] = ClientAuthConstants.TokenKindValue,
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = ClientAuthConstants.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials,
        };

        var handler = new JsonWebTokenHandler();
        var accessToken = handler.CreateToken(descriptor);

        return new ClientTokenPair(accessToken, string.Empty, expires);
    }

    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

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
}
