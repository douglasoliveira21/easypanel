namespace EasyPanel.Modules.Identity;

/// <summary>
/// Dados de entrada para a atualização de um usuário do próprio tenant (R7.3).
/// A alteração de senha não faz parte deste contrato (é conduzida pelos fluxos
/// de senha do <see cref="IAuthService"/> — R3), e o tenant nunca é alterado.
/// </summary>
/// <param name="Email">
/// Novo email/nome de usuário. A unicidade por tenant continua valendo (R7.2):
/// colidir com o email de outro usuário do mesmo tenant é conflito.
/// </param>
/// <param name="MfaEnabled">Novo estado de exigência de segundo fator (R2.10).</param>
/// <param name="Roles">
/// Conjunto desejado de papéis. É reconciliado com os papéis atuais (adiciona os
/// ausentes, remove os excedentes); devem pertencer a <see cref="Roles.All"/>.
/// </param>
/// <param name="CustomerId">
/// Cliente (Customer) ao qual vincular o usuário (Fase 10). Obrigatório quando
/// <paramref name="Roles"/> contém <see cref="Roles.Cliente"/>; ignorado (forçado a
/// nulo) caso contrário.
/// </param>
public sealed record UpdateUserRequest(
    string Email,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    Guid? CustomerId = null);
