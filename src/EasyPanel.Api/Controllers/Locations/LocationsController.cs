using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Locations;

/// <summary>
/// Cadastro de locais (pontos de instalação) do tenant do contexto (R9): expõe a
/// criação, consulta por id, atualização e alteração de status em
/// <c>/api/v1/locations</c>, além da listagem por cliente em
/// <c>/api/v1/customers/{customerId}/locations</c>.
///
/// <para><b>Autorização granular (R5.3/R5.4).</b> Cada ação exige a permissão
/// adequada via <see cref="RequirePermissionAttribute"/>: leitura → <c>location.view</c>,
/// criação → <c>location.create</c>, atualização e alteração de status →
/// <c>location.edit</c>. Não autenticado → 401; sem permissão → 403 (avaliação
/// sempre no backend — R5.6).</para>
///
/// <para><b>Isolamento e origem do tenant (R6.3/R9.1).</b> O tenant é sempre
/// derivado do token. Como <see cref="Location"/> é uma <c>TenantEntity</c>,
/// leituras são auto-escopadas pelo filtro global do ORM: um local de outro tenant
/// é indistinguível de um inexistente (404 — não-vazamento, R9.6).</para>
///
/// <para><b>Validação (R12.2) e contratos (R12.1).</b> Corpos de entrada validados
/// via <see cref="IValidator{T}"/>; entrada e saída usam DTOs de API distintos das
/// entidades/domínio. Falhas de <see cref="Result"/> são mapeadas para HTTP a
/// partir do <see cref="ErrorType"/> (o tratamento centralizado chega na 9.1).</para>
/// </summary>
[ApiController]
public sealed class LocationsController(
    ILocationService locationService,
    IValidator<CreateLocationApiRequest> createValidator,
    IValidator<UpdateLocationApiRequest> updateValidator) : ControllerBase
{
    private readonly ILocationService _locationService = locationService;
    private readonly IValidator<CreateLocationApiRequest> _createValidator = createValidator;
    private readonly IValidator<UpdateLocationApiRequest> _updateValidator = updateValidator;

    /// <summary>
    /// Lista os locais de um cliente do tenant do contexto (R9.7), paginados. Se o
    /// cliente não pertencer ao tenant, a listagem vem vazia (não-vazamento).
    /// Requer <c>location.view</c>.
    /// </summary>
    [HttpGet("api/v1/customers/{customerId:guid}/locations")]
    [RequirePermission(Permissions.LocationView)]
    [ProducesResponseType(typeof(LocationPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LocationPageResponse>> ListByCustomer(
        Guid customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new PageRequest(page, pageSize);

        var result = await _locationService
            .ListByCustomerAsync(customerId, request, cancellationToken)
            .ConfigureAwait(false);

        var paged = result.Value;
        var items = paged.Items.Select(ToResponse).ToList();

        return Ok(new LocationPageResponse(items, paged.Page, paged.PageSize, paged.TotalCount));
    }

    /// <summary>
    /// Consulta um local por identificador, restrito ao tenant do contexto (R9.6).
    /// Inexistente/de outro tenant → 404. Requer <c>location.view</c>.
    /// </summary>
    [HttpGet("api/v1/locations/{id:guid}")]
    [RequirePermission(Permissions.LocationView)]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _locationService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>
    /// Cria um local vinculado a um cliente do próprio tenant (R9.1). Retorna 201.
    /// Cliente inexistente/de outro tenant ou dados inválidos → 400 (R9.4/R12.2).
    /// Requer <c>location.create</c>.
    /// </summary>
    [HttpPost("api/v1/locations")]
    [RequirePermission(Permissions.LocationCreate)]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        CreateLocationApiRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(ToModelStateDictionary(validation));
        }

        var domainRequest = new CreateLocationRequest(
            request.CustomerId,
            request.Nome,
            request.Endereco,
            request.Responsavel,
            request.Telefone,
            request.Email,
            request.Observacoes,
            request.Status);

        var result = await _locationService.CreateAsync(domainRequest, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>
    /// Atualiza um local do próprio tenant (R9.5). Retorna 200. Inexistente/de outro
    /// tenant → 404; dados inválidos → 400. Requer <c>location.edit</c>.
    /// </summary>
    [HttpPut("api/v1/locations/{id:guid}")]
    [RequirePermission(Permissions.LocationEdit)]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateLocationApiRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(ToModelStateDictionary(validation));
        }

        var domainRequest = new UpdateLocationRequest(
            request.Nome,
            request.Endereco,
            request.Responsavel,
            request.Telefone,
            request.Email,
            request.Observacoes);

        var result = await _locationService.UpdateAsync(id, domainRequest, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>
    /// Altera o status de um local do próprio tenant (R9.8). Retorna 200.
    /// Inexistente/de outro tenant → 404; status inválido → 400. Requer
    /// <c>location.edit</c>.
    /// </summary>
    [HttpPatch("api/v1/locations/{id:guid}/status")]
    [RequirePermission(Permissions.LocationEdit)]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        ChangeLocationStatusApiRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _locationService
            .ChangeStatusAsync(id, request.Status, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static LocationResponse ToResponse(LocationDto dto) =>
        new(
            dto.Id,
            dto.TenantId,
            dto.CustomerId,
            dto.Nome,
            dto.Endereco,
            dto.Responsavel,
            dto.Telefone,
            dto.Email,
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
