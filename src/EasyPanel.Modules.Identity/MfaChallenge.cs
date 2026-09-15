namespace EasyPanel.Modules.Identity;

/// <summary>
/// Desafio de segundo fator emitido quando um usuário com <see cref="ApplicationUser.MfaEnabled"/>
/// autentica com credenciais válidas (R2.9). Em vez de tokens de sessão, o login
/// devolve este desafio: um <see cref="MfaTicket"/> de curta duração que o cliente
/// deve reapresentar, junto do código do segundo fator, ao endpoint de verificação
/// de MFA para então obter os tokens.
///
/// <para>O <see cref="MfaTicket"/> é um portador assinado e de propósito distinto
/// dos tokens de acesso (não pode ser usado como Bearer); vale por poucos minutos
/// (<see cref="ExpiresAt"/>). Nenhum token de sessão é emitido enquanto o segundo
/// fator não for validado, preservando R2.9.</para>
/// </summary>
/// <param name="MfaTicket">Ticket opaco/assinado de curta duração que identifica o desafio pendente.</param>
/// <param name="ExpiresAt">Momento de expiração do ticket (UTC).</param>
public sealed record MfaChallenge(string MfaTicket, DateTimeOffset ExpiresAt);
