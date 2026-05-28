# Nexus HA — Patient Service

Patient registry on port **9000**. `Draft → Active → Archived` lifecycle, phone-and-DOB / email-and-DOB duplicate detection, many-to-many branch links with primary-branch invariant, 30-day archived-patient purge with audit PII redaction.

## Stack

- .NET 10 / ASP.NET Core Web API on port **9000**
- SQL Server (database: `NexusPatients`), EF Core 10 (code-first)
- JWT bearer authN against `nexus-identity-service` JWKS
- Outbound `BranchesClient` against `nexus-branch-service` (resilient HttpClient with circuit breaker)
- Outbound audit publishing to `nexus-audit-service` (fail-open + local outbox)
- Serilog + OpenTelemetry (OTLP gRPC) + Prometheus `/metrics`
- Swashbuckle Swagger + ReDoc

## Quick start

```powershell
dotnet build Nexus.Patient.sln
dotnet run --project src/Nexus.Patient.Api
# http://localhost:9000
```

## Endpoints (final shape — built incrementally across phases)

| Method | Route | OperationId |
|---|---|---|
| GET | `/api/v1/patients` | `listPatients` |
| POST | `/api/v1/patients` | `createPatient` |
| GET | `/api/v1/patients/{id}` | `getPatientById` |
| PATCH | `/api/v1/patients/{id}` | `patchPatient` |
| POST | `/api/v1/patients/{id}/activate` | `activatePatient` |
| POST | `/api/v1/patients/{id}/archive` | `archivePatient` |
| GET | `/api/v1/patients/{id}/branches` | `listPatientBranches` |
| POST | `/api/v1/patients/{id}/branches` | `linkPatientToBranch` |
| DELETE | `/api/v1/patients/{id}/branches/{branchId}` | `unlinkPatientFromBranch` |

Plus `/health/*`, `/metrics`, `/swagger`, `/redoc`.

## Documents

- `docs/IMPLEMENTATION_PLAN.md` — engineering blueprint
- `docs/CONTRACT_TESTING.md` — manual contract playbook
- `../../specs/patients.openapi.json` — authoritative OpenAPI spec

See `CHANGELOG.md` and `TROUBLESHOOTING.md`.
