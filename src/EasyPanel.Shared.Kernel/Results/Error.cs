namespace EasyPanel.Shared.Kernel.Results;

/// <summary>
/// Categoria de um erro esperado de domínio. Mapeada para códigos HTTP
/// pelo tratamento centralizado de erros (ver design, seção Error Handling).
/// </summary>
public enum ErrorType
{
    /// <summary>Falha de validação de entrada (HTTP 400).</summary>
    Validation,

    /// <summary>Recurso não encontrado ou acesso cross-tenant (HTTP 404).</summary>
    NotFound,

    /// <summary>Conflito de estado, ex.: duplicidade (HTTP 409).</summary>
    Conflict,

    /// <summary>Operação não autorizada por permissão (HTTP 403).</summary>
    Forbidden
}

/// <summary>
/// Representa um erro esperado de domínio, com código estável, mensagem
/// legível e categoria. Usado pelo padrão <see cref="Result"/> para
/// modelar falhas sem recorrer a exceções para fluxo de controle.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Erro sentinela que representa a ausência de erro.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Validation);

    /// <summary>Cria um erro de validação.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Cria um erro de recurso não encontrado.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Cria um erro de conflito.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Cria um erro de autorização negada.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
