using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Customers;

/// <summary>
/// Cadastro de clientes do tenant do contexto (R8): expõe <c>/api/v1/customers</c>
/// com listagem paginada, consulta por id, criação, atualização e alteração de
/// status.
///
/// <para><b>Autorização granular (R5.3/R5.4).</b> Cada ação exige a permissão
/// adequada via <see cref="RequirePermissionAttribute"/>: leitura → <c>customer.view</c>,
/// criação → <c>customer.create</c>, atualização e alteração de status →
/// <c>customer.edit</c>. Requisições não autenticadas recebem 401 e autenticadas
/// sem a permissão recebem 403, ambos pelo pipeline padrão de autorização
/// (avaliação sempre no backend — R5.6).</para>
///
/// <para><b>Isolamento e origem do tenant (R6.3/R8.1).</b> O tenant é sempre
/// derivado do token; o controller nunca o aceita do cliente. Como
/// <see cref="Customer"/> é uma <c>TenantEntity</c>, leituras são auto-escopadas
/// pelo filtro global do ORM: um cliente de outro tenant é indistinguível de um
/// inexistente (404 — não-vazamento, R8.7).</para>
///
/// <para><b>Validação (R12.2) e contratos (R12.1).</b> Os corpos de entrada são
/// validados explicitamente via <see cref="IValidator{T}"/> (FluentValidation);
/// entrada e saída usam DTOs de API distintos das entidades/domínio. O tratamento
/// centralizado de erros (ProblemDetails) chega na tarefa 9.1; aqui as falhas de
/// <see cref="Result"/> são mapeadas para HTTP diretamente a partir do
/// <see cref="ErrorType"/>.</para>
/// </summary>
[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(
    ICustomerService customerService,
    IValidator<CreateCustomerApiRequest> createValidator,
    IValidator<UpdateCustomerApiRequest> updateValidator) : ControllerBase
{
    private readonly ICustomerService _customerService = customerService;
    private readonly IValidator<CreateCustomerApiRequest> _createValidator = createValidator;
    private readonly IValidator<UpdateCustomerApiRequest> _updateValidator = updateValidator;

    /// <summary>
    /// Lista os clientes do tenant do contexto (R8.8), paginados. <c>page</c> e
    /// <c>pageSize</c> são normalizados pelo <see cref="PageRequest"/> (PageSize ≤ 100
    /// — R12.4). Requer <c>customer.view</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.CustomerView)]
    [ProducesResponseType(typeof(CustomerPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new PageRequest(page, pageSize);

        var result = await _customerService.ListAsync(request, cancellationToken).ConfigureAwait(false);

        var paged = result.Value;
        var items = paged.Items.Select(ToResponse).ToList();

        return Ok(new CustomerPageResponse(items, paged.Page, paged.PageSize, paged.TotalCount));
    }

    /// <summary>
    /// Consulta um cliente por identificador, restrito ao tenant do contexto (R8.7).
    /// Inexistente/de outro tenant → 404. Requer <c>customer.view</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.CustomerView)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _customerService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>
    /// Cria um cliente vinculado ao tenant do contexto (R8.1). Retorna 201 com o
    /// cliente criado. CNPJ inválido/dados inválidos → 400 (R8.4/R12.2); CNPJ
    /// duplicado no tenant → 409 (R8.5). Requer <c>customer.create</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission(Permissions.CustomerCreate)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateCustomerApiRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(ToModelStateDictionary(validation));
        }

        var domainRequest = new CreateCustomerRequest(
            request.RazaoSocial,
            request.Cnpj,
            request.NomeFantasia,
            request.InscricaoEstadual,
            request.Telefone,
            request.Email,
            request.Endereco,
            request.Cidade,
            request.Estado,
            request.Cep,
            request.Observacoes,
            request.Status);

        var result = await _customerService.CreateAsync(domainRequest, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>
    /// Atualiza um cliente do próprio tenant (R8.6). Retorna 200 com o cliente
    /// atualizado. Inexistente/de outro tenant → 404; CNPJ inválido → 400; CNPJ
    /// duplicado → 409. Requer <c>customer.edit</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.CustomerEdit)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateCustomerApiRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(ToModelStateDictionary(validation));
        }

        var domainRequest = new UpdateCustomerRequest(
            request.RazaoSocial,
            request.Cnpj,
            request.NomeFantasia,
            request.InscricaoEstadual,
            request.Telefone,
            request.Email,
            request.Endereco,
            request.Cidade,
            request.Estado,
            request.Cep,
            request.Observacoes);

        var result = await _customerService.UpdateAsync(id, domainRequest, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>
    /// Altera o status de um cliente do próprio tenant (R8.9). Retorna 200 com o
    /// cliente atualizado. Inexistente/de outro tenant → 404; status inválido → 400.
    /// Requer <c>customer.edit</c>.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [RequirePermission(Permissions.CustomerEdit)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        ChangeCustomerStatusApiRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _customerService
            .ChangeStatusAsync(id, request.Status, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static CustomerResponse ToResponse(CustomerDto dto) =>
        new(
            dto.Id,
            dto.TenantId,
            dto.RazaoSocial,
            dto.NomeFantasia,
            dto.Cnpj,
            dto.InscricaoEstadual,
            dto.Telefone,
            dto.Email,
            dto.Endereco,
            dto.Cidade,
            dto.Estado,
            dto.Cep,
            dto.Observacoes,
            dto.Status,
            dto.CreatedAt,
            dto.UpdatedAt);

    /// <summary>
    /// Traduz um <see cref="FluentValidation.Results.ValidationResult"/> inválido em
    /// um <see cref="Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary"/>
    /// para produzir 400 com <c>ValidationProblemDetails</c> (R12.2).
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
    /// HTTP correspondente ao seu <see cref="ErrorType"/> (400/403/404/409). Espelha
    /// a tabela de Error Handling do design; o tratamento centralizado chega na 9.1.
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
