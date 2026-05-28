using FluentValidation;

namespace Nexus.Audit.Api.Infrastructure.Errors;

/// <summary>
/// Endpoint filter that resolves <see cref="IValidator{T}"/> from DI and short-circuits
/// the request with a 422 ProblemDetails response when the body fails validation.
/// </summary>
/// <typeparam name="T">The request DTO to validate. Expected at argument index 0.</typeparam>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    private readonly IValidator<T> _validator;
    private readonly string _code;

    /// <summary>Create the filter. <paramref name="code"/> defaults to <see cref="ErrorCodes.AuditValidation"/>.</summary>
    public ValidationFilter(IValidator<T> validator, string? code = null)
    {
        _validator = validator;
        _code = code ?? ErrorCodes.AuditValidation;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context);
        }

        var result = await _validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .Select(e => new ProblemError(e.PropertyName, e.ErrorMessage))
            .ToList();

        throw new DomainException(
            code: _code,
            status: StatusCodes.Status422UnprocessableEntity,
            title: "Validation failed",
            detail: "One or more fields failed validation.",
            errors: errors);
    }
}
