using FluentValidation;
using Nexus.Identity.Api.Features.Auth.Models;

namespace Nexus.Identity.Api.Features.Auth.Validators;

/// <summary>Validates <see cref="TokenRequest"/> bodies.</summary>
public sealed class TokenRequestValidator : AbstractValidator<TokenRequest>
{
    /// <summary>Configures validation rules.</summary>
    public TokenRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
