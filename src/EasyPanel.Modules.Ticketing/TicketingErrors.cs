using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Ticketing;

/// <summary>Erros de domínio do módulo de Chamados (Fase 6), no padrão de <c>InventoryErrors</c>/<c>SupplyErrors</c>.</summary>
public static class TicketingErrors
{
    /// <summary>Recurso inexistente ou de outro tenant. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "ticketing.not_found",
        "Recurso não encontrado.");

    /// <summary>Título do chamado ausente (R1.3). → 400.</summary>
    public static readonly Error TitleRequired = Error.Validation(
        "ticketing.ticket.title_required",
        "O título do chamado é obrigatório.");

    /// <summary>Cliente informado não pertence ao tenant corrente (R1.3). → 400.</summary>
    public static readonly Error InvalidCustomer = Error.Validation(
        "ticketing.ticket.invalid_customer",
        "Cliente informado é inválido para o tenant.");

    /// <summary>Local informado não pertence ao tenant corrente (R1.4). → 400.</summary>
    public static readonly Error InvalidLocation = Error.Validation(
        "ticketing.ticket.invalid_location",
        "Local informado é inválido para o tenant.");

    /// <summary>Impressora informada não pertence ao tenant corrente (R1.4). → 400.</summary>
    public static readonly Error InvalidPrinter = Error.Validation(
        "ticketing.ticket.invalid_printer",
        "Impressora informada é inválida para o tenant.");

    /// <summary>Técnico informado não pertence ao tenant corrente (R2.4). → 400.</summary>
    public static readonly Error InvalidAssignee = Error.Validation(
        "ticketing.ticket.invalid_assignee",
        "Usuário informado para atribuição é inválido para o tenant.");

    /// <summary>Transição de status não permitida a partir do status corrente (R2.3). → 400.</summary>
    public static readonly Error InvalidStatusTransition = Error.Validation(
        "ticketing.ticket.invalid_status_transition",
        "Transição de status não permitida a partir do status corrente.");

    /// <summary>Comentário vazio (R2.5). → 400.</summary>
    public static readonly Error CommentRequired = Error.Validation(
        "ticketing.ticket.comment_required",
        "O comentário não pode ser vazio.");

    /// <summary>Prazo de SLA fora do intervalo permitido (R4.1). → 400.</summary>
    public static readonly Error InvalidSlaPolicy = Error.Validation(
        "ticketing.sla.invalid_policy",
        "Os prazos de SLA devem ser maiores que zero, com resolução maior ou igual à primeira resposta.");

    /// <summary>Anexo excede o tamanho máximo permitido (R5.2). → 400.</summary>
    public static readonly Error AttachmentTooLarge = Error.Validation(
        "ticketing.attachment.too_large",
        "O arquivo excede o tamanho máximo permitido.");

    /// <summary>Tipo de arquivo do anexo não permitido (R5.2). → 400.</summary>
    public static readonly Error AttachmentTypeNotAllowed = Error.Validation(
        "ticketing.attachment.type_not_allowed",
        "O tipo do arquivo não é permitido.");

    /// <summary>Falha ao gravar ou ler o anexo no storage. → 400 (nenhum metadado é persistido nem retornado).</summary>
    public static readonly Error AttachmentStorageFailure = Error.Validation(
        "ticketing.attachment.storage_failure",
        "Não foi possível processar o arquivo no armazenamento.");
}
