using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Web.Client.Models.Common;
using Nexus.Web.Client.Models.Patients;

namespace Nexus.Web.Client.Services.Patients;

public sealed class PatientService : IPatientService
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    // PATCH bodies must omit null fields — backend validates minLength on present fields only
    private static readonly JsonSerializerOptions _patchOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PatientService(HttpClient http) => _http = http;

    public async Task<ApiResponse<PagedResult<PatientModel>>> ListAsync(
        int page = 1,
        int size = 20,
        string? status = null,
        string? branchId = null,
        string? q = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder($"/api/v1/patients?page={page}&size={size}");
        if (!string.IsNullOrWhiteSpace(status))   sb.Append($"&status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(branchId)) sb.Append($"&branchId={Uri.EscapeDataString(branchId)}");
        if (!string.IsNullOrWhiteSpace(q))        sb.Append($"&q={Uri.EscapeDataString(q)}");

        var response = await _http.GetAsync(sb.ToString(), ct);
        if (response.IsSuccessStatusCode)
        {
            var paged = await response.Content.ReadFromJsonAsync<PagedResult<PatientModel>>(_jsonOpts, ct);
            if (paged is not null) return ApiResponse<PagedResult<PatientModel>>.Ok(paged);
        }
        return ApiResponse<PagedResult<PatientModel>>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<PatientModel>> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/patients/{id}", ct);
        if (response.IsSuccessStatusCode)
        {
            var patient = await response.Content.ReadFromJsonAsync<PatientModel>(_jsonOpts, ct);
            if (patient is not null) return ApiResponse<PatientModel>.Ok(patient);
        }
        return ApiResponse<PatientModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<PatientModel>> CreateAsync(PatientCreateRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, _jsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients") { Content = content };
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode)
        {
            var patient = await response.Content.ReadFromJsonAsync<PatientModel>(_jsonOpts, ct);
            if (patient is not null) return ApiResponse<PatientModel>.Ok(patient);
        }
        return ApiResponse<PatientModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<PatientModel>> PatchAsync(string id, PatientPatchRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, _patchOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/patients/{id}") { Content = content };
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode)
        {
            var patient = await response.Content.ReadFromJsonAsync<PatientModel>(_jsonOpts, ct);
            if (patient is not null) return ApiResponse<PatientModel>.Ok(patient);
        }
        return ApiResponse<PatientModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<PatientModel>> ActivateAsync(string id, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/{id}/activate");
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode)
        {
            var patient = await response.Content.ReadFromJsonAsync<PatientModel>(_jsonOpts, ct);
            if (patient is not null) return ApiResponse<PatientModel>.Ok(patient);
        }
        return ApiResponse<PatientModel>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<PatientModel>> ArchiveAsync(string id, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/{id}/archive");
        httpRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await _http.SendAsync(httpRequest, ct);
        if (response.IsSuccessStatusCode)
        {
            var patient = await response.Content.ReadFromJsonAsync<PatientModel>(_jsonOpts, ct);
            if (patient is not null) return ApiResponse<PatientModel>.Ok(patient);
        }
        return ApiResponse<PatientModel>.Fail(await ReadProblemAsync(response));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(opts);
            if (problem is not null) return problem;
        }
        catch { /* fall through */ }

        return new ProblemDetails
        {
            Title  = $"HTTP {(int)response.StatusCode}",
            Status = (int)response.StatusCode,
            Detail = "An unexpected error occurred."
        };
    }
}
