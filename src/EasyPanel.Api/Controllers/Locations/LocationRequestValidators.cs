using FluentValidation;

namespace EasyPanel.Api.Controllers.Locations;

/// <summary>
/// Validador de <see cref="CreateLocationApiRequest"/> (R12.2). Exige um cliente
/// referenciado (CustomerId não vazio), nome presente e comprimentos válidos antes
/// de a requisição alcançar o <see cref="EasyPanel.Modules.Customers.ILocationService"/>.
/// A verificação de existência/pertencimento do cliente ao tenant (R9.4) fica no
/// serviço, surgindo como <c>LocationErrors.CustomerNotFound</c> (HTTP 400).
/// </summary>
public sealed class CreateLocationApiRequestValidator : AbstractValidator<CreateLocationApiRequest>
{
    /// <summary>Configura as regras de validação da criação de local.</summary>
    public CreateLocationApiRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty();

        RuleFor(x => x.Nome)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Responsavel)
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

/// <summary>
/// Validador de <see cref="UpdateLocationApiRequest"/> (R12.2). Exige nome presente
/// e comprimentos válidos. Entrada inválida → HTTP 400.
/// </summary>
public sealed class UpdateLocationApiRequestValidator : AbstractValidator<UpdateLocationApiRequest>
{
    /// <summary>Configura as regras de validação da atualização de local.</summary>
    public UpdateLocationApiRequestValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Responsavel)
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
