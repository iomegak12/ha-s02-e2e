namespace Nexus.Web.Client.Models.Common;

/// <summary>
/// Generic wrapper for all API responses.
/// On success, Data is populated. On failure, Error is populated.
/// </summary>
public sealed class ApiResponse<T>
{
    public T?            Data    { get; private init; }
    public ProblemDetails? Error  { get; private init; }
    public bool          IsSuccess => Error is null;

    public static ApiResponse<T> Ok(T data)               => new() { Data = data };
    public static ApiResponse<T> Fail(ProblemDetails error) => new() { Error = error };
}
