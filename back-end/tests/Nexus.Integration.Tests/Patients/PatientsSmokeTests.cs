using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Patients;

[Collection("stack")]
public sealed class PatientsSmokeTests
{
    private readonly StackFixture _fx;
    public PatientsSmokeTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.PatientsBaseUrl);
        var response = await http.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_exposes_all_9_patient_operationIds()
    {
        using var http = _fx.Client(_fx.Settings.PatientsBaseUrl);
        var doc = await http.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var operationIds = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(m => m.Value.TryGetProperty("operationId", out _))
            .Select(m => m.Value.GetProperty("operationId").GetString())
            .ToList();

        operationIds.Should().Contain(new[]
        {
            "listPatients", "createPatient", "getPatientById", "patchPatient",
            "activatePatient", "archivePatient",
            "listPatientBranches", "linkPatientToBranch", "unlinkPatientFromBranch",
        });
    }

    [Fact]
    public async Task List_requires_admin_bearer()
    {
        using var http = _fx.Client(_fx.Settings.PatientsBaseUrl);
        var response = await http.GetAsync("/api/v1/patients");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
