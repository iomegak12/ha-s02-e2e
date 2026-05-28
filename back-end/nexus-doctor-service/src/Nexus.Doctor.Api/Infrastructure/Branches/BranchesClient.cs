using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Nexus.Doctors.Api.Infrastructure.Errors;

namespace Nexus.Doctors.Api.Infrastructure.Branches;

/// <summary>Subset of the Branches API consumed by the Doctors service.</summary>
public interface IBranchesClient
{
    Task<bool> IsActiveBranchAsync(Guid branchId, CancellationToken ct);
    Task<BranchSummary?> GetBranchAsync(Guid branchId, CancellationToken ct);
}

/// <summary>Minimal Branch shape consumed by Doctors.</summary>
public sealed class BranchSummary
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; init; }
}

/// <summary>
/// Typed <see cref="HttpClient"/> hitting <c>nexus-branch-service</c>. Polly resilience
/// is configured at registration time via <c>AddStandardResilienceHandler</c>.
/// </summary>
public sealed class BranchesClient : IBranchesClient
{
    /// <summary>DI name for the typed HttpClient.</summary>
    public const string HttpClientName = "BranchesClient";

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger<BranchesClient> _logger;

    public BranchesClient(HttpClient http, IHttpContextAccessor accessor, ILogger<BranchesClient> logger)
    {
        _http = http;
        _accessor = accessor;
        _logger = logger;
    }

    public async Task<bool> IsActiveBranchAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await GetBranchAsync(branchId, ct);
        return branch is { IsActive: true };
    }

    public async Task<BranchSummary?> GetBranchAsync(Guid branchId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/branches/{branchId}");
            ForwardBearer(request);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Branches lookup for {BranchId} returned {Status}", branchId, (int)response.StatusCode);
                throw DependencyUnavailable("Branches lookup returned non-success.");
            }
            return await response.Content.ReadFromJsonAsync<BranchSummary>(cancellationToken: ct);
        }
        catch (DomainException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Branches lookup failed for {BranchId}", branchId);
            throw DependencyUnavailable($"Branches service is unavailable: {ex.GetType().Name}.");
        }
    }

    private void ForwardBearer(HttpRequestMessage request)
    {
        var ctx = _accessor.HttpContext;
        if (ctx is null) return;
        if (ctx.Request.Headers.TryGetValue("Authorization", out var auth) && !string.IsNullOrWhiteSpace(auth))
        {
            request.Headers.TryAddWithoutValidation("Authorization", (string)auth!);
        }
    }

    private static DomainException DependencyUnavailable(string detail) =>
        new(ErrorCodes.DependencyUnavailable, StatusCodes.Status503ServiceUnavailable,
            "Service Unavailable", detail);
}
