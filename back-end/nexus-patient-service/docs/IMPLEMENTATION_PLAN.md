# Nexus HA — Patient Service: Implementation Plan

> **Audience:** the developer building `nexus-patient-service` independently.
> **Authoritative references:** [`../../../specs/patients.openapi.json`](../../../specs/patients.openapi.json), [`../../../docs/LLD_Nexus_HA.md`](../../../docs/LLD_Nexus_HA.md), [`../../../docs/HLD_Nexus_HA.md`](../../../docs/HLD_Nexus_HA.md), [`../../../docs/ADR_Nexus_HA.md`](../../../docs/ADR_Nexus_HA.md).
> **Independence rule:** self-contained, no project references, no shared NuGet, no shared source. Copy-paste of cross-cutting helpers is the intended pattern.

---

## 1. Scope

| OpenAPI tag | Operations | Feature folder |
|---|---|---|
| `Patients` | `listPatients`, `createPatient`, `getPatientById`, `patchPatient` | `Features/Patients` |
| `PatientLifecycle` | `activatePatient`, `archivePatient` | `Features/PatientLifecycle` |
| `PatientBranches` | `listPatientBranches`, `linkPatientToBranch`, `unlinkPatientFromBranch` | `Features/PatientBranches` |

Listening port: **9000**.

**Lifecycle:** `Draft → Active → Archived`. **Archived is terminal — no reactivation.** State transitions are guarded server-side; illegal transitions → `409 LIFECYCLE_INVALID`.

---

## 2. Solution & project layout

```
nexus-patient-service/
├── Nexus.Patient.sln
├── src/Nexus.Patient.Api/Nexus.Patient.Api.csproj       (net10.0, Web SDK)
├── tests/Nexus.Patient.Api.Tests/Nexus.Patient.Api.Tests.csproj
├── docs/                              (this folder)
├── README.md, CHANGELOG.md, TROUBLESHOOTING.md, CONTRIBUTING.md, LICENSE
├── .gitignore, .dockerignore, .editorconfig
└── Dockerfile
```

csproj baseline: `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=false`, `GenerateDocumentationFile=true`. File-scoped namespaces.

---

## 3. Feature-folder structure (under `src/Nexus.Patient.Api/`)

```
Program.cs
Features/
├── Patients/
│   ├── Models/        (Patient, CreatePatientRequest, PatchPatientRequest, PatientListItem, PagedPatients)
│   ├── Validators/    (CreatePatientRequestValidator, PatchPatientRequestValidator)
│   ├── Repository/    (IPatientRepository, PatientRepository)
│   ├── Service/       (IPatientService, PatientService, IDuplicateDetector, PhoneDobDuplicateDetector)
│   └── Controller/    (PatientsController)
├── PatientLifecycle/
│   ├── Models/        (ActivatePatientRequest?, ArchivePatientRequest? — both empty per spec)
│   ├── Validators/    (-)
│   ├── Service/       (IPatientLifecycleService, PatientLifecycleService, PatientStateMachine)
│   └── Controller/    (PatientLifecycleController)
└── PatientBranches/
    ├── Models/        (PatientBranchLink, LinkPatientToBranchRequest, PagedPatientBranches)
    ├── Validators/    (LinkPatientToBranchRequestValidator)
    ├── Repository/    (IPatientBranchRepository, PatientBranchRepository)
    ├── Service/       (IPatientBranchService, PatientBranchService)
    └── Controller/    (PatientBranchesController)

Infrastructure/
├── Auth/              (JwtBearerConfig — RS256 via Identity JWKS, AdminRolePolicy, CurrentUser)
├── Persistence/       (PatientDbContext, RowVersionInterceptor, Migrations/)
├── Resilience/        (ResilienceOptions binding)
├── Clients/           (IBranchesClient, BranchesClient — typed HttpClient → branch service)
├── Audit/             (IAuditPublisher, HttpAuditPublisher, AuditEntryDto)
├── Observability/     (SerilogBootstrap, OtelBootstrap, HealthCheckRegistration)
├── Errors/            (ProblemDetailsMiddleware, DomainException, ErrorCodes)
├── Pagination/        (PageRequest, PagedResult<T>, SortParser)
└── Startup/           (BannerWriter, StartupSummaryWriter)

Configuration/
├── JwtBearerOptions.cs   (Authority, Audience, MetadataAddress for JWKS)
├── RateLimitingOptions.cs
├── CorsOptions.cs
├── ResilienceOptions.cs
├── BranchesClientOptions.cs
├── AuditClientOptions.cs
├── ObservabilityOptions.cs
└── DuplicateDetectionOptions.cs

appsettings.json, appsettings.Development.json, appsettings.Production.json
```

---

## 4. Persistence (EF Core 10, code-first)

- DbContext `PatientDbContext` → database **`NexusPatients`**.
- Entities:
  - `Patient` → `Patients` (Id `uniqueidentifier` PK, MRN `nvarchar(32)` UQ, FullName, DateOfBirth `date`, Gender, Email `nvarchar(254)?`, Phone `nvarchar(20)?`, AddressJson `nvarchar(max)?`, State `nvarchar(16)` — `Draft|Active|Archived`, CreatedAtUtc, UpdatedAtUtc, ArchivedAtUtc null, RowVersion `rowversion`).
  - `PatientBranchLink` → `PatientBranchLinks` (Id PK, PatientId FK, BranchId, IsPrimary `bit`, LinkedAtUtc, UnlinkedAtUtc null). Unique filtered index `(PatientId, BranchId) WHERE UnlinkedAtUtc IS NULL`. Filtered index for `(PatientId) WHERE IsPrimary=1 AND UnlinkedAtUtc IS NULL` (max 1 primary).
- Indexes for duplicate detection: `(Phone, DateOfBirth) WHERE Phone IS NOT NULL`, `(Email, DateOfBirth) WHERE Email IS NOT NULL`.
- ETag = base64(`RowVersion`); `If-Match` → `409 ETAG_MISMATCH` on mismatch.
- Migrations in `Infrastructure/Persistence/Migrations/`. `Persistence:AutoMigrate=true` in Development.

---

## 5. Validation (FluentValidation)

- `CreatePatientRequestValidator` — `fullName` 1–200, `dateOfBirth` past, `gender` enum, `email`/`phone` format, at least one of `email`/`phone` present, `address` shape.
- `PatchPatientRequestValidator` — at least one field present.
- `LinkPatientToBranchRequestValidator` — `branchId` GUID required.
- Auto-invocation via `ValidationFilter<T>` endpoint filter → 422 with `errors[]`.

---

## 6. Error model (RFC 9457 ProblemDetails)

Standard envelope: `type/title/status/code/detail/instance/traceId/errors[]`. Codes used:

| HTTP | code | When |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed JSON |
| 401 | `UNAUTHENTICATED` | Missing/invalid bearer |
| 403 | `UNAUTHORIZED` | Authenticated but not `Admin` |
| 404 | `NOT_FOUND` | Patient or link not found |
| 409 | `PATIENT_DUPLICATE_PHONE_DOB` | Phone+DOB collision in create |
| 409 | `PATIENT_DUPLICATE_EMAIL_DOB` | Email+DOB collision in create |
| 409 | `ETAG_MISMATCH` | `If-Match` mismatch on patch |
| 409 | `LIFECYCLE_INVALID` | Illegal state transition (e.g. activate from Archived) |
| 409 | `PRIMARY_BRANCH_REQUIRED` | Removing the last/primary branch link |
| 409 | `BRANCH_LINK_DUPLICATE` | Re-linking an already-linked branch |
| 422 | `PATIENT_VALIDATION` | FluentValidation failure |
| 429 | `RATE_LIMITED` | Rate limit hit |

`type` base: `https://errors.nexusha.local/<slug>`. `traceId` = `Activity.Id`.

---

## 7. AuthN/Z

JwtBearer:
- `Authority` = `http://nexus-identity-service:8000` (override via `Auth:Authority`).
- `MetadataAddress` = `<authority>/api/v1/auth/jwks` — feed JWKS directly, no OIDC discovery doc.
- `Audience` = `nexus-ha`, `ValidateIssuer=true`, `Issuer` = `https://api.nexusha.local/identity`.
- `JwtBearerOptions.MapInboundClaims=false`.
- JWKS auto-refreshed every 5 minutes (default `ConfigurationManager` cadence).

Policy `AdminOnly` = `RequireAuthenticatedUser().RequireRole("Admin")`. Applied to all controllers.

---

## 8. Resilience (Microsoft.Extensions.Http.Resilience)

Two outbound typed clients: `BranchesClient` and audit publisher's `AuditClient`. Each registered via `AddHttpClient<T>(...).AddStandardResilienceHandler()`, bound to `Resilience:Branches` and `Resilience:Audit` respectively.

```json
"Resilience": {
  "Branches": { "TotalRequestTimeoutSeconds": 10, "AttemptTimeoutSeconds": 3,
    "Retry": { "MaxRetryAttempts": 3, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
    "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 } },
  "Audit":    { /* same shape */ }
}
```

`BranchesClient` exposes `Task<BranchSummary?> GetByIdAsync(Guid id, CancellationToken ct)` and `Task<bool> ExistsAsync(Guid id, CancellationToken ct)`. Used in `linkPatientToBranch` to confirm the branch exists and is active.

---

## 9. Audit publishing

`IAuditPublisher.PublishAsync(AuditEntryDto entry, CancellationToken ct)` → `HttpAuditPublisher` is a typed client posting `/api/v1/audit` to audit service.
- Required header `Idempotency-Key` (UUIDv7) per call.
- `Authorization: Bearer <forwarded-admin-token>` extracted from `IHttpContextAccessor`.
- Publish failures are **logged WARN and swallowed** — never block user action.
- Events emitted: `Patient.Created`, `Patient.Updated`, `Patient.StateChanged` (with `from→to`), `PatientBranch.Linked`, `PatientBranch.Unlinked`.

---

## 10. Observability

- Serilog: console (JSON in Prod, coloured in Dev) + rolling file `/var/log/nexus/patient/patient-.log` (7-day retention). Enrichers: `FromLogContext`, machine, env, `service=nexus-patient`.
- OpenTelemetry .NET: Traces+Metrics+Logs OTLP gRPC exporter, instrumentation `AspNetCore`, `HttpClient`, `EntityFrameworkCore`, `Runtime`, `Process`. Resource `service.name=nexus-patient`.
- Prometheus: `/metrics` via `OpenTelemetry.Exporter.Prometheus.AspNetCore`. Custom metrics: `nexus_patient_state_transitions_total{from,to}`, `nexus_patient_duplicate_rejections_total{rule}`.
- Health checks (`Microsoft.Extensions.Diagnostics.HealthChecks`):
  - `/health/live` — process up.
  - `/health/ready` — SQL Server + Branches service `HEAD /api/v1/branches?size=1` (degraded-not-failed if branches down).
  - `/health` — combined JSON.

---

## 11. Startup banner & summary

`Spectre.Console` Figlet "Nexus Patient" + panels: Configuration (port 9000, env, base URL, JWT authority/audience), Dependencies (SQL Server probe, Branches reachability, Audit reachability, OTLP endpoint), Toggles (rate limiting, CORS, Swagger/ReDoc URLs).

---

## 12. Rate limiting

`Microsoft.AspNetCore.RateLimiting` fixed-window IP partition, `PermitLimit=100`, `WindowSeconds=60`, `RateLimiting:Enabled=false` by default. 429 → ProblemDetails `RATE_LIMITED` + `Retry-After`. Excludes `/health/**`, `/metrics`.

---

## 13. CORS

Default `{ AllowedOrigins:["*"], AllowedHeaders:["*"], AllowedMethods:["*"], AllowCredentials:false }`. Startup validator: `AllowCredentials=true` ⇒ origins must not contain `*`.

---

## 14. Configuration files

`appsettings.json`:

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://+:9000" } } },
  "ConnectionStrings": { "Default": "<provided-later>" },
  "Auth": { "Authority": "http://nexus-identity-service:8000", "Audience": "nexus-ha",
            "Issuer": "https://api.nexusha.local/identity" },
  "Persistence": { "AutoMigrate": false, "FailFast": false },
  "RateLimiting": { "Enabled": false, "PermitLimit": 100, "WindowSeconds": 60 },
  "Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false },
  "BranchesClient": { "BaseUrl": "http://nexus-branch-service:11000" },
  "AuditClient":    { "BaseUrl": "http://nexus-audit-service:12000" },
  "Resilience": { "Branches": { "TotalRequestTimeoutSeconds": 10, "AttemptTimeoutSeconds": 3,
      "Retry": { "MaxRetryAttempts": 3, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
      "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 } },
    "Audit": { "TotalRequestTimeoutSeconds": 10, "AttemptTimeoutSeconds": 3,
      "Retry": { "MaxRetryAttempts": 3, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
      "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 } } },
  "Observability": { "Otlp": { "Endpoint": "", "Protocol": "Grpc" }, "Prometheus": { "Enabled": true } },
  "Logging": { "File": { "Path": "logs/patient-.log", "RetainedFileCountLimit": 7 } },
  "DuplicateDetection": { "PhoneDobEnabled": true, "EmailDobEnabled": true }
}
```

`appsettings.Development.json`: `Persistence:AutoMigrate=true`, plain console logging.
`appsettings.Production.json`: `Observability:Otlp:Endpoint=http://otel-collector:4317`, `Persistence:FailFast=true`.

All options bound with `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

## 15. OpenAPI surface

Swashbuckle at `/swagger/v1/swagger.json`, Swagger UI `/swagger`, ReDoc UI `/redoc`. XML doc comments feed descriptions. `OperationFilter` attaches `Idempotency-Key` + `If-Match` headers where the source spec defines them.

CI guidance: `spectral lint specs/patients.openapi.json` and diff against `/swagger/v1/swagger.json` to detect drift.

---

## 16. Static documentation (Redocly CLI)

`docs/redocly.yaml`:

```yaml
extends: [recommended]
apis:
  patients@v1:
    root: ../../../specs/patients.openapi.json
```

`docs/package.json` script: `redocly build-docs ../../../specs/patients.openapi.json -o site/index.html`. `docs/site/` gitignored.

---

## 17. Dockerfile (multi-stage, Alpine, no HEALTHCHECK)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Nexus.Patient.sln", "./"]
COPY ["src/Nexus.Patient.Api/Nexus.Patient.Api.csproj", "src/Nexus.Patient.Api/"]
COPY ["tests/Nexus.Patient.Api.Tests/Nexus.Patient.Api.Tests.csproj", "tests/Nexus.Patient.Api.Tests/"]
RUN dotnet restore "Nexus.Patient.sln"
COPY . .
RUN dotnet publish "src/Nexus.Patient.Api/Nexus.Patient.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:9000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 9000
ENTRYPOINT ["dotnet", "Nexus.Patient.Api.dll"]
```

`.dockerignore`: `**/bin`, `**/obj`, `**/.vs`, `docs/site`, `logs`, `*.user`, `.git`, `tests`.

---

## 18. Test project (xUnit + FluentAssertions + NSubstitute + Bogus + coverlet)

```
tests/Nexus.Patient.Api.Tests/
├── Features/
│   ├── Patients/
│   │   ├── Validators/   (CreatePatientRequestValidatorTests, PatchPatientRequestValidatorTests)
│   │   ├── Repository/   (PatientRepositoryTests — EF InMemory)
│   │   ├── Service/      (PatientServiceTests, PhoneDobDuplicateDetectorTests)
│   │   └── Controller/   (PatientsControllerTests)
│   ├── PatientLifecycle/
│   │   ├── Service/      (PatientLifecycleServiceTests, PatientStateMachineTests — covers all illegal transitions)
│   │   └── Controller/   (PatientLifecycleControllerTests)
│   └── PatientBranches/
│       ├── Validators/   (LinkPatientToBranchRequestValidatorTests)
│       ├── Repository/   (PatientBranchRepositoryTests)
│       ├── Service/      (PatientBranchServiceTests — covers PRIMARY_BRANCH_REQUIRED)
│       └── Controller/   (PatientBranchesControllerTests)
├── Infrastructure/
│   ├── Errors/           (ProblemDetailsMiddlewareTests)
│   ├── Clients/          (BranchesClientTests — NSubstitute HttpMessageHandler)
│   └── Audit/            (HttpAuditPublisherTests)
├── Fakers/               (PatientFaker, PatientBranchLinkFaker)
└── Nexus.Patient.Api.Tests.csproj
```

Packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `NSubstitute`, `Bogus`, `coverlet.collector`, `Microsoft.EntityFrameworkCore.InMemory`.

Coverage target ≥80% line — reported, not gated in Phase 1. Integration tests deferred.

---

## 19. Documentation comments

XML doc comments on every public type and member of validators, repositories, services, controllers, options classes, middleware. `GenerateDocumentationFile=true`. `.editorconfig` sets `dotnet_diagnostic.CS1591.severity = suggestion`.

---

## 20. Top-level service artifacts

`README.md` (overview, stack, prereqs, quick start, config, API doc links, endpoint summary), `CHANGELOG.md` (Keep-a-Changelog, `## [0.1.0] - Unreleased`), `TROUBLESHOOTING.md` (DB unreachable, JWKS refresh failure, branches service down, duplicate-rule false positives, OTLP errors), `CONTRIBUTING.md` (branching, Conventional Commits, dev loop, add a feature folder, add a migration, update OpenAPI), `LICENSE` (MIT, `Copyright (c) 2026 Nexus HA Engineering`), `.gitignore`, `.editorconfig`.

---

## 21. Patient-specific business rules (developer must enforce)

- **Duplicate detection**: on `createPatient`, if `Phone+DOB` matches an existing non-archived patient → `409 PATIENT_DUPLICATE_PHONE_DOB`; same for `Email+DOB`. Each rule has its own toggle in `DuplicateDetection`.
- **Lifecycle state machine**:
  - `Draft` → `Active` only via `activatePatient`. Requires ≥1 active branch link.
  - `Active` → `Archived` only via `archivePatient`.
  - `Archived` → anything: **forbidden** → `409 LIFECYCLE_INVALID`.
- **Primary branch invariant**: every `Active` patient must have exactly one `IsPrimary=true` active link. `unlinkPatientFromBranch` that would remove the only primary link → `409 PRIMARY_BRANCH_REQUIRED`. `linkPatientToBranch` with `isPrimary=true` flips any existing primary link to non-primary atomically.
- **Branch validation on link**: call `BranchesClient.GetByIdAsync(branchId)`; if 404 or branch inactive → `404 NOT_FOUND` (per spec) or `409 BRANCH_LINK_INACTIVE`.

---

## 22. Verification

```powershell
dotnet build Nexus.Patient.sln
dotnet test  Nexus.Patient.sln --collect "XPlat Code Coverage"
docker build -t nexus-patient-service:dev .
docker run --rm -p 9000:9000 -e ConnectionStrings__Default="<your-sql>" nexus-patient-service:dev
curl -s http://localhost:9000/health/ready
```

Plus in `docs/`:

```powershell
npm install -g @redocly/cli
npx @redocly/cli lint ../../specs/patients.openapi.json
npm run docs:build
```

Acceptance: xUnit green; `/swagger/v1/swagger.json` exposes all 9 operationIds from [`../../../specs/patients.openapi.json`](../../../specs/patients.openapi.json); `docs/site/index.html` renders; container boots with banner; manual contract test pass per [`CONTRACT_TESTING.md`](CONTRACT_TESTING.md).

---

## Open items for the dev to confirm

1. Should soft-deleted (`UnlinkedAtUtc IS NOT NULL`) links be returned by `listPatientBranches`? — Plan assumes **no** (only active links).
2. Should `archivePatient` cascade-unlink branches? — Plan assumes **no**; links remain for audit history; `IsPrimary` is preserved.
3. When `BranchesClient` is circuit-open, should `linkPatientToBranch` reject with 503 or queue? — Plan assumes **503 fail-fast**, no queueing in Phase 1.
