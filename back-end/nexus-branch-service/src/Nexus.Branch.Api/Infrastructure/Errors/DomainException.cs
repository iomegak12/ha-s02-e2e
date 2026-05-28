namespace Nexus.Branches.Api.Infrastructure.Errors;

/// <summary>Single ProblemDetails field-level error entry.</summary>
public sealed record ProblemError(string Field, string Message);

/// <summary>Application-thrown exception that maps directly to an RFC 9457 ProblemDetails response.</summary>
public sealed class DomainException : Exception
{
    public string Code { get; }
    public int Status { get; }
    public string Title { get; }
    public IReadOnlyList<ProblemError>? Errors { get; }

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
