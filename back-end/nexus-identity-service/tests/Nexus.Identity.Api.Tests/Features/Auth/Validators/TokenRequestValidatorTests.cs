using FluentValidation.TestHelper;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Features.Auth.Validators;

namespace Nexus.Identity.Api.Tests.Features.Auth.Validators;

public class TokenRequestValidatorTests
{
    private readonly TokenRequestValidator _sut = new();

    [Fact]
    public void Valid_request_passes()
    {
        var result = _sut.TestValidate(new TokenRequest { Username = "asha.menon", Password = "S3cret!Pa55word" });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_username_fails(string username)
    {
        var result = _sut.TestValidate(new TokenRequest { Username = username, Password = "S3cret!Pa55word" });
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_password_fails(string password)
    {
        var result = _sut.TestValidate(new TokenRequest { Username = "asha.menon", Password = password });
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}
