using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Audit.Api.Infrastructure.Errors;

namespace Nexus.Audit.Api.Tests.Infrastructure.Errors;

public sealed class ProblemDetailsMiddlewareTests
{
    [Fact]
    public async Task Maps_DomainException_to_problem_json_with_code_and_status()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new DomainException(
                code: ErrorCodes.IdempotencyKeyMissing,
                status: StatusCodes.Status400BadRequest,
                title: "Missing Idempotency-Key",
                detail: "header required"),
            NullLogger<ProblemDetailsMiddleware>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/audit";
        http.Response.Body = new MemoryStream();

        await middleware.Invoke(http);

        http.Response.StatusCode.Should().Be(400);
        http.Response.ContentType.Should().Be("application/problem+json");

        var body = ReadJson(http.Response.Body);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.IdempotencyKeyMissing);
        body.GetProperty("title").GetString().Should().Be("Missing Idempotency-Key");
        body.GetProperty("detail").GetString().Should().Be("header required");
        body.GetProperty("instance").GetString().Should().Be("/api/v1/audit");
        body.GetProperty("type").GetString().Should().StartWith(ErrorCodes.TypeUriBase);
        body.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Maps_unhandled_exception_to_500_INTERNAL_ERROR()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<ProblemDetailsMiddleware>.Instance);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.Invoke(http);

        http.Response.StatusCode.Should().Be(500);
        var body = ReadJson(http.Response.Body);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.InternalError);
    }

    [Fact]
    public async Task Surfaces_field_level_errors_for_validation()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new DomainException(
                code: ErrorCodes.AuditValidation,
                status: StatusCodes.Status422UnprocessableEntity,
                title: "Validation failed",
                detail: "see errors",
                errors: [new ProblemError("Summary", "is required")]),
            NullLogger<ProblemDetailsMiddleware>.Instance);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.Invoke(http);

        http.Response.StatusCode.Should().Be(422);
        var body = ReadJson(http.Response.Body);
        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("field").GetString().Should().Be("Summary");
        errors[0].GetProperty("message").GetString().Should().Be("is required");
    }

    [Fact]
    public async Task Passthrough_when_no_exception()
    {
        var middleware = new ProblemDetailsMiddleware(
            ctx => { ctx.Response.StatusCode = 204; return Task.CompletedTask; },
            NullLogger<ProblemDetailsMiddleware>.Instance);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.Invoke(http);

        http.Response.StatusCode.Should().Be(204);
    }

    private static JsonElement ReadJson(Stream body)
    {
        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }
}
