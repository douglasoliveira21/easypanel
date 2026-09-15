namespace EasyPanel.Modules.Identity;

/// <summary>
/// Expõe o identificador do usuário atuante na requisição corrente (o "ator"),
/// derivado sempre do <c>ClaimsPrincipal</c> autenticado (claim <c>sub</c>) — nunca
/// de entrada fornecida pelo cliente (R6.3).
///
/// <para>Existe para enriquecer a trilha de auditoria com o ator das mutações
/// (R7.8) sem alterar a assinatura do <see cref="IUserService"/>: o
/// <see cref="ITenantContext"/> resolve o <c>tenant</c> da requisição, mas não o
/// usuário atuante; este acessor complementa essa lacuna. A implementação de
/// produção lê o <c>HttpContext</c> corrente; em contextos sem requisição HTTP
/// (ex.: testes de unidade/serviço, tarefas de fundo) devolve <c>null</c>
/// graciosamente.</para>
///
/// <para>Registrado com tempo de vida <c>scoped</c>, coerente com o ciclo de vida
/// da requisição.</para>
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Identificador do usuário autenticado que originou a requisição, ou
    /// <c>null</c> quando não há requisição/usuário resolvível (ex.: fluxo anônimo
    /// ou execução fora de um pipeline HTTP).
    /// </summary>
    Guid? UserId { get; }
}

/// <summary>
/// Implementação nula de <see cref="ICurrentUserAccessor"/> que sempre devolve
/// <c>null</c>. Serve como padrão seguro para composições sem pipeline HTTP (ex.:
/// testes de serviço e execuções de fundo), onde não há ator a resolver. A
/// auditoria continua funcionando (R7.8), apenas sem <c>ActorUserId</c>.
/// </summary>
public sealed class NullCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>Instância compartilhada e imutável.</summary>
    public static readonly NullCurrentUserAccessor Instance = new();

    /// <inheritdoc />
    public Guid? UserId => null;
}
