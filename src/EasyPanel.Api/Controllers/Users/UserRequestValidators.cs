using EasyPanel.Modules.Identity;
using FluentValidation;

namespace EasyPanel.Api.Controllers.Users;

/// <summary>
/// Validador de <see cref="CreateUserApiRequest"/> (R12.2). Aplica as regras de
/// contrato da camada de API — presença e formato de email, presença de senha e
/// pertencimento dos papéis ao catálogo <see cref="Roles.All"/> — antes de a
/// requisição alcançar o <see cref="IUserService"/>. Entrada inválida → HTTP 400
/// com detalhes por campo.
///
/// <para>A política de força da senha (R3.7) é aplicada pelo ASP.NET Identity no
/// serviço, não aqui: o validador exige apenas que a senha esteja presente, para
/// não duplicar (e arriscar divergir de) a política canônica do Identity.</para>
/// </summary>
public sealed class CreateUserApiRequestValidator : AbstractValidator<CreateUserApiRequest>
{
    /// <summary>Configura as regras de validação da criação de usuário.</summary>
    public CreateUserApiRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty();

        RuleForEach(x => x.Roles)
            .Must(role => Roles.All.Contains(role, StringComparer.Ordinal))
            .When(x => x.Roles is not null)
            .WithMessage("Um ou mais papéis informados são inválidos.");
    }
}

/// <summary>
/// Validador de <see cref="UpdateUserApiRequest"/> (R12.2). Exige email presente e
/// válido e papéis pertencentes ao catálogo <see cref="Roles.All"/>. Entrada
/// inválida → HTTP 400.
/// </summary>
public sealed class UpdateUserApiRequestValidator : AbstractValidator<UpdateUserApiRequest>
{
    /// <summary>Configura as regras de validação da atualização de usuário.</summary>
    public UpdateUserApiRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleForEach(x => x.Roles)
            .Must(role => Roles.All.Contains(role, StringComparer.Ordinal))
            .When(x => x.Roles is not null)
            .WithMessage("Um ou mais papéis informados são inválidos.");
    }
}
