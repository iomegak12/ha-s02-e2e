# Nexus HA — Identity & Admin Service

Self-hosted identity provider for Nexus HA hospital administrators. Issues and validates JWTs, manages admin accounts, and publishes audit events to the audit service.

> Status: **Phase 0 — scaffolding only.** No business logic yet. See [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) for the authoritative build plan.

## Tech stack

- .NET 10 (`net10.0`), ASP.NET Core minimal hosting
- EF Core 10 against SQL Server (added in Phase 2)
- FluentValidation, Swashbuckle + ReDoc, Serilog, OpenTelemetry, Prometheus (added in Phase 1)

## Prerequisites

- .NET 10 SDK
- SQL Server (LocalDB or container) — required from Phase 2 onward
- Docker (optional, required from Phase 8 onward)

## Quick start

```powershell
dotnet build Nexus.Identity.sln
dotnet run --project src/Nexus.Identity.Api
# In another shell:
curl http://localhost:8000/health/live
```

## Running tests

```powershell
dotnet test Nexus.Identity.sln
```

## Layout

```
nexus-identity-service/
├── src/Nexus.Identity.Api/        ASP.NET Core service
├── tests/Nexus.Identity.Api.Tests/ xUnit test project
├── docs/                           IMPLEMENTATION_PLAN.md, CONTRACT_TESTING.md
├── README.md
├── CHANGELOG.md
├── TROUBLESHOOTING.md
├── CONTRIBUTING.md
└── LICENSE
```

## Documentation

- [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) — authoritative build plan
- [docs/CONTRACT_TESTING.md](docs/CONTRACT_TESTING.md) — manual contract test playbook
- [../../specs/identity.openapi.json](../../specs/identity.openapi.json) — source spec

## License

MIT — see [LICENSE](LICENSE).
