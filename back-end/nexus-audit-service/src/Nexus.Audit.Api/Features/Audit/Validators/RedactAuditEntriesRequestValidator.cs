using FluentValidation;
using Nexus.Audit.Api.Features.Audit.Models;

namespace Nexus.Audit.Api.Features.Audit.Validators;

/// <summary>Rules for the admin-only redact hook.</summary>
public sealed class RedactAuditEntriesRequestValidator : AbstractValidator<RedactAuditEntriesRequest>
{
    public RedactAuditEntriesRequestValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EntityId).NotEqual(Guid.Empty);
        When(x => x.RedactionPlaceholder is not null, () =>
        {
            RuleFor(x => x.RedactionPlaceholder!).MaximumLength(256);
        });
    }
}
