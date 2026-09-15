using FluentValidation;

namespace EasyPanel.Api.Controllers.Customers;

/// <summary>
/// Validador de <see cref="CreateCustomerApiRequest"/> (R12.2). Aplica as regras
/// de contrato da camada de API — presença de razão social e CNPJ, comprimentos —
/// antes de a requisição alcançar o <see cref="EasyPanel.Modules.Customers.ICustomerService"/>.
/// Entrada inválida → HTTP 400 com detalhes por campo.
///
/// <para>A validação de dígitos verificadores do CNPJ (R8.4) permanece no serviço
/// (fonte única de verdade via <c>CnpjValidator</c>), surgindo como
/// <c>CustomerErrors.InvalidCnpj</c> (HTTP 400); o validador exige apenas que o
/// CNPJ esteja presente, para não duplicar (nem divergir de) a regra canônica.</para>
/// </summary>
public sealed class CreateCustomerApiRequestValidator : AbstractValidator<CreateCustomerApiRequest>
{
    /// <summary>Configura as regras de validação da criação de cliente.</summary>
    public CreateCustomerApiRequestValidator()
    {
        RuleFor(x => x.RazaoSocial)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Cnpj)
            .NotEmpty();

        RuleFor(x => x.NomeFantasia)
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Estado)
            .MaximumLength(2)
            .When(x => !string.IsNullOrWhiteSpace(x.Estado));
    }
}

/// <summary>
/// Validador de <see cref="UpdateCustomerApiRequest"/> (R12.2). Exige razão social
/// e CNPJ presentes e comprimentos válidos; a validação de dígitos do CNPJ fica no
/// serviço. Entrada inválida → HTTP 400.
/// </summary>
public sealed class UpdateCustomerApiRequestValidator : AbstractValidator<UpdateCustomerApiRequest>
{
    /// <summary>Configura as regras de validação da atualização de cliente.</summary>
    public UpdateCustomerApiRequestValidator()
    {
        RuleFor(x => x.RazaoSocial)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Cnpj)
            .NotEmpty();

        RuleFor(x => x.NomeFantasia)
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Estado)
            .MaximumLength(2)
            .When(x => !string.IsNullOrWhiteSpace(x.Estado));
    }
}
