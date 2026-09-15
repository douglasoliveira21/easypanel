using EasyPanel.Shared.Kernel;

namespace EasyPanel.UnitTests;

/// <summary>
/// Teste de sanidade da estrutura da solução (Task 1.1). Garante que os
/// projetos de módulo estão referenciados e compilam. Substituído por testes
/// reais conforme os módulos são implementados nas tarefas seguintes.
/// </summary>
public class SolutionStructureTests
{
    [Fact]
    public void KernelAssemblyMarker_IsReferenceable()
    {
        Assert.Equal("EasyPanel.Shared.Kernel", typeof(IKernelAssemblyMarker).Namespace);
    }
}
