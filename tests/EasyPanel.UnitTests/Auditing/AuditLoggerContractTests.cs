using System.Reflection;
using EasyPanel.Modules.Auditing;

namespace EasyPanel.UnitTests.Auditing;

/// <summary>
/// Unit tests do contrato somente-adição da auditoria (R10.4). A garantia de que a
/// auditoria não expõe atualização/remoção é estrutural: o único ponto de escrita
/// (<see cref="IAuditLogger"/>) declara exclusivamente inserção.
/// </summary>
public class AuditLoggerContractTests
{
    [Fact]
    public void IAuditLogger_ExposesOnlyAppendOperation()
    {
        var methods = typeof(IAuditLogger).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        var method = Assert.Single(methods);
        Assert.Equal(nameof(IAuditLogger.LogAsync), method.Name);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("remove")]
    [InlineData("edit")]
    public void IAuditLogger_HasNoMutationMethods(string forbiddenVerb)
    {
        var methods = typeof(IAuditLogger).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(
            methods,
            m => m.Name.Contains(forbiddenVerb, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuditResult_HasExpectedClosedSet()
    {
        Assert.Equal(
            new[] { AuditResult.Success, AuditResult.Denied, AuditResult.Failure },
            Enum.GetValues<AuditResult>());
    }
}
