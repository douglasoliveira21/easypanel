using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Users;

/// <summary>
/// Gestão de usuários do tenant do contexto (R7): expõe <c>/api/v1/users</c> com
/// listagem paginada, criação, atualização, desativação e reativação.
///
/// <para><b>Autorização (R5.3/R5.4).</b> Todas as ações exigem a permissão
/// <c>user.manage</c> via <see cref="RequirePermissionAttribute"/> aplicado à
/// classe: requisições não autenticadas recebem 401 e autenticadas sem a permissão
/// recebem 403, ambos pelo pipeline padrão de autorização (avaliação sempre no
/// backend — R5.6).</para>
///
/// <para><b>Isolamento e origem do tenant (R6.3/R7.1).</b> O tenant é sempre
/// derivado do token pelo <see cref="IUserService"/>; o controller nunca o aceita
/// do cliente. Usuários de outro tenant são indistinguíveis de inexistentes
/// (404 — não-vazamento).</para>
///
/// <para><b>Validação (R12.2).</b> Os corpos de entrada são validados
/// explicitamente via <see cref="IValidator{T}"/> (FluentValidation); entrada
/// inválida → 400 com <c>ValidationProblemDetails</c>.</para>
///
/// <para><b>Contratos (R12.1).</b> Entrada e saída usam DTOs de API distintos das
/// entidades e do domínio; nenhuma resposta expõe hash de senha ou stamps (R11.2).</para>
///
/// <para><b>Auditoria (R7.8).</b> Toda mutação é auditada pelo serviço, com o ator
/// enriquecido a partir do claim <c>sub</c> do token (via
/// <see cref="ICurrentUserAccessor"/>). O tratamento centralizado de erros
/// (ProblemDetails) chega na tarefa 9.1; aqui as falhas de <see cref="Result"/> são
/// mapeadas para HTTP diretamente a partir do <see cref="ErrorType"/>.</para>
/// </summary>
[ApiController]
[Route("api/v1/users")]
[RequirePermission(Permissions.UserManage)]
public sealed class UsersController(
    IUserService userService,
    IValidator<CreateUserApiRequest> createValidator,
    IValidator<UpdateUserApiRequest> updateValidator) : ControllerBase
{
    private readonly IUserService _userService = userService;
    private readonly IValidator<CreateUserApiRequest> _createValidator = createValidator;
    private readonly IValidator<UpdateUserApiRequest> _updateValidator = updateValidator;

    /// <summary>
    /// Lista os usuários do tenant do contexto (R7.7), paginados. <c>page</c> e
    /// <c>pageSize</c> são normalizados pelo <see cref="PageRequest"/> (PageSize ≤ 100
    /// — R12.4). Requer a permissão <c>user.manage</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new PageRequest(page, pageSize);

        var result = await _userService.ListAsync(request, cancellationToken).ConfigureAwait(false);

        // A listagem não produz erros esperados de domínio nesta fase; um contexto
        // sem tenant devolve página vazia (R7.7).
        var page_ = result.Value;
        var items = page_.Items.Select(ToResponse).ToList();

        return Ok(new UserPageResponse(items, page_.Page, page_.PageSize, page_.TotalCount));
    }

    /// <summary>
    /// Cria um usuário vinculado ao tenant do contexto (R7.1). Retorna 201 com o
    /// usuário criado. Email duplicado no tenant → 409 (R7.2); dados/senha/papéis
    /// inválidos → 400 (R12.2/R3.7/R5.1).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateUserApiRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(ToModelStateDictionary(validation));
        }

        var domainRequest = new CreateUserRequest(
            request.Email,
            request.Password,
            request.Roles ?? Array.Empty<string>(),
            request.CustomerId);

        var result = await _userService.CreateAsync(domainRequest, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(List), new { }, response);
    }

    /// <summary>
    /// Atualiza dados e papéis de um usuário do próprio tenant (R7.3). Retorna 200
    /// com o usuário atualizado. Usuário inexistente/de outro tenant → 404; email
    /// duplicado → 409; dados/papéis inválidos → 400.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateUserApiRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(ToModelStateDictionary(validation));
        }

        var domainRequest = new UpdateUserRequest(
            request.Email,
            request.MfaEnabled,
            request.Roles ?? Array.Empty<string>(),
            request.CustomerId);

        var result = await _userService.UpdateAsync(id, domainRequest, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : MapFailure(result.Error);
    }

    /// <summary>
    /// Desativa um usuário do próprio tenant (R7.4): marca inativo e revoga seus
    /// refresh tokens. Retorna 204. Usuário inexistente/de outro tenant → 404.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _userService.DeactivateAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    /// <summary>
    /// Reativa um usuário do próprio tenant (R7.6): marca ativo. Retorna 204.
    /// Usuário inexistente/de outro tenant → 404.
    /// </summary>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _userService.ReactivateAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    private static UserResponse ToResponse(UserDto dto) =>
        new(dto.Id, dto.Email, dto.TenantId, dto.CustomerId, dto.IsActive, dto.MfaEnabled, dto.Roles, dto.CreatedAt);

    /// <summary>
    /// Traduz um <see cref="FluentValidation.Results.ValidationResult"/> inválido em
    /// um <see cref="ModelStateDictionary"/> para produzir 400 com
    /// <c>ValidationProblemDetails</c> (R12.2).
    /// </summary>
    private static Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary ToModelStateDictionary(
        FluentValidation.Results.ValidationResult validation)
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        foreach (var error in validation.Errors)
        {
            modelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return modelState;
    }

    /// <summary>
    /// Mapeia um erro esperado de domínio (<see cref="Error"/>) para o resultado
    /// HTTP correspondente ao seu <see cref="ErrorType"/> (400/403/404/409), com um
    /// <see cref="ProblemDetails"/> que não vaza dados sensíveis. Espelha a tabela de
    /// Error Handling do design; o tratamento centralizado chega na tarefa 9.1.
    /// </summary>
    private IActionResult MapFailure(Error error) => error.Type switch
    {
        ErrorType.NotFound => NotFound(ToProblem(error, StatusCodes.Status404NotFound, "Não encontrado")),
        ErrorType.Conflict => Conflict(ToProblem(error, StatusCodes.Status409Conflict, "Conflito")),
        ErrorType.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ToProblem(error, StatusCodes.Status403Forbidden, "Proibido")),
        _ => BadRequest(ToProblem(error, StatusCodes.Status400BadRequest, "Requisição inválida")),
    };

    private static ProblemDetails ToProblem(Error error, int status, string title) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = error.Message,
            Type = error.Code,
        };
}
