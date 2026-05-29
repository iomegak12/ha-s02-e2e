using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Web.Client.Models.Branches;
using Nexus.Web.Client.Models.Common;

namespace Nexus.Web.Client.Services.Branches;

public sealed class BranchService : IBranchService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    // PATCH bodies must omit null fields — backend validates minLength on present fields only
    private static readonly JsonSerializerOptions _patchOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BranchService(HttpClient http) => _http = http;

    public async Task<ApiResponse<PagedResult<BranchModel>>> ListAsync(
        int page = 1,
        int size = 20,
        string? city = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder($"/api/v1/branches?page={page}&size={size}");
        if (!string.IsNullOrWhiteSpace(city))
            sb.Append($"&city={Uri.EscapeDataString(city)}");
        if (isActive.HasValue)
            sb.Append($"&isActive={isActive.Value.ToString().ToLowerInvariant()}");

        var response = await _http.GetAsync(sb.ToString(), ct);
        if (response.IsSuccessStatusCode)
        {
            // API returns { page, size, totalCount, items } — map to PagedResult<T>
            var raw = await response.Content.ReadFromJsonAsync<PagedBranchRaw>(_jsonOpts, ct);
            if (raw is not null)
                return ApiResponse<PagedResult<BranchModel>>.Ok(new PagedResult<BranchModel>
                {
                    Items = raw.Items,
                    TotalCount = raw.TotalCount,
                    Page = raw.Page,
                    Size = raw.Size
                });
        }
        return ApiResponse<PagedResult<BranchModel>>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<BranchModel>> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/branches/{id}", ct);
        if (response.IsSuccessStatusCode)
        {
            var branch = await response.Content.ReadFromJsonAsync<BranchModel>(_jsonOpts, ct);
            if (branch is not null) return ApiResponse<BranchModel>.Ok(branch);
        }
        return ApiResponse<BranchModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<BranchModel>> CreateAsync(BranchCreateRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, _jsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/branches") { Content = content };
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode)
        {
            var branch = await response.Content.ReadFromJsonAsync<BranchModel>(_jsonOpts, ct);
            if (branch is not null) return ApiResponse<BranchModel>.Ok(branch);
        }
        return ApiResponse<BranchModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<BranchModel>> PatchAsync(string id, BranchPatchRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, _patchOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/branches/{id}") { Content = content };
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode)
        {
            var branch = await response.Content.ReadFromJsonAsync<BranchModel>(_jsonOpts, ct);
            if (branch is not null) return ApiResponse<BranchModel>.Ok(branch);
        }
        return ApiResponse<BranchModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<bool>> DeactivateAsync(string id, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/branches/{id}");
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode) return ApiResponse<bool>.Ok(true);
        return ApiResponse<bool>.Fail(await ReadProblemAsync(response));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOpts);
            return problem ?? UnknownError((int)response.StatusCode);
        }
        catch
        {
            return UnknownError((int)response.StatusCode);
        }
    }

    private static ProblemDetails UnknownError(int status) => new()
    {
        Type   = "https://errors.nexusha.local/unknown",
        Title  = "Unexpected Error",
        Status = status,
        Detail = $"An unexpected error occurred (HTTP {status})."
    };

    // Raw DTO matching the API's PagedBranch schema
    private sealed class PagedBranchRaw
    {
        public int Page { get; set; }
        public int Size { get; set; }
        public int TotalCount { get; set; }
        public List<BranchModel> Items { get; set; } = [];
    }
}
