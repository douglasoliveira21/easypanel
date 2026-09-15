namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Resultado de um evento auditável (R10). Persistido como <c>int</c> pela
/// convenção de enums do <c>AppDbContext</c>, mantendo tipagem forte no domínio.
/// </summary>
public enum AuditResult
{
    /// <summary>A ação foi concluída com sucesso.</summary>
    Success = 0,

    /// <summary>A ação foi recusada por falta de permissão ou isolamento de tenant.</summary>
    Denied = 1,

    /// <summary>A ação falhou (ex.: credenciais inválidas em uma tentativa de login).</summary>
    Failure = 2,
}
