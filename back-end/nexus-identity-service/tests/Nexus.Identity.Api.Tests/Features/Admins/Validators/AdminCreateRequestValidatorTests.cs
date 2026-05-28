using FluentValidation.TestHelper;
using Nexus.Identity.Api.Features.Admins.Models;
using Nexus.Identity.Api.Features.Admins.Validators;

namespace Nexus.Identity.Api.Tests.Features.Admins.Validators;

public class AdminCreateRequestValidatorTests
{
    private readonly AdminCreateRequestValidator _sut = new();

    private static AdminCreateRequest Valid(string? username = null, string? displayName = null, string? password = null) => new()
    {
        Username = username ?? "asha.menon",
        DisplayName = displayName ?? "Asha Menon",
        Password = password ?? "S3cret!Pa55word",
    };

    [Fact]
    public void Happy_path_passes()
    {
        var result = _sut.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]               // shorter than 3
    [InlineData("Asha")]              // uppercase forbidden
    [InlineData("name with space")]   // space forbidden
    [InlineData("user@example.com")]  // '@' forbidden
    public void Invalid_username_fails(string username)
    {
        var result = _sut.TestValidate(Valid(username: username));
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Username_64_chars_is_max_allowed()
    {
        var sixtyFour = new string('a', 64);
        var result = _sut.TestValidate(Valid(username: sixtyFour));
        result.ShouldNotHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Username_65_chars_fails()
    {
        var sixtyFive = new string('a', 65);
        var result = _sut.TestValidate(Valid(username: sixtyFive));
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]               // shorter than 2
    public void Invalid_display_name_fails(string displayName)
    {
        var result = _sut.TestValidate(Valid(displayName: displayName));
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]            // shorter than 8
    public void Invalid_password_fails(string password)
    {
        var result = _sut.TestValidate(Valid(password: password));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Password_129_chars_fails()
    {
        var result = _sut.TestValidate(Valid(password: new string('a', 129)));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}
