using Microsoft.Extensions.Options;
using Nexus.Audit.Api.Configuration;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Features.Audit.Validators;

namespace Nexus.Audit.Api.Tests.Features.Audit.Validators;

public sealed class AppendAuditEntryRequestValidatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider Clock = new FixedClock(FixedNow);

    private static AppendAuditEntryRequestValidator BuildValidator(int maxSkewMinutes = 5)
    {
        var opts = Options.Create(new IdempotencyOptions
        {
            RetentionDays = 7,
            MaxClockSkewMinutes = maxSkewMinutes,
            TrimIntervalMinutes = 60,
        });
        return new AppendAuditEntryRequestValidator(opts, Clock);
    }

    private static AppendAuditEntryRequest ValidRequest(Action<AppendAuditEntryRequestBuilder>? customize = null)
    {
        var b = new AppendAuditEntryRequestBuilder
        {
            EntityType = "Patient",
            EntityId = Guid.NewGuid(),
            EntityCode = null,
            Action = AuditActions.Created,
            ActorId = Guid.NewGuid(),
            ActorUsername = "admin",
            OccurredAtUtc = FixedNow.UtcDateTime.AddSeconds(-30),
            Summary = "Patient created via test",
            DiffJson = null,
        };
        customize?.Invoke(b);
        return b.Build();
    }

    [Fact]
    public async Task Valid_request_passes()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_EntityType_fails()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest(b => b.EntityType = ""));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AppendAuditEntryRequest.EntityType));
    }

    [Fact]
    public async Task Empty_EntityId_fails()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest(b => b.EntityId = Guid.Empty));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_action_fails()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest(b => b.Action = "Frobnicated"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AppendAuditEntryRequest.Action));
    }

    [Fact]
    public async Task All_canonical_actions_pass()
    {
        var validator = BuildValidator();
        foreach (var action in AuditActions.All)
        {
            var result = await validator.ValidateAsync(ValidRequest(b => b.Action = action));
            result.IsValid.Should().BeTrue($"action '{action}' should be accepted");
        }
    }

    [Fact]
    public async Task Summary_over_512_chars_fails()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest(b => b.Summary = new string('s', 513)));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task DiffJson_over_64K_fails()
    {
        var validator = BuildValidator();
        var oversize = new string('x', AppendAuditEntryRequestValidator.MaxDiffJsonLength + 1);
        var result = await validator.ValidateAsync(ValidRequest(b => b.DiffJson = oversize));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AppendAuditEntryRequest.DiffJson));
    }

    [Fact]
    public async Task OccurredAt_just_inside_future_skew_passes()
    {
        var validator = BuildValidator(maxSkewMinutes: 5);
        var result = await validator.ValidateAsync(ValidRequest(b =>
            b.OccurredAtUtc = FixedNow.UtcDateTime.AddMinutes(4).AddSeconds(59)));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task OccurredAt_beyond_future_skew_fails()
    {
        var validator = BuildValidator(maxSkewMinutes: 5);
        var result = await validator.ValidateAsync(ValidRequest(b =>
            b.OccurredAtUtc = FixedNow.UtcDateTime.AddMinutes(10)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AppendAuditEntryRequest.OccurredAtUtc));
    }

    [Fact]
    public async Task OccurredAt_older_than_30_days_fails()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest(b =>
            b.OccurredAtUtc = FixedNow.UtcDateTime.AddDays(-31)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AppendAuditEntryRequest.OccurredAtUtc));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AppendAuditEntryRequestBuilder
    {
        public string EntityType { get; set; } = "";
        public Guid EntityId { get; set; }
        public string? EntityCode { get; set; }
        public string Action { get; set; } = "";
        public Guid ActorId { get; set; }
        public string ActorUsername { get; set; } = "";
        public DateTime OccurredAtUtc { get; set; }
        public string Summary { get; set; } = "";
        public string? DiffJson { get; set; }

        public AppendAuditEntryRequest Build() => new(
            EntityType, EntityId, EntityCode, Action, ActorId, ActorUsername, OccurredAtUtc, Summary, DiffJson);
    }
}
