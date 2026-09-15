using Microsoft.Extensions.Logging;

namespace EasyPanel.WindowsClient;

/// <summary>Resultado da tentativa de aplicar uma atualização (R16).</summary>
public enum UpdateOutcome
{
    /// <summary>Pacote recusado na verificação (hash/assinatura) — versão mantida (R16.4).</summary>
    Rejected = 0,

    /// <summary>Atualização aplicada e verificada com sucesso.</summary>
    Applied = 1,

    /// <summary>Aplicação falhou na verificação de saúde; rollback realizado (R16.5).</summary>
    RolledBack = 2,
}

/// <summary>
/// Operações de sistema necessárias para aplicar uma atualização, abstraídas para
/// permitir teste determinístico do fluxo (backup/aplicação/health-check/rollback)
/// sem tocar o sistema de arquivos real nem reiniciar o serviço.
/// </summary>
public interface IUpdateHost
{
    /// <summary>Faz backup da versão atual antes de aplicar (R16.5).</summary>
    Task BackupCurrentAsync(CancellationToken ct);

    /// <summary>Aplica o conteúdo do pacote (extrai/substitui binários).</summary>
    Task ApplyAsync(byte[] packageContent, string version, CancellationToken ct);

    /// <summary>Reinicia e verifica a saúde da nova versão; <c>false</c> se não sobe (R16.5).</summary>
    Task<bool> RestartAndHealthCheckAsync(CancellationToken ct);

    /// <summary>Restaura a versão anterior a partir do backup (rollback — R16.5).</summary>
    Task RollbackAsync(CancellationToken ct);
}

/// <summary>
/// Orquestra a aplicação segura de atualizações (R16.2–R16.5): verifica o pacote
/// (<see cref="UpdatePackageVerifier"/>); se válido, faz backup, aplica, reinicia e
/// verifica a saúde. Em falha de inicialização, faz rollback para a versão anterior.
/// Um pacote inválido é recusado sem tocar a instalação corrente.
/// </summary>
public sealed class UpdateApplier
{
    private readonly UpdatePackageVerifier _verifier;
    private readonly IUpdateHost _host;
    private readonly ILogger<UpdateApplier> _logger;

    public UpdateApplier(UpdatePackageVerifier verifier, IUpdateHost host, ILogger<UpdateApplier> logger)
    {
        _verifier = verifier;
        _host = host;
        _logger = logger;
    }

    /// <summary>
    /// Tenta aplicar o pacote. Retorna o <see cref="UpdateOutcome"/> correspondente:
    /// recusado, aplicado ou revertido.
    /// </summary>
    public async Task<UpdateOutcome> TryApplyAsync(
        byte[] packageContent,
        UpdatePackageMetadata metadata,
        CancellationToken ct)
    {
        if (!_verifier.Verify(packageContent, metadata))
        {
            _logger.LogWarning(
                "Pacote de atualização {Version} recusado (hash/assinatura inválidos). Versão mantida.",
                metadata.Version);
            return UpdateOutcome.Rejected;
        }

        await _host.BackupCurrentAsync(ct).ConfigureAwait(false);
        await _host.ApplyAsync(packageContent, metadata.Version, ct).ConfigureAwait(false);

        var healthy = await _host.RestartAndHealthCheckAsync(ct).ConfigureAwait(false);
        if (healthy)
        {
            _logger.LogInformation("Atualização {Version} aplicada com sucesso.", metadata.Version);
            return UpdateOutcome.Applied;
        }

        _logger.LogError(
            "Nova versão {Version} não passou no health-check; efetuando rollback.",
            metadata.Version);
        await _host.RollbackAsync(ct).ConfigureAwait(false);
        return UpdateOutcome.RolledBack;
    }
}
