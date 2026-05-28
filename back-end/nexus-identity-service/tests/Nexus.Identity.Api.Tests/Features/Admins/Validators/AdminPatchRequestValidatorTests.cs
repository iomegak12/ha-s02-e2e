using FluentValidation.TestHelper;
using Nexus.Identity.Api.Features.Admins.Models;
using Nexus.Identity.Api.Features.Admins.Validators;

namespace Nexus.Identity.Api.Tests.Features.Admins.Validators;

public class AdminPatchRequestValidatorTests
{
    private readonly AdminPatchRequestValidator _sut = new();

    [Fact]
    public void Empty_body_fails()
    {
        var result = _sut.TestValidate(new AdminPatchRequest());
        result.ShouldHaveValidationErrorFor("body");
    }

    [Fact]
    public void DisplayName_only_passes()
    {
        var result = _sut.TestValidate(new AdminPatchRequest { DisplayName = "New Name" });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void IsActive_only_passes()
    {
        var result = _sut.TestValidate(new AdminPatchRequest { IsActive = false });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Short_display_name_fails()
    {
        var result = _sut.TestValidate(new AdminPatchRequest { DisplayName = "A" });
        result.ShouldHaveValidationErrorFor(x => x.DisplayName!);
    }

    [Fact]
    public void Long_display_name_fails()
    {
        var result = _sut.TestValidate(new AdminPatchRequest { DisplayName = new string('a', 121) });
        result.ShouldHaveValidationErrorFor(x => x.DisplayName!);
    }
}
