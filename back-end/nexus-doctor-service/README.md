# Nexus HA — Doctor Service

Doctor registry on port **10000**. `Pending → Verified → Approved → Active → Deactivated` lifecycle (with `Deactivated → Active` re-activation), multipart verification document uploads with SHA-256 dedup, license uniqueness, branch linking.

## Stack

- .NET 10 / ASP.NET Core Web API on port **10000**
- SQL Server (database: `NexusDoctors`), EF Core 10 (code-first)
- JWT bearer authN against `nexus-identity-service` JWKS
- Outbound `BranchesClient` against `nexus-branch-service` (resilient HttpClient with circuit breaker)
- Outbound audit publishing to `nexus-audit-service` (fail-open + local outbox)
- `IDocumentStorage` abstraction with `LocalFileSystemDocumentStorage` writing to `/var/data/nexus/doctor-docs/`
- Serilog + OpenTelemetry (OTLP gRPC) + Prometheus `/metrics`
- Swashbuckle Swagger + ReDoc

## Quick start

```powershell
dotnet build Nexus.Doctor.sln
dotnet run --project src/Nexus.Doctor.Api
# http://localhost:10000
```

## Endpoints (final shape — built incrementally across phases)

| Phase | Method | Route | OperationId |
|---|---|---|---|
| 6 | GET | `/api/v1/doctors` | `listDoctors` |
| 6 | POST | `/api/v1/doctors` | `createDoctor` |
| 6 | GET | `/api/v1/doctors/{id}` | `getDoctorById` |
| 6 | PATCH | `/api/v1/doctors/{id}` | `patchDoctor` |
| 6 | POST | `/api/v1/doctors/{id}/verify` | `verifyDoctor` |
| 6 | POST | `/api/v1/doctors/{id}/approve` | `approveDoctor` |
| 6 | POST | `/api/v1/doctors/{id}/activate` | `activateDoctor` |
| 6 | POST | `/api/v1/doctors/{id}/deactivate` | `deactivateDoctor` |
| 6 | GET | `/api/v1/doctors/{id}/branches` | `listDoctorBranches` |
| 6 | POST | `/api/v1/doctors/{id}/branches` | `linkDoctorToBranch` |
| 6 | DELETE | `/api/v1/doctors/{id}/branches/{branchId}` | `unlinkDoctorFromBranch` |
| 7 | GET | `/api/v1/doctors/{id}/documents` | `listDoctorDocuments` |
| 7 | POST | `/api/v1/doctors/{id}/documents` | `uploadDoctorDocument` |
| 7 | GET | `/api/v1/doctors/{id}/documents/{docId}` | `getDoctorDocument` |
| 7 | PATCH | `/api/v1/doctors/{id}/documents/{docId}` | `reviewDoctorDocument` |
| 7 | DELETE | `/api/v1/doctors/{id}/documents/{docId}` | `deleteDoctorDocument` |

Plus `/health/*`, `/metrics`, `/swagger`, `/redoc`.

## Documents

- `docs/IMPLEMENTATION_PLAN.md` — engineering blueprint
- `docs/CONTRACT_TESTING.md` — manual contract playbook
- `../../specs/doctors.openapi.json` — authoritative OpenAPI spec

See `CHANGELOG.md` and `TROUBLESHOOTING.md`.
