namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Semeador idempotente dos papéis fixos da Fase 1 (R5.1). Garante que os 8
/// papéis existam no banco (tabela <c>AspNetRoles</c>, já criada pela migração do
/// Identity) sem duplicar os já presentes.
///
/// A invocação em tempo de inicialização (Program.cs) é deliberadamente deixada
/// para a tarefa de bootstrap/composição de DI (10.1); aqui provemos apenas o
/// serviço e seu registro em DI. Isso evita interferência com as factories de
/// teste (SQLite + EnsureCreated) que não passam pelo pipeline de bootstrap.
/// </summary>
public interface IRoleSeeder
{
    /// <summary>
    /// Cria quaisquer papéis ausentes de forma idempotente. Executar novamente não
    /// duplica papéis já existentes.
    /// </summary>
    /// <returns>Quantidade de papéis efetivamente criados nesta execução.</returns>
    Task<int> SeedAsync(CancellationToken cancellationToken = default);
}
