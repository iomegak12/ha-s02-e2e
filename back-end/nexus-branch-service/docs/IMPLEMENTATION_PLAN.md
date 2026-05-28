# Nexus HA — Branch Service: Implementation Plan

> **Audience:** the developer building `nexus-branch-service` independently.
> **Authoritative references:** [`../../../specs/branches.openapi.json`](../../../specs/branches.openapi.json), [`../../../docs/LLD_Nexus_HA.md`](../../../docs/LLD_Nexus_HA.md), [`../../../docs/HLD_Nexus_HA.md`](../../../docs/HLD_Nexus_HA.md), [`../../../docs/ADR_Nexus_HA.md`](../../../docs/ADR_Nexus_HA.md).
> **Independence rule:** self-contained, no project references, no shared NuGet, no shared source.

---

## 1. Scope

| OpenAPI tag | Operations | Feature folder |
|---|---|---|
| `Branches` | `listBranches`, `createBranch`, `getBranchById`, `patchBranch`, `deactivateBranch` | `Features/Branches` |

Listening port: **11000**.

This service is the **source of truth** for branch data. Soft delete only (`deactivateBranch` flips `IsActive=false`; no hard delete in Phase 1). No reactivation operation in Phase 1 (open item #1).

---

## 2. Solution & project layout

```
nexus-branch-service/
├── Nexus.Branch.sln
├── src/Nexus.Branch.Api/Nexus.Branch.Api.csproj      (net10.0, Web SDK)
├── tests/Nexus.Branch.Api.Tests/Nexus.Branch.Api.Tests.csproj
├── docs/, README.md, CHANGELOG.md, TROUBLESHOOTING.md, CONTRIBUTING.md, LICENSE
├── .gitignore, .dockerignore, .editorconfig
└── Dockerfile
```

csproj baseline same as siblings (net10.0, Nullable on, ImplicitUsings on, `GenerateDocumentationFile=true`).

---

## 3. Feature-folder structure

```
Program.cs
Features/
└── Branches/
    ├── Models/        (Branch, CreateBranchRequest, PatchBranchRequest, BranchListItem, PagedBranches)
    ├── Validators/    (CreateBranchRequestValidator, PatchBranchRequestValidator)
    ├── Repository/    (IBranchRepository, BranchRepository)
    ├── Service/       (IBranchService, BranchService)
    └── Controller/    (BranchesController)

Infrastructure/
├── Auth/              (JwtBearerConfig via Identity JWKS, AdminRolePolicy, CurrentUser)
├── Persistence/       (BranchDbContext, RowVersionInterceptor, Migrations/)
├── Resilience/        (ResilienceOptions binding — only Audit)
├── Audit/             (IAuditPublisher, HttpAuditPublisher, AuditEntryDto)
├── Observability/     (SerilogBootstrap, OtelBootstrap, HealthCheckRegistration)
├── Errors/            (ProblemDetailsMiddleware, DomainException, ErrorCodes)
├── Pagination/
└── Startup/

Configuration/
├── JwtBearerOptions.cs
├── RateLimitingOptions.cs
├── CorsOptions.cs
├── ResilienceOptions.cs
├── AuditClientOptions.cs
└── ObservabilityOptions.cs

appsettings.json, appsettings.Development.json, appsettings.Production.json
```

---

## 4. Persistence (EF Core 10)

DbContext `BranchDbContext` → database **`NexusBranches`**.

- `Branch` → `Branches` (Id PK, Code `nvarchar(16)` UQ, Name `nvarchar(200)`, AddressLine1, AddressLine2, City, State, PostalCode, Country, Phone, Email, IsActive `bit` default 1, CreatedAtUtc, UpdatedAtUtc, DeactivatedAtUtc null, RowVersion). Filtered unique index on `Code WHERE IsActive=1` to allow reuse of code after soft delete (open item #2).

ETag = base64(RowVersion). `If-Match` → `409 ETAG_MISMATCH` on mismatch.

---

## 5. Validation (FluentValidation)

- `CreateBranchRequestValidator` — `code` `[A-Z][A-Z0-9-]{1,15}`, `name` 1–200, address fields, `country` ISO-3166-α2, `phone`/`email` format.
- `PatchBranchRequestValidator` — at least one field. `code` immutable once created (enforce in service).
- Auto-invocation via `ValidationFilter<T>` endpoint filter → `422 BRANCH_VALIDATION`.

---

## 6. Error model (RFC 9457 ProblemDetails)

| HTTP | code | When |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed JSON |
| 401 | `UNAUTHENTICATED` | Missing/invalid bearer |
| 403 | `UNAUTHORIZED` | Authenticated but not `Admin` |
| 404 | `NOT_FOUND` | Branch id unknown |
| 409 | `BRANCH_CODE_DUPLICATE` | Code collision on create |
| 409 | `ETAG_MISMATCH` | `If-Match` mismatch |
| 409 | `BRANCH_ALREADY_INACTIVE` | Deactivating an already-inactive branch (idempotent → still 204; this code only on patch-then-deactivate race) |
| 422 | `BRANCH_VALIDATION` | FluentValidation failure |
| 429 | `RATE_LIMITED` | Rate limit hit |

---

## 7. AuthN/Z

JwtBearer: Authority `http://nexus-identity-service:8000`, MetadataAddress `<authority>/api/v1/auth/jwks`, Audience `nexus-ha`, Issuer `https://api.nexusha.local/identity`. `MapInboundClaims=false`. Policy `AdminOnly` on all controllers.

---

## 8. Resilience

Single outbound dependency: Audit. Typed `AuditClient` registered with `AddStandardResilienceHandler()` bound to `Resilience:Audit` (same shape as siblings).

---

## 9. Audit publishing

`IAuditPublisher.PublishAsync(...)` → `HttpAuditPublisher` typed client posting `/api/v1/audit` with `Idempotency-Key` UUIDv7 + forwarded admin bearer. Failures log WARN and continue.

Events: `Branch.Created`, `Branch.Updated`, `Branch.Deactivated`.

---

## 10. Observability

Serilog (console JSON in Prod, plain in Dev) + rolling file `/var/log/nexus/branch/branch-.log` 7-day retention. OpenTelemetry OTLP gRPC. Prometheus `/metrics`. Custom metric: `nexus_branch_state_transitions_total{action}`.

Health: `/health/live`, `/health/ready` (SQL Server + Audit HEAD), `/health` combined.

---

## 11. Startup banner & summary

`Spectre.Console` Figlet "Nexus Branch". Panels: Configuration (port 11000, env, JWT authority/audience), Dependencies (SQL Server, Audit, OTLP), Toggles (rate limiting, CORS, Swagger/ReDoc/metrics).

---

## 12. Rate limiting

Fixed-window IP, `PermitLimit=100`, `WindowSeconds=60`, off by default.

---

## 13. CORS

Default `{ *, *, *, false }` with startup validator.

---

## 14. Configuration files

`appsettings.json`:

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://+:11000" } } },
  "ConnectionStrings": { "Default": "<provided-later>" },
  "Auth": { "Authority": "http://nexus-identity-service:8000", "Audience": "nexus-ha",
            "Issuer": "https://api.nexusha.local/identity" },
  "Persistence": { "AutoMigrate": false, "FailFast": false },
  "RateLimiting": { "Enabled": false, "PermitLimit": 100, "WindowSeconds": 60 },
  "Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false },
  "AuditClient": { "BaseUrl": "http://nexus-audit-service:12000" },
  "Resilience": { "Audit": { "TotalRequestTimeoutSeconds": 10, "AttemptTimeoutSeconds": 3,
      "Retry": { "MaxRetryAttempts": 3, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
      "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 } } },
  "Observability": { "Otlp": { "Endpoint": "", "Protocol": "Grpc" }, "Prometheus": { "Enabled": true } },
  "Logging": { "File": { "Path": "logs/branch-.log", "RetainedFileCountLimit": 7 } }
}
```

`appsettings.Development.json`: `Persistence:AutoMigrate=true`.
`appsettings.Production.json`: `Observability:Otlp:Endpoint=http://otel-collector:4317`, `Persistence:FailFast=true`.

All options bound with `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

## 15. OpenAPI surface

Swashbuckle `/swagger/v1/swagger.json`, Swagger UI `/swagger`, ReDoc `/redoc`. XML doc comments feed descriptions. CI: `spectral lint specs/branches.openapi.json`.

---

## 16. Static documentation (Redocly CLI)

`docs/redocly.yaml`:

```yaml
extends: [recommended]
apis:
  branches@v1:
    root: ../../../specs/branches.openapi.json
```

`docs/site/` gitignored.

---

## 17. Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Nexus.Branch.sln", "./"]
COPY ["src/Nexus.Branch.Api/Nexus.Branch.Api.csproj", "src/Nexus.Branch.Api/"]
COPY ["tests/Nexus.Branch.Api.Tests/Nexus.Branch.Api.Tests.csproj", "tests/Nexus.Branch.Api.Tests/"]
RUN dotnet restore "Nexus.Branch.sln"
COPY . .
RUN dotnet publish "src/Nexus.Branch.Api/Nexus.Branch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:11000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 11000
ENTRYPOINT ["dotnet", "Nexus.Branch.Api.dll"]
```

`.dockerignore`: `**/bin`, `**/obj`, `**/.vs`, `docs/site`, `logs`, `*.user`, `.git`, `tests`.

---

## 18. Test project

```
tests/Nexus.Branch.Api.Tests/
├── Features/Branches/
│   ├── Validators/   (CreateBranchRequestValidatorTests, PatchBranchRequestValidatorTests)
│   ├── Repository/   (BranchRepositoryTests — EF InMemory)
│   ├── Service/      (BranchServiceTests — duplicate code, soft delete idempotency, code immutability)
│   └── Controller/   (BranchesControllerTests)
├── Infrastructure/
│   ├── Errors/       (ProblemDetailsMiddlewareTests)
│   └── Audit/        (HttpAuditPublisherTests)
├── Fakers/           (BranchFaker)
└── Nexus.Branch.Api.Tests.csproj
```

Packages: `xunit`, `FluentAssertions`, `NSubstitute`, `Bogus`, `coverlet.collector`, `Microsoft.EntityFrameworkCore.InMemory`. Coverage target ≥80% line, reported.

---

## 19. Documentation comments

XML doc comments on all public types/members of validators, repository, service, controller, options, middleware. `GenerateDocumentationFile=true`.

---

## 20. Top-level service artifacts

`README.md` (overview, stack, quick start, config, API doc links, endpoints), `CHANGELOG.md` (`## [0.1.0] - Unreleased`), `TROUBLESHOOTING.md` (DB unreachable, JWKS refresh failure, soft delete vs hard delete, audit fanout), `CONTRIBUTING.md`, `LICENSE` (MIT 2026 Nexus HA Engineering), `.gitignore`, `.editorconfig`.

---

## 21. Branch-specific business rules

- **Code is immutable** after create. `patchBranch` carrying `code` → ignored or `422 BRANCH_VALIDATION` (developer choice; plan recommends 422 to surface bug early).
- **Soft delete only.** `deactivateBranch` sets `IsActive=false, DeactivatedAtUtc=now`. Idempotent — re-deactivating an inactive branch returns `204` (not 409).
- **`listBranches` default filter**: `isActive=true` by default. Pass `isActive=false` to include inactive.
- **Downstream services** depend on this service via typed clients. Maintain backward compatibility on `Branch` shape — additive changes only.

---

## 22. Verification

```powershell
dotnet build Nexus.Branch.sln
dotnet test  Nexus.Branch.sln --collect "XPlat Code Coverage"
docker build -t nexus-branch-service:dev .
docker run --rm -p 11000:11000 -e ConnectionStrings__Default="<sql>" nexus-branch-service:dev
curl -s http://localhost:11000/health/ready
```

Plus in `docs/`:

```powershell
npx @redocly/cli lint ../../specs/branches.openapi.json
npm run docs:build
```

Acceptance: xUnit green; `/swagger/v1/swagger.json` exposes all 5 operationIds from [`../../../specs/branches.openapi.json`](../../../specs/branches.openapi.json); banner panel renders; manual contract tests pass per [`CONTRACT_TESTING.md`](CONTRACT_TESTING.md).

---

## Open items for the dev to confirm

1. Should there be a `reactivateBranch` endpoint? — Plan assumes **no** for Phase 1.
2. Should branch `code` be reusable after soft delete? — Plan assumes **yes** via filtered unique index (`WHERE IsActive=1`).
3. Should `deactivateBranch` fail if doctors/patients are still linked? — Plan assumes **no** (Branch service doesn't know link counts; patient/doctor services own their links and can treat inactive branches as still-linked-but-frozen).
