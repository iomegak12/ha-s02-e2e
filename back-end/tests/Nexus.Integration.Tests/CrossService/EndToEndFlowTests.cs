using System.Net.Http.Headers;
using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.CrossService;

/// <summary>
/// Headline end-to-end proof of the Phase 9 stack:
/// branch → patient → doctor (+ document upload + verify) → patient archive
/// → audit trail contains every step.
/// </summary>
[Collection("stack")]
public sealed class EndToEndFlowTests
{
    private readonly StackFixture _fx;
    public EndToEndFlowTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task Full_flow_branch_patient_doctor_document_audit()
    {
        // Spec: ^[A-Z]{2,5}$ — must be 2-5 uppercase letters, no digits.
        var rng = Random.Shared;
        var branchCode = new string(Enumerable.Range(0, 4).Select(_ => (char)('A' + rng.Next(0, 26))).ToArray());
        var phone = $"+91-9{Random.Shared.Next(100000000, 999999999)}";
        var email = $"e2e+{Guid.NewGuid():N}@example.com";
        var license = $"E2E-LIC-{Guid.NewGuid():N}".Substring(0, 32);

        // 1. CREATE BRANCH ----------------------------------------------------------
        using var branches = _fx.AuthClient(_fx.Settings.BranchesBaseUrl);
        var createBranch = new HttpRequestMessage(HttpMethod.Post, "/api/v1/branches/")
        {
            Content = JsonContent.Create(new
            {
                code = branchCode,
                name = $"E2E Branch {branchCode}",
                city = "Bengaluru",
                isActive = true,
            }),
        };
        createBranch.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        var branchResp = await branches.SendAsync(createBranch);
        branchResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var branchBody = await branchResp.Content.ReadFromJsonAsync<JsonElement>();
        var branchId = branchBody.GetProperty("id").GetGuid();

        try
        {
            // 2. CREATE + ACTIVATE PATIENT ---------------------------------------------
            using var patients = _fx.AuthClient(_fx.Settings.PatientsBaseUrl);
            var createPatient = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/")
            {
                Content = JsonContent.Create(new
                {
                    firstName = "E2E",
                    lastName = "Patient",
                    dateOfBirth = "1990-01-01",
                    gender = "Male",
                    primaryPhone = phone,
                    email,
                    addressLine1 = "1 Test Lane",
                    city = "Bengaluru",
                    state = "KA",
                    postalCode = "560001",
                    country = "IN",
                    primaryBranchId = branchId,
                }),
            };
            createPatient.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            var patientResp = await patients.SendAsync(createPatient);
            patientResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var patientBody = await patientResp.Content.ReadFromJsonAsync<JsonElement>();
            var patientId = patientBody.GetProperty("id").GetGuid();
            patientBody.GetProperty("status").GetString().Should().Be("Draft");

            var activate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/{patientId}/activate");
            activate.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            var activateResp = await patients.SendAsync(activate);
            activateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. CREATE DOCTOR + UPLOAD MEDICAL LICENSE DOCUMENT ----------------------
            using var doctors = _fx.AuthClient(_fx.Settings.DoctorsBaseUrl);
            var createDoctor = new HttpRequestMessage(HttpMethod.Post, "/api/v1/doctors/")
            {
                Content = JsonContent.Create(new
                {
                    firstName = "E2E",
                    lastName = "Doctor",
                    specialty = "Cardiology",
                    licenseNumber = license,
                    primaryPhone = phone,
                    email,
                    primaryBranchId = branchId,
                }),
            };
            createDoctor.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            var doctorResp = await doctors.SendAsync(createDoctor);
            doctorResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var doctorBody = await doctorResp.Content.ReadFromJsonAsync<JsonElement>();
            var doctorId = doctorBody.GetProperty("id").GetGuid();

            // Multipart upload of a tiny PDF body.
            var pdfBytes = System.Text.Encoding.UTF8.GetBytes($"%PDF-1.4 e2e {Guid.NewGuid():N}");
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("MedicalLicense"), "kind");
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            form.Add(fileContent, "file", "license.pdf");
            var uploadReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/doctors/{doctorId}/documents/")
            {
                Content = form,
            };
            uploadReq.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            var uploadResp = await doctors.SendAsync(uploadReq);
            uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var uploadBody = await uploadResp.Content.ReadFromJsonAsync<JsonElement>();
            var docId = uploadBody.GetProperty("id").GetGuid();

            // Review the document → Verified, then verify the doctor.
            var review = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/doctors/{doctorId}/documents/{docId}")
            {
                Content = JsonContent.Create(new { status = "Verified" }),
            };
            review.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            (await doctors.SendAsync(review)).StatusCode.Should().Be(HttpStatusCode.OK);

            var verify = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/doctors/{doctorId}/verify");
            verify.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            (await doctors.SendAsync(verify)).StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. ARCHIVE THE PATIENT ----------------------------------------------------
            var archive = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/patients/{patientId}/archive");
            archive.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            (await patients.SendAsync(archive)).StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. ASSERT AUDIT TRAIL -----------------------------------------------------
            // The publisher is fail-open and the audit POST happens after the business
            // write — give it a brief moment to make its way through the chain.
            await WaitForAuditAsync(patientId, "Patient.Created");
            await WaitForAuditAsync(patientId, "Patient.StateChanged");
            await WaitForAuditAsync(doctorId, "Doctor.Created");
            await WaitForAuditAsync(docId, "DoctorDocument.Uploaded");
            await WaitForAuditAsync(doctorId, "Doctor.StateChanged");
        }
        finally
        {
            // Best-effort branch cleanup so reruns are idempotent.
            var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/branches/{branchId}");
            del.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
            await branches.SendAsync(del);
        }
    }

    private async Task WaitForAuditAsync(Guid entityId, string action, int maxAttempts = 80)
    {
        using var audit = _fx.AuthClient(_fx.Settings.AuditBaseUrl);
        for (var i = 0; i < maxAttempts; i++)
        {
            var resp = await audit.GetAsync($"/api/v1/audit?entityId={entityId}&action={action}&page=1&size=10");
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
                if (body.GetProperty("totalCount").GetInt64() >= 1) return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        Assert.Fail($"Audit entry for entityId={entityId} action={action} did not appear within {maxAttempts} attempts.");
    }
}
