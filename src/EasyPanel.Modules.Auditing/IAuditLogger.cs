namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Ponto de entrada para registro de eventos sensíveis na trilha de auditoria (R10).
///
/// <para>
/// A auditoria é <b>somente-adição</b> por convenção de negócio (R10.4): este é o
/// único contrato de escrita e ele expõe exclusivamente a <b>inserção</b> de novos
/// registros. Não há — em lugar algum — API de atualização ou remoção de
/// <see cref="AuditLog"/>. Uma vez gravado, um evento é imutável.
/// </para>
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Registra um novo evento de auditoria (R10.1, R10.3). A implementação atribui
    /// o <c>Id</c> e carimba o <c>OccurredAt</c> (UTC) no momento da gravação.
    /// </summary>
    /// <param name="entry">Descrição do evento a registrar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task LogAsync(AuditEntry entry, CancellationToken ct);
}
