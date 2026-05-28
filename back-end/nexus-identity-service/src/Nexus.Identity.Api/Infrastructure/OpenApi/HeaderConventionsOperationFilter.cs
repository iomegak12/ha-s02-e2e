using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nexus.Identity.Api.Infrastructure.OpenApi;

/// <summary>
/// Swashbuckle operation filter that decorates operations with conventional
/// <c>Idempotency-Key</c> (write) and <c>If-Match</c> (update) header parameters.
/// </summary>
public sealed class HeaderConventionsOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod?.ToUpperInvariant();
        if (method is null)
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();

        if (method is "POST")
        {
            AddHeader(operation, "Idempotency-Key", required: false,
                description: "Client-generated UUID used to safely retry the same operation.");
        }

        if (method is "PATCH" or "PUT" or "DELETE")
        {
            AddHeader(operation, "If-Match", required: true,
                description: "ETag of the resource being modified. Mismatch results in 409 ETAG_MISMATCH.");
        }
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
