using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nexus.Audit.Api.Infrastructure.OpenApi;

/// <summary>
/// Decorates the <c>appendAuditEntry</c> operation with the documented header
/// parameters: <c>Idempotency-Key</c> (required) and <c>X-Source-Service</c>
/// (optional). Both echo what the <c>IdempotencyKeyFilter</c> enforces at runtime.
/// </summary>
public sealed class AuditHeaderOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(operation.OperationId, "appendAuditEntry", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();

        AddHeader(operation, "Idempotency-Key", required: true,
            description: "Caller-supplied UUID — unique per (sourceService, key). Required.");

        AddHeader(operation, "X-Source-Service", required: false,
            description: "Optional source-service hint (overrides the JWT iss claim). Lower-cased on write.");
    }

    private static void AddHeader(OpenApiOperation operation, string name, bool required, string description)
    {
        if (operation.Parameters.Any(p => p.In == ParameterLocation.Header
            && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = "string" },
        });
    }
}
