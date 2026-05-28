using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Identity.Api.Infrastructure.Errors;

namespace Nexus.Identity.Api.Tests.Infrastructure.Errors;

public class ProblemDetailsMiddlewareTests
{
    private static async Task<(int Status, string ContentType, JsonDocument Body)> InvokeAsync(RequestDelegate next)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/v1/things";

        var mw = new ProblemDetailsMiddleware(next, NullLogger<ProblemDetailsMiddleware>.Instance);
        await mw.Invoke(ctx);

        ctx.Response.Body.Position = 0;
        var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, ctx.Response.ContentType ?? string.Empty, doc);
    }

    [Fact]
    public async Task DomainException_writes_problem_json()
    {
        var (status, contentType, doc) = await InvokeAsync(_ => throw new DomainException(
            ErrorCodes.AdminUsernameDuplicate, 409, "Username Already Exists", "duplicate"));

        status.Should().Be(409);
        contentType.Should().StartWith("application/problem+json");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        doc.RootElement.GetProperty("code").GetString().Should().Be(ErrorCodes.AdminUsernameDuplicate);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Username Already Exists");
        doc.RootElement.GetProperty("detail").GetString().Should().Be("duplicate");
        doc.RootElement.GetProperty("instance").GetString().Should().Be("/api/v1/things");
        doc.RootElement.GetProperty("type").GetString().Should().StartWith(ErrorCodes.TypeUriBase);
        doc.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DomainException_includes_errors_array_when_present()
    {
        var errors = new[] { new ProblemError("username", "Invalid characters.") };
        var (_, _, doc) = await InvokeAsync(_ => throw new DomainException(
            ErrorCodes.AdminValidation, 422, "Validation Failed", "validation", errors));

        var errs = doc.RootElement.GetProperty("errors").EnumerateArray().ToList();
        errs.Should().ContainSingle();
        errs[0].GetProperty("field").GetString().Should().Be("username");
        errs[0].GetProperty("message").GetString().Should().Be("Invalid characters.");
    }

    [Fact]
    public async Task Unhandled_exception_writes_500_INTERNAL_ERROR()
    {
        var (status, contentType, doc) = await InvokeAsync(_ => throw new InvalidOperationException("boom"));

        status.Should().Be(500);
        contentType.Should().StartWith("application/problem+json");
        doc.RootElement.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task No_exception_passes_through_unchanged()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var mw = new ProblemDetailsMiddleware(c =>
        {
            c.Response.StatusCode = 204;
            return Task.CompletedTask;
        }, NullLogger<ProblemDetailsMiddleware>.Instance);

        await mw.Invoke(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Body.Length.Should().Be(0);
    }
}
