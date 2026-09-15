namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Opções de bootstrap (seed) da inicialização (tarefa 10.1), vinculadas da seção
/// <c>Bootstrap</c> da configuração.
///
/// <para>O seed limita-se a dados estruturais — os papéis fixos da Fase 1 (R5.1) —
/// e, opcionalmente, a um usuário Super Admin inicial para o onboarding (R68). Não
/// há dados de negócio fictícios: o Super Admin só é criado quando um email <b>e</b>
/// uma senha são explicitamente fornecidos por configuração/segredo (nunca
/// versionados — R1.6).</para>
/// </summary>
public sealed class BootstrapOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Bootstrap";

    /// <summary>
    /// Se verdadeiro, os papéis fixos da plataforma são semeados na inicialização
    /// (idempotente). Padrão: verdadeiro.
    /// </summary>
    public bool SeedRolesOnStartup { get; set; } = true;

    /// <summary>
    /// Email do Super Admin inicial da plataforma. Quando informado junto de
    /// <see cref="SuperAdminPassword"/>, um usuário Super Admin (sem tenant) é
    /// criado na inicialização, se ainda não existir. Vazio desabilita o seed do
    /// Super Admin.
    /// </summary>
    public string? SuperAdminEmail { get; set; }

    /// <summary>
    /// Senha do Super Admin inicial (injetada por segredo; nunca versionada — R1.6).
    /// Deve satisfazer a política de senhas (R3.7). Vazia desabilita o seed do
    /// Super Admin.
    /// </summary>
    public string? SuperAdminPassword { get; set; }
}
