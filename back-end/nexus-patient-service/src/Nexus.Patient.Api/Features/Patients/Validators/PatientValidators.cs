using FluentValidation;
using Nexus.Patients.Api.Features.Patients.Models;

namespace Nexus.Patients.Api.Features.Patients.Validators;

/// <summary>FluentValidation rules for <see cref="PatientCreateRequest"/> (spec bounds).</summary>
public sealed class PatientCreateRequestValidator : AbstractValidator<PatientCreateRequest>
{
    private static readonly string[] AllowedGenders = { "Male", "Female", "Other", "Unknown" };

    public PatientCreateRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .Must(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", out _))
            .WithMessage("dateOfBirth must be ISO yyyy-MM-dd.");
        RuleFor(x => x.Gender).Must(g => AllowedGenders.Contains(g))
            .WithMessage($"gender must be one of {string.Join(", ", AllowedGenders)}.");
        RuleFor(x => x.PrimaryPhone).NotEmpty().MaximumLength(32);
        When(x => x.Email is not null, () =>
        {
            RuleFor(x => x.Email!).EmailAddress().MaximumLength(256);
        });
        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(200);
        When(x => x.AddressLine2 is not null, () => RuleFor(x => x.AddressLine2!).MaximumLength(200));
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().Length(2);
        RuleFor(x => x.PrimaryBranchId).NotEqual(Guid.Empty);
    }
}

/// <summary>Rules for PATCH — every supplied field must respect bounds.</summary>
public sealed class PatientPatchRequestValidator : AbstractValidator<PatientPatchRequest>
{
    public PatientPatchRequestValidator()
    {
        When(x => x.FirstName is not null, () => RuleFor(x => x.FirstName!).NotEmpty().MaximumLength(100));
        When(x => x.LastName is not null, () => RuleFor(x => x.LastName!).NotEmpty().MaximumLength(100));
        When(x => x.PrimaryPhone is not null, () => RuleFor(x => x.PrimaryPhone!).NotEmpty().MaximumLength(32));
        When(x => x.Email is not null && x.Email.Length > 0, () => RuleFor(x => x.Email!).EmailAddress().MaximumLength(256));
        When(x => x.AddressLine1 is not null, () => RuleFor(x => x.AddressLine1!).NotEmpty().MaximumLength(200));
        When(x => x.Country is not null, () => RuleFor(x => x.Country!).Length(2));

        RuleFor(x => x)
            .Must(r =>
                r.FirstName is not null || r.LastName is not null
                || r.PrimaryPhone is not null || r.Email is not null
                || r.AddressLine1 is not null || r.AddressLine2 is not null
                || r.City is not null || r.State is not null
                || r.PostalCode is not null || r.Country is not null
                || r.PrimaryBranchId is not null)
            .WithMessage("At least one field must be supplied.");
    }
}

/// <summary>Rules for branch-link body.</summary>
public sealed class PatientBranchLinkRequestValidator : AbstractValidator<PatientBranchLinkRequest>
{
    public PatientBranchLinkRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEqual(Guid.Empty);
    }
}
