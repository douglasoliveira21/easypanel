using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Emissão e validação do <c>mfa_ticket</c> de curta duração usado no fluxo de
/// segundo fator (R2.9). O ticket é um portador assinado com <b>propósito
/// dedicado</b> (distinto do access token): identifica o desafio pendente entre a
/// etapa de senha (login) e a etapa de verificação do segundo fator, sem conceder
/// acesso a nenhum recurso protegido.
///
/// <para><b>Segurança.</b> O ticket carrega um propósito próprio (audiência/claim
/// dedicado) e vida curta (poucos minutos), de forma que <i>não</i> possa ser
/// aceito pelo esquema Bearer como um token de acesso, nem reutilizado após
/// expirar. A validação recusa qualquer ticket cuja assinatura, propósito ou
/// validade não confira (R2.9).</para>
/// </summary>
public interface IMfaTicketService
{
    /// <summary>
    /// Emite um <c>mfa_ticket</c> de curta duração para o usuário que passou pela
    /// etapa de senha mas ainda precisa completar o segundo fator.
    /// </summary>
    /// <param name="userId">Identificador do usuário desafiado.</param>
    /// <returns>O desafio contendo o ticket assinado e sua expiração.</returns>
    MfaChallenge IssueTicket(Guid userId);

    /// <summary>
    /// Valida um <c>mfa_ticket</c> (assinatura, propósito e expiração) e, se válido,
    /// resolve o identificador do usuário desafiado.
    /// </summary>
    /// <param name="ticket">Valor do ticket apresentado pelo cliente.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>
    /// Sucesso com o <see cref="Guid"/> do usuário, ou falha
    /// (<see cref="AuthErrors.InvalidMfaTicket"/>) quando o ticket é inválido,
    /// expirado ou de propósito incorreto.
    /// </returns>
    Task<Result<Guid>> ValidateTicketAsync(string ticket, CancellationToken ct);
}
