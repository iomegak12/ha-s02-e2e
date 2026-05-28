# Nexus HA — Audit Service

The append-only audit sink for the Nexus HA platform. Every mutating service
(`Identity`, `Patients`, `Doctors`, `Branches`) posts who-changed-what-when
records here. **This service is the sink — it does not publish to itself.**

## Stack

- .NET 10 / ASP.NET Core Web API
- SQL Server (database: `NexusAudit`), EF Core 10 (code-first)
- JWT bearer authN against `nexus-identity-service` JWKS
- Serilog + OpenTelemetry (OTLP gRPC) + Prometheus `/metrics`
- Swashbuckle Swagger + ReDoc

## Quick start

```powershell
dotnet build Nexus.Audit.sln
dotnet run --project src/Nexus.Audit.Api
# http://localhost:12000
```

Docker:

```powershell
docker build -t nexus-audit-service:dev .
docker run --rm -p 12000:12000 `
  -e ConnectionStrings__Default="Server=host.docker.internal,1433;Database=NexusAudit;User Id=sa;Password=...;TrustServerCertificate=True;Encrypt=False" `
  nexus-audit-service:dev
```

## Endpoints

| Method | Route | OperationId |
|---|---|---|
| POST | `/api/v1/audit` | `appendAuditEntry` |
| GET | `/api/v1/audit` | `queryAuditEntries` |
| GET | `/api/v1/audit/{id}` | `getAuditEntryById` |
| GET | `/health`, `/health/live`, `/health/ready` | |
| GET | `/metrics` | Prometheus |
| GET | `/swagger`, `/redoc` | OpenAPI UIs |

## Configuration matrix

| Section | Key | Default | Notes |
|---|---|---|---|
| `Kestrel:Endpoints:Http:Url` | | `http://+:12000` | listening port |
| `ConnectionStrings:Default` | | (unset) | SQL Server connection |
| `Auth:Authority` | | `http://nexus-identity-service:8000` | JWKS metadata host |
| `Auth:Audience` | | `nexus-ha` | |
| `Auth:Issuer` | | `https://api.nexusha.local/identity` | |
| `Persistence:AutoMigrate` | | `false` | Dev: `true` |
| `RateLimiting:Enabled` | | `false` | source-service partitioned when on |
| `Idempotency:RetentionDays` | | `7` | idempotency record TTL |
| `Idempotency:MaxClockSkewMinutes` | | `5` | future-time tolerance |
| `Observability:Otlp:Endpoint` | | (empty in Dev) | Prod: collector endpoint |

## Static documentation site

The `docs/` directory contains a Redocly setup that renders
`specs/audit.openapi.json` as a static site.

```powershell
cd docs
npm install
npm run docs:lint     # sanity-check the spec
npm run docs:build    # writes site/index.html (gitignored)
npm run docs:preview  # local preview server
```

## Verification recipe

```powershell
dotnet build Nexus.Audit.sln
dotnet test Nexus.Audit.sln --collect "XPlat Code Coverage"
docker build -t nexus-audit-service:dev .
docker run --rm -p 12000:12000 -e ConnectionStrings__Default="<sql>" nexus-audit-service:dev
curl -s http://localhost:12000/health/ready
```

Then walk `docs/CONTRACT_TESTING.md` end-to-end against a running
`nexus-identity-service` + SQL Server stack.

## Documents

- `docs/IMPLEMENTATION_PLAN.md` — engineering blueprint
- `docs/CONTRACT_TESTING.md` — manual contract playbook
- `docs/redocly.yaml` + `docs/package.json` — Redocly static-docs setup
- `../../specs/audit.openapi.json` — authoritative OpenAPI spec

See `CHANGELOG.md` for release history and `TROUBLESHOOTING.md` for common
operational issues.
