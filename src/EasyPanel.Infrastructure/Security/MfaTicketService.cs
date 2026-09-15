using System.Text;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IMfaTicketService"/> sobre a biblioteca
/// IdentityModel (R2.9). Emite e valida o <c>mfa_ticket</c> como um JWS assinado
/// com a <b>mesma chave</b> das <see cref="JwtOptions"/>, porém com um propósito
/// dedicado (audiência distinta + claim <c>purpose=mfa_ticket</c>) e vida muito
/// curta (<see cref="TicketLifetime"/>).
///
/// <para><b>Isolamento do access token.</b> A audiência do ticket
/// (<c>{Audience}:mfa</c>) difere da audiência dos tokens de acesso, e o esquema
/// Bearer valida a audiência esperada; assim, um <c>mfa_ticket</c> jamais é aceito
/// como token de acesso a recursos protegidos. A validação aqui exige emissor,
/// audiência de MFA, propósito e assinatura corretos, além de expiração válida
/// (sem tolerância de relógio), recusando qualquer desvio (R2.9).</para>
/// </summary>
public sealed class MfaTicketService : IMfaTicketService
{
    /// <summary>Valor do claim de propósito que marca o token como um ticket de MFA.</summary>
    public const string PurposeClaimType = "purpose";

    /// <summary>Propósito dedicado do ticket de segundo fator.</summary>
    public const string MfaTicketPurpose = "mfa_ticket";

    /// <summary>Vida curta do ticket: janela para completar o segundo fator.</summary>
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly JwtOptions _options;
    private readonly string _mfaAudience;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    /// <summary>Cria o serviço a partir das opções de JWT já validadas.</summary>
    public MfaTicketService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _mfaAudience = $"{_options.Audience}:mfa";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _mfaAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    }

    /// <inheritdoc />
    public MfaChallenge IssueTicket(Guid userId)
    {
        var now = Clock.GetUtcNow();
        var expires = now.Add(TicketLifetime);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [PurposeClaimType] = MfaTicketPurpose,
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _mfaAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials,
        };

        var handler = new JsonWebTokenHandler();
        var ticket = handler.CreateToken(descriptor);

        return new MfaChallenge(ticket, expires);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> ValidateTicketAsync(string ticket, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Result.Failure<Guid>(AuthErrors.InvalidMfaTicket);
        }

        var handler = new JsonWebTokenHandler();
        var validation = await handler
            .ValidateTokenAsync(ticket, _validationParameters)
            .ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return Result.Failure<Guid>(AuthErrors.InvalidMfaTicket);
        }

        var jwt = (JsonWebToken)validation.SecurityToken;

        // Propósito dedicado: recusa qualquer token que não seja um mfa_ticket
        // (ex.: tentativa de reaproveitar um access token).
        if (!string.Equals(jwt.GetClaim(PurposeClaimType)?.Value, MfaTicketPurpose, StringComparison.Ordinal))
        {
            return Result.Failure<Guid>(AuthErrors.InvalidMfaTicket);
        }

        var subject = jwt.GetClaim(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out var userId)
            ? Result.Success(userId)
            : Result.Failure<Guid>(AuthErrors.InvalidMfaTicket);
    }
}
