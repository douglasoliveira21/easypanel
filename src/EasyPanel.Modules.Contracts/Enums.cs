namespace EasyPanel.Modules.Contracts;

/// <summary>Estado do ciclo de vida de um <see cref="Contract"/> (Fase 7 — R5.1). Transições
/// permitidas são impostas pelo serviço, não pelo enum — ver <c>design.md</c>.</summary>
public enum ContractStatus
{
    Rascunho = 0,
    Ativo = 1,
    Suspenso = 2,
    Encerrado = 3,
}

/// <summary>
/// Tipo de contador coberto por uma <see cref="ContractFranchise"/> (Fase 7 —
/// R3). Espelha <c>Modules.Monitoring.CounterType</c> (mesmos valores
/// inteiros), mantido separado para não criar referência de módulo cruzada —
/// <c>Infrastructure.Contracts</c> é quem sabe que os dois enums correspondem
/// à mesma semântica de contador.
/// </summary>
public enum ContractCounterType
{
    BlackAndWhite = 0,
    Color = 1,
    A3 = 2,
    A4 = 3,
    Scan = 4,
    Other = 99,
}
