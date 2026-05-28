using System.Text.RegularExpressions;
using FluentValidation;
using Nexus.Identity.Api.Features.Admins.Models;

namespace Nexus.Identity.Api.Features.Admins.Validators;

/// <summary>Validates <see cref="AdminCreateRequest"/> bodies.</summary>
public sealed partial class AdminCreateRequestValidator : AbstractValidator<AdminCreateRequest>
{
    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    /// <summary>Configures validation rules.</summary>
    public AdminCreateRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(64)
            .Matches(UsernameRegex())
            .WithMessage("Username must contain only lowercase letters, digits, '.', '_' or '-'.");

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(120);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);
    }
}
