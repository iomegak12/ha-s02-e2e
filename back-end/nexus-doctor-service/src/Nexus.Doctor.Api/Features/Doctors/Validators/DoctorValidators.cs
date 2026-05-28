using FluentValidation;
using Nexus.Doctors.Api.Features.Doctors.Models;

namespace Nexus.Doctors.Api.Features.Doctors.Validators;

/// <summary>FluentValidation rules for <see cref="DoctorCreateRequest"/>.</summary>
public sealed class DoctorCreateRequestValidator : AbstractValidator<DoctorCreateRequest>
{
    public DoctorCreateRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LicenseNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PrimaryPhone).NotEmpty().MaximumLength(32);
        When(x => x.Email is not null, () => RuleFor(x => x.Email!).EmailAddress().MaximumLength(256));
        RuleFor(x => x.PrimaryBranchId).NotEqual(Guid.Empty);
    }
}

/// <summary>Rules for PATCH — at least one field, each within bounds.</summary>
public sealed class DoctorPatchRequestValidator : AbstractValidator<DoctorPatchRequest>
{
    public DoctorPatchRequestValidator()
    {
        When(x => x.FirstName is not null, () => RuleFor(x => x.FirstName!).NotEmpty().MaximumLength(100));
        When(x => x.LastName is not null, () => RuleFor(x => x.LastName!).NotEmpty().MaximumLength(100));
        When(x => x.Specialty is not null, () => RuleFor(x => x.Specialty!).NotEmpty().MaximumLength(100));
        When(x => x.PrimaryPhone is not null, () => RuleFor(x => x.PrimaryPhone!).NotEmpty().MaximumLength(32));
        When(x => x.Email is not null && x.Email.Length > 0, () => RuleFor(x => x.Email!).EmailAddress().MaximumLength(256));

        RuleFor(x => x).Must(r =>
            r.FirstName is not null || r.LastName is not null || r.Specialty is not null
            || r.PrimaryPhone is not null || r.Email is not null || r.PrimaryBranchId is not null)
            .WithMessage("At least one field must be supplied.");
    }
}

/// <summary>Rules for branch-link body.</summary>
public sealed class DoctorBranchLinkRequestValidator : AbstractValidator<DoctorBranchLinkRequest>
{
    public DoctorBranchLinkRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEqual(Guid.Empty);
    }
}
