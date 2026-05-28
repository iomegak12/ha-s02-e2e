using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Identity.Api.Infrastructure.Errors;

/// <summary>
/// Terminal middleware that converts <see cref="DomainException"/> and unhandled
/// exceptions into <c>application/problem+json</c> responses per RFC 9457.
/// </summary>
public sealed class ProblemDetailsMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;

    /// <summary>Create the middleware.</summary>
    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Process a request.</summary>
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException dx)
        {
            await WriteProblemAsync(context, dx.Status, dx.Code, dx.Title, dx.Message, dx.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR", "Internal Server Error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        string? detail,
        IReadOnlyList<ProblemError>? errors = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemPayload
        {
            Type = ErrorCodes.TypeUriBase + code.ToLowerInvariant().Replace('_', '-'),
            Title = title,
            Status = status,
            Code = code,
            Detail = detail,
            Instance = context.Request.Path.Value,
            TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
            Errors = errors,
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions);
    }

    private sealed class ProblemPayload
    {
        public string Type { get; init; } = default!;
        public string Title { get; init; } = default!;
        public int Status { get; init; }
        public string Code { get; init; } = default!;
        public string? Detail { get; init; }
        public string? Instance { get; init; }
        public string TraceId { get; init; } = default!;
        public IReadOnlyList<ProblemError>? Errors { get; init; }
    }
}
