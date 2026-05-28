using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Doctors;

[Collection("stack")]
public sealed class DoctorsSmokeTests
{
    private readonly StackFixture _fx;
    public DoctorsSmokeTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.DoctorsBaseUrl);
        var response = await http.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_exposes_all_16_doctor_operationIds_including_documents()
    {
        using var http = _fx.Client(_fx.Settings.DoctorsBaseUrl);
        var doc = await http.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var operationIds = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(m => m.Value.TryGetProperty("operationId", out _))
            .Select(m => m.Value.GetProperty("operationId").GetString())
            .ToList();

        operationIds.Should().Contain(new[]
        {
            "listDoctors", "createDoctor", "getDoctorById", "patchDoctor",
            "verifyDoctor", "approveDoctor", "activateDoctor", "deactivateDoctor",
            "listDoctorBranches", "linkDoctorToBranch", "unlinkDoctorFromBranch",
            "listDoctorDocuments", "uploadDoctorDocument", "getDoctorDocument",
            "reviewDoctorDocument", "deleteDoctorDocument",
        });
    }
}
