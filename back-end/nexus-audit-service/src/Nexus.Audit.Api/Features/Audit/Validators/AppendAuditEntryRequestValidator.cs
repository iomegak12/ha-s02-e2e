using FluentValidation;
using Microsoft.Extensions.Options;
using Nexus.Audit.Api.Configuration;
using Nexus.Audit.Api.Features.Audit.Models;

namespace Nexus.Audit.Api.Features.Audit.Validators;

/// <summary>
/// FluentValidation rules for <see cref="AppendAuditEntryRequest"/>. Field bounds match
/// the database schema in <c>docs/IMPLEMENTATION_PLAN.md</c> §4; the clock-skew rule
/// is documented in §21.
/// </summary>
public sealed class AppendAuditEntryRequestValidator : AbstractValidator<AppendAuditEntryRequest>
{
    /// <summary>Hard cap on <see cref="AppendAuditEntryRequest.DiffJson"/> length (64 KB).</summary>
    public const int MaxDiffJsonLength = 64 * 1024;

    /// <summary>Maximum allowed age of <c>occurredAtUtc</c>. Older entries are rejected as 422.</summary>
    public static readonly TimeSpan MaxBackdate = TimeSpan.FromDays(30);

    /// <summary>Create the validator.</summary>
    public AppendAuditEntryRequestValidator(
        IOptions<IdempotencyOptions> idempotencyOptions,
        TimeProvider timeProvider)
    {
        var skew = TimeSpan.FromMinutes(idempotencyOptions.Value.MaxClockSkewMinutes);

        RuleFor(x => x.EntityType)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.EntityId)
            .NotEqual(Guid.Empty);

        RuleFor(x => x.EntityCode)
            .MaximumLength(64);

        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(AuditActions.IsKnown)
            .WithMessage($"action must be one of: {string.Join(", ", AuditActions.All)}");

        RuleFor(x => x.ActorId)
            .NotEqual(Guid.Empty);

        RuleFor(x => x.ActorUsername)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.Summary)
            .NotEmpty()
            .MaximumLength(512);

        RuleFor(x => x.DiffJson)
            .MaximumLength(MaxDiffJsonLength)
            .WithMessage($"diffJson must not exceed {MaxDiffJsonLength} characters");

        RuleFor(x => x.OccurredAtUtc)
            .Must(value =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                return value <= now + skew && value >= now - MaxBackdate;
            })
            .WithMessage(_ =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                return $"occurredAtUtc must be within [{now - MaxBackdate:O}, {now + skew:O}]";
            });
    }
}
