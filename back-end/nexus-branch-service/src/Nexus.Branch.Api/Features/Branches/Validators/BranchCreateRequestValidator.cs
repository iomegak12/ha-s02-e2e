using System.Text.RegularExpressions;
using FluentValidation;
using Nexus.Branches.Api.Features.Branches.Models;

namespace Nexus.Branches.Api.Features.Branches.Validators;

/// <summary>Spec: <c>code</c> is 2-5 uppercase letters; name 2-120; city 2-80.</summary>
public sealed class BranchCreateRequestValidator : AbstractValidator<BranchCreateRequest>
{
    private static readonly Regex CodePattern = new("^[A-Z]{2,5}$", RegexOptions.Compiled);

    public BranchCreateRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Must(c => c is not null && CodePattern.IsMatch(c))
            .WithMessage("Code must be 2-5 uppercase letters.");

        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.City).NotEmpty().MinimumLength(2).MaximumLength(80);
    }
}

/// <summary>PATCH: every supplied field must respect the same bounds. <c>code</c> is not patchable.</summary>
public sealed class BranchPatchRequestValidator : AbstractValidator<BranchPatchRequest>
{
    public BranchPatchRequestValidator()
    {
        When(x => x.Name is not null, () =>
        {
            RuleFor(x => x.Name!).MinimumLength(2).MaximumLength(120);
        });
        When(x => x.City is not null, () =>
        {
            RuleFor(x => x.City!).MinimumLength(2).MaximumLength(80);
        });

        RuleFor(x => x)
            .Must(r => r.Name is not null || r.City is not null || r.IsActive is not null)
            .WithMessage("At least one of name, city or isActive must be supplied.");
    }
}
