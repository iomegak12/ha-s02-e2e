namespace Nexus.Identity.Api.Infrastructure.Errors;

/// <summary>
/// Single ProblemDetails field-level error entry.
/// </summary>
/// <param name="Field">JSON pointer or property name of the offending field.</param>
/// <param name="Message">Human-readable message describing what is wrong.</param>
public sealed record ProblemError(string Field, string Message);

/// <summary>
/// Application-thrown exception that maps directly to an RFC 9457 ProblemDetails response.
/// Throw from services/repositories to short-circuit a request with a stable error contract.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>Stable machine-readable error code from <see cref="ErrorCodes"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status code to emit.</summary>
    public int Status { get; }

    /// <summary>Short, human-readable title (mirrors RFC 9457).</summary>
    public string Title { get; }

    /// <summary>Optional field-level errors (used for 422 validation responses).</summary>
    public IReadOnlyList<ProblemError>? Errors { get; }

    /// <summary>Create a domain exception.</summary>
    public DomainException(
        string code,
        int status,
        string title,
        string? detail = null,
        IReadOnlyList<ProblemError>? errors = null)
        : base(detail ?? title)
    {
        Code = code;
        Status = status;
        Title = title;
        Errors = errors;
    }
}
