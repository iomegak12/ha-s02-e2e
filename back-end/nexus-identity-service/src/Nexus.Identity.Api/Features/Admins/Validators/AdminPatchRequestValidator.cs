using FluentValidation;
using Nexus.Identity.Api.Features.Admins.Models;

namespace Nexus.Identity.Api.Features.Admins.Validators;

/// <summary>Validates <see cref="AdminPatchRequest"/> bodies.</summary>
public sealed class AdminPatchRequestValidator : AbstractValidator<AdminPatchRequest>
{
    /// <summary>Configures validation rules.</summary>
    public AdminPatchRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.DisplayName is not null || x.IsActive is not null)
            .WithName("body")
            .WithMessage("At least one of 'displayName' or 'isActive' must be supplied.");

        When(x => x.DisplayName is not null, () =>
        {
            RuleFor(x => x.DisplayName!)
                .MinimumLength(2)
                .MaximumLength(120);
        });
    }
}
