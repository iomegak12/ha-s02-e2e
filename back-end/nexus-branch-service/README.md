# Nexus HA — Branch Service

Master data for hospital branches. The **gating dependency** for Patients and
Doctors (both validate `branchId` references against this service at link time).

## Stack

- .NET 10 / ASP.NET Core Web API on port **11000**
- SQL Server (database: `NexusBranches`), EF Core 10 (code-first)
- JWT bearer authN against `nexus-identity-service` JWKS
- Outbound audit publishing to `nexus-audit-service` (fail-open + local outbox)
- Serilog + OpenTelemetry (OTLP gRPC) + Prometheus `/metrics`
- Swashbuckle Swagger + ReDoc

## Quick start

```powershell
dotnet build Nexus.Branch.sln
dotnet run --project src/Nexus.Branch.Api
# http://localhost:11000
```

## Endpoints (final shape — built incrementally across phases)

| Method | Route | OperationId |
|---|---|---|
| GET | `/api/v1/branches` | `listBranches` |
| POST | `/api/v1/branches` | `createBranch` |
| GET | `/api/v1/branches/{id}` | `getBranchById` |
| PATCH | `/api/v1/branches/{id}` | `patchBranch` |
| DELETE | `/api/v1/branches/{id}` | `deactivateBranch` |
| GET | `/health`, `/health/live`, `/health/ready` | |
| GET | `/metrics` | Prometheus |
| GET | `/swagger`, `/redoc` | OpenAPI UIs |

## Documents

- `docs/IMPLEMENTATION_PLAN.md` — engineering blueprint
- `docs/CONTRACT_TESTING.md` — manual contract playbook
- `../../specs/branches.openapi.json` — authoritative OpenAPI spec

See `CHANGELOG.md` for release history and `TROUBLESHOOTING.md` for common
operational issues.
