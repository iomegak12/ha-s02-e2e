using FluentValidation;
using Nexus.Identity.Api.Features.Auth.Models;

namespace Nexus.Identity.Api.Features.Auth.Validators;

/// <summary>
/// Validates the body of <c>POST /api/v1/auth/refresh</c> and <c>POST /api/v1/auth/revoke</c>.
/// </summary>
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    /// <summary>Configures validation rules.</summary>
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(2048);
    }
}
