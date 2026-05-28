# Nexus HA — Audit Service: Implementation Plan

> **Audience:** the developer building `nexus-audit-service` independently.
> **Authoritative references:** [`../../../specs/audit.openapi.json`](../../../specs/audit.openapi.json), [`../../../docs/LLD_Nexus_HA.md`](../../../docs/LLD_Nexus_HA.md) (§14), [`../../../docs/HLD_Nexus_HA.md`](../../../docs/HLD_Nexus_HA.md), [`../../../docs/ADR_Nexus_HA.md`](../../../docs/ADR_Nexus_HA.md).
> **Independence rule:** self-contained, no project references, no shared NuGet, no shared source.

---

## 1. Scope

| OpenAPI tag | Operations | Feature folder |
|---|---|---|
| `Audit` | `appendAuditEntry`, `queryAuditEntries`, `getAuditEntryById` | `Features/Audit` |

Listening port: **12000**.

This service is the **audit sink** — it is the destination every other service publishes to. **It does NOT publish to itself.** Storage is append-only; updates and deletes are not permitted via the API.

---

## 2. Solution & project layout

```
nexus-audit-service/
├── Nexus.Audit.sln
├── src/Nexus.Audit.Api/Nexus.Audit.Api.csproj    (net10.0, Web SDK)
├── tests/Nexus.Audit.Api.Tests/Nexus.Audit.Api.Tests.csproj
├── docs/, README.md, CHANGELOG.md, TROUBLESHOOTING.md, CONTRIBUTING.md, LICENSE
├── .gitignore, .dockerignore, .editorconfig
└── Dockerfile
```

csproj baseline same as siblings.

---

## 3. Feature-folder structure

```
Program.cs
Features/
└── Audit/
    ├── Models/        (AuditEntry, AppendAuditEntryRequest, AuditEntryListItem, PagedAuditEntries, IdempotencyRecord)
    ├── Validators/    (AppendAuditEntryRequestValidator)
    ├── Repository/    (IAuditEntryRepository, AuditEntryRepository, IIdempotencyRepository, IdempotencyRepository)
    ├── Service/       (IAuditService, AuditService)
    └── Controller/    (AuditController)

Infrastructure/
├── Auth/              (JwtBearerConfig via Identity JWKS, AdminRolePolicy, ServiceOrAdminPolicy, CurrentUser)
├── Persistence/       (AuditDbContext, Migrations/)
├── Observability/     (SerilogBootstrap, OtelBootstrap, HealthCheckRegistration)
├── Errors/            (ProblemDetailsMiddleware, DomainException, ErrorCodes)
├── Pagination/
├── Idempotency/       (IdempotencyKeyFilter, IdempotencyKeyOptions)
└── Startup/

Configuration/
├── JwtBearerOptions.cs
├── RateLimitingOptions.cs
├── CorsOptions.cs
├── IdempotencyOptions.cs
└── ObservabilityOptions.cs

appsettings.json, appsettings.Development.json, appsettings.Production.json
```

**No** `Resilience/`, `Audit/`, `Clients/` infrastructure — this service has no outbound HTTP dependencies.

---

## 4. Persistence (EF Core 10, code-first)

DbContext `AuditDbContext` → database **`NexusAudit`**.

- `AuditEntry` → `AuditEntries`:
  - `Id` `uniqueidentifier` PK
  - `EntityType` `nvarchar(64)` (e.g. `Patient`, `Doctor`, `Branch`, `Admin`, `DoctorDocument`, `PatientBranch`, `DoctorBranch`)
  - `EntityId` `uniqueidentifier`
  - `EntityCode` `nvarchar(64)?` (optional human handle, e.g. branch code)
  - `Action` `nvarchar(32)` (`Created`, `Updated`, `StatusChanged`, `Linked`, `Unlinked`, `Reviewed`, `Uploaded`, `Deleted`, `Purged`)
  - `ActorId` `uniqueidentifier`
  - `ActorUsername` `nvarchar(64)`
  - `OccurredAtUtc` `datetime2(7)`
  - `ReceivedAtUtc` `datetime2(7)` (server-stamped)
  - `Summary` `nvarchar(512)`
  - `DiffJson` `nvarchar(max)?`
  - `SourceService` `nvarchar(64)` (server-derived from JWT `iss` / explicit header `X-Source-Service`)
  - `IdempotencyKey` `nvarchar(128)`
  - Indexes:
    - `IX_AuditEntries_EntityTypeEntityId` (EntityType, EntityId, OccurredAtUtc DESC)
    - `IX_AuditEntries_ActorId` (ActorId, OccurredAtUtc DESC)
    - `IX_AuditEntries_OccurredAtUtc` (OccurredAtUtc DESC)
    - Filtered unique `IX_AuditEntries_Idem` on `(SourceService, IdempotencyKey)`
- `IdempotencyRecord` → `IdempotencyRecords` (SourceService, IdempotencyKey, EntryId FK, CreatedAtUtc) — backing the filter, with retention TTL handled by a background `IdempotencyTrimWorker` (default 7-day retention).

**Entities are immutable after insert.** No `Update` paths in the repository.

---

## 5. Validation (FluentValidation)

- `AppendAuditEntryRequestValidator` — `entityType` 1–64, `entityId` GUID, `action` enum, `actorId` GUID, `actorUsername` 1–64, `occurredAtUtc` not future (>5-minute skew rejected), `summary` 1–512.
- Auto-invocation via `ValidationFilter<T>` endpoint filter → `422 AUDIT_VALIDATION`.

---

## 6. Error model (RFC 9457 ProblemDetails)

| HTTP | code | When |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed JSON |
| 400 | `IDEMPOTENCY_KEY_MISSING` | `appendAuditEntry` without `Idempotency-Key` header |
| 401 | `UNAUTHENTICATED` | Missing/invalid bearer |
| 403 | `UNAUTHORIZED` | Authenticated but not allowed role |
| 404 | `NOT_FOUND` | Entry id unknown |
| 409 | `IDEMPOTENCY_CONFLICT` | Same `(SourceService, Idempotency-Key)` already used with **different** payload |
| 422 | `AUDIT_VALIDATION` | FluentValidation failure |
| 429 | `RATE_LIMITED` | Rate limit hit |

Replays with the **same** `(SourceService, Idempotency-Key)` and **identical** payload return the **original** response (200/201 with stored entry).

---

## 7. AuthN/Z

JwtBearer: Authority `http://nexus-identity-service:8000`, MetadataAddress `<authority>/api/v1/auth/jwks`, Audience `nexus-ha`, Issuer `https://api.nexusha.local/identity`. `MapInboundClaims=false`.

**Policies:**
- `AdminOnly` — required on `queryAuditEntries`, `getAuditEntryById` (read endpoints).
- `AdminOnly` is **also** required on `appendAuditEntry` — every write must carry a valid admin bearer (which the calling service forwards from the original user request). This is the design constraint: services do not have their own service identities in Phase 1; they relay user tokens.

**Source-service hint:** request header `X-Source-Service` (optional) annotates `SourceService`. If absent, derived from JWT `iss`. The idempotency key uniqueness is **scoped by `SourceService`**, so collisions across services are impossible.

---

## 8. Resilience

Not applicable — no outbound HTTP.

---

## 9. Audit publishing

Not applicable — this **is** the audit sink. No `IAuditPublisher` in this service.

---

## 10. Observability

Serilog (console JSON Prod / coloured Dev) + rolling file `/var/log/nexus/audit/audit-.log` 7-day retention. OpenTelemetry .NET OTLP gRPC, instrumentation `AspNetCore`, `HttpClient` (incoming bearer fwd), `EntityFrameworkCore`, `Runtime`, `Process`. Prometheus `/metrics`. Custom metrics:
- `nexus_audit_entries_appended_total{sourceService,entityType,action}`
- `nexus_audit_idempotency_replays_total{sourceService}`
- `nexus_audit_idempotency_conflicts_total{sourceService}`

Health: `/health/live`, `/health/ready` (SQL Server), `/health` combined.

---

## 11. Startup banner & summary

`Spectre.Console` Figlet "Nexus Audit". Panels: Configuration (port 12000, env, JWT authority/audience, idempotency retention days), Dependencies (SQL Server, OTLP), Toggles (rate limiting, CORS, Swagger/ReDoc/metrics).

---

## 12. Rate limiting

Fixed-window IP, `PermitLimit=100`, `WindowSeconds=60`, off by default. Append endpoint has a **higher** ceiling when enabled (`PermitLimit=500` per minute per source-service) — recommended to switch from IP partition to source-service partition for the append endpoint:

```csharp
options.AddPolicy("source-svc-fixed", ctx => RateLimitPartition.GetFixedWindowLimiter(
    ctx.Request.Headers.TryGetValue("X-Source-Service", out var v) ? v.ToString() : "unknown",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 500, Window = TimeSpan.FromSeconds(60) }));
```

`RateLimiting:Enabled=false` keeps everything bypassed.

---

## 13. CORS

Default `{ *, *, *, false }`. Audit is typically called only by sibling services (no browser origin), but the default is permissive in Phase 1.

---

## 14. Configuration files

`appsettings.json`:

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://+:12000" } } },
  "ConnectionStrings": { "Default": "<provided-later>" },
  "Auth": { "Authority": "http://nexus-identity-service:8000", "Audience": "nexus-ha",
            "Issuer": "https://api.nexusha.local/identity" },
  "Persistence": { "AutoMigrate": false, "FailFast": false },
  "RateLimiting": { "Enabled": false, "PermitLimit": 100, "WindowSeconds": 60, "AppendPermitLimit": 500 },
  "Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false },
  "Idempotency": { "RetentionDays": 7, "MaxClockSkewMinutes": 5, "TrimIntervalMinutes": 60 },
  "Observability": { "Otlp": { "Endpoint": "", "Protocol": "Grpc" }, "Prometheus": { "Enabled": true } },
  "Logging": { "File": { "Path": "logs/audit-.log", "RetainedFileCountLimit": 7 } }
}
```

`appsettings.Development.json`: `Persistence:AutoMigrate=true`.
`appsettings.Production.json`: `Observability:Otlp:Endpoint=http://otel-collector:4317`, `Persistence:FailFast=true`.

All options bound with `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

## 15. OpenAPI surface

Swashbuckle `/swagger/v1/swagger.json`, Swagger UI `/swagger`, ReDoc `/redoc`. XML doc comments. `OperationFilter` attaches `Idempotency-Key` (required) and `X-Source-Service` (optional) to `appendAuditEntry`.

---

## 16. Static documentation (Redocly CLI)

`docs/redocly.yaml`:

```yaml
extends: [recommended]
apis:
  audit@v1:
    root: ../../../specs/audit.openapi.json
```

`docs/site/` gitignored.

---

## 17. Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Nexus.Audit.sln", "./"]
COPY ["src/Nexus.Audit.Api/Nexus.Audit.Api.csproj", "src/Nexus.Audit.Api/"]
COPY ["tests/Nexus.Audit.Api.Tests/Nexus.Audit.Api.Tests.csproj", "tests/Nexus.Audit.Api.Tests/"]
RUN dotnet restore "Nexus.Audit.sln"
COPY . .
RUN dotnet publish "src/Nexus.Audit.Api/Nexus.Audit.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:12000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 12000
ENTRYPOINT ["dotnet", "Nexus.Audit.Api.dll"]
```

`.dockerignore`: `**/bin`, `**/obj`, `**/.vs`, `docs/site`, `logs`, `*.user`, `.git`, `tests`.

---

## 18. Test project

```
tests/Nexus.Audit.Api.Tests/
├── Features/Audit/
│   ├── Validators/   (AppendAuditEntryRequestValidatorTests — including clock-skew rejection)
│   ├── Repository/   (AuditEntryRepositoryTests, IdempotencyRepositoryTests — EF InMemory)
│   ├── Service/      (AuditServiceTests — IDEMPOTENCY_CONFLICT vs replay vs new insert)
│   └── Controller/   (AuditControllerTests — header requirements, replay semantics)
├── Infrastructure/
│   ├── Errors/       (ProblemDetailsMiddlewareTests)
│   ├── Idempotency/  (IdempotencyKeyFilterTests)
│   └── Auth/         (JwtBearer policy tests using TestServer or NSubstitute auth handler)
├── Fakers/           (AuditEntryFaker, AppendAuditEntryRequestFaker)
└── Nexus.Audit.Api.Tests.csproj
```

Packages: `xunit`, `FluentAssertions`, `NSubstitute`, `Bogus`, `coverlet.collector`, `Microsoft.EntityFrameworkCore.InMemory`. Coverage target ≥80% line.

Background worker `IdempotencyTrimWorker` tested via fake clock.

---

## 19. Documentation comments

XML doc comments on every public type/member of validators, repositories, services, controllers, options, middleware, the idempotency filter. `GenerateDocumentationFile=true`.

---

## 20. Top-level service artifacts

`README.md` (overview, stack, quick start, config, "this service is the sink — it does not publish"), `CHANGELOG.md`, `TROUBLESHOOTING.md` (DB unreachable, JWKS refresh failure, idempotency conflicts, clock skew), `CONTRIBUTING.md`, `LICENSE` (MIT 2026 Nexus HA Engineering), `.gitignore`, `.editorconfig`.

---

## 21. Audit-specific business rules

- **Idempotency**: `appendAuditEntry` MUST include `Idempotency-Key`. The pair `(SourceService, IdempotencyKey)` is unique. On replay with identical payload → return the original stored entry (200 with `X-Idempotent-Replay: true` header). With **different** payload → `409 IDEMPOTENCY_CONFLICT`.
- **Clock skew**: `occurredAtUtc` must be ≤ `ReceivedAtUtc + MaxClockSkewMinutes` and ≥ `ReceivedAtUtc - 30 days`. Outside → `422 AUDIT_VALIDATION`.
- **Append-only**: no `PUT`/`PATCH`/`DELETE` on entries. `IAuditEntryRepository` exposes only `AddAsync`, `GetByIdAsync`, `QueryAsync`.
- **Query filters** (`queryAuditEntries`): `entityType`, `entityId`, `action`, `actorId`, `from`, `to`, `sourceService`, `page`, `size`, `sort` (default `occurredAtUtc desc`).
- **Retention** of `IdempotencyRecord`: `IdempotencyTrimWorker` runs every `TrimIntervalMinutes` and deletes records older than `RetentionDays`. The audit entry itself is **never deleted** by this service.

---

## 22. Verification

```powershell
dotnet build Nexus.Audit.sln
dotnet test  Nexus.Audit.sln --collect "XPlat Code Coverage"
docker build -t nexus-audit-service:dev .
docker run --rm -p 12000:12000 -e ConnectionStrings__Default="<sql>" nexus-audit-service:dev
curl -s http://localhost:12000/health/ready
```

Plus in `docs/`:

```powershell
npx @redocly/cli lint ../../specs/audit.openapi.json
npm run docs:build
```

Acceptance: tests green; `/swagger/v1/swagger.json` exposes all 3 operationIds from [`../../../specs/audit.openapi.json`](../../../specs/audit.openapi.json) (`appendAuditEntry`, `queryAuditEntries`, `getAuditEntryById`); container boots with banner; manual contract tests pass per [`CONTRACT_TESTING.md`](CONTRACT_TESTING.md).

---

## Open items for the dev to confirm

1. Should reads (`queryAuditEntries`, `getAuditEntryById`) require **stricter** RBAC (e.g. `AuditReader` role)? — Plan assumes **no**; `Admin` is enough in Phase 1.
2. Should `DiffJson` be size-limited? — Plan assumes max 64 KB (enforce in validator).
3. Should we store the original `X-Source-Service` literally, or normalize? — Plan: store literal, lowercased.
