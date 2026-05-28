using FluentValidation.TestHelper;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Features.Auth.Validators;

namespace Nexus.Identity.Api.Tests.Features.Auth.Validators;

public class RefreshRequestValidatorTests
{
    private readonly RefreshRequestValidator _sut = new();

    [Fact]
    public void Valid_refresh_passes()
    {
        var result = _sut.TestValidate(new RefreshRequest { RefreshToken = "rt-abc123" });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_refresh_fails(string token)
    {
        var result = _sut.TestValidate(new RefreshRequest { RefreshToken = token });
        result.ShouldHaveValidationErrorFor(x => x.RefreshToken);
    }
}
