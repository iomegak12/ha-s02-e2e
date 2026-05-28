# Nexus HA — Identity & Admin Service: Implementation Plan

> **Audience:** the developer building `nexus-identity-service` independently.
> **Authoritative references:** [`../../../specs/identity.openapi.json`](../../../specs/identity.openapi.json), [`../../../docs/LLD_Nexus_HA.md`](../../../docs/LLD_Nexus_HA.md) (§7 error model, §9 schema, §10 AuthN, §14 Audit), [`../../../docs/HLD_Nexus_HA.md`](../../../docs/HLD_Nexus_HA.md), [`../../../docs/ADR_Nexus_HA.md`](../../../docs/ADR_Nexus_HA.md) (ADR-002, ADR-006, ADR-007).
> **Independence rule:** this service is self-contained. No project references, no shared NuGet, no shared source folders with sibling services. Copy-paste of common helpers (ProblemDetails, pagination DTO, JWT validation wiring) into this codebase is the intended pattern.

---

## 1. Scope

This service is the **self-hosted identity provider** for hospital administrators. Patients and doctors never authenticate.

| OpenAPI tag | Operations | Feature folder |
|---|---|---|
| `Auth` | `issueToken`, `refreshToken`, `revokeToken`, `getJwks` | `Features/Auth` |
| `Admins` | `listAdmins`, `createAdmin`, `getAdminById`, `patchAdmin`, `deactivateAdmin` | `Features/Admins` |

Listening port: **8000** (HTTP) — set in `Properties/launchSettings.json`, `appsettings.json` (`Kestrel:Endpoints`), `Dockerfile` (`EXPOSE 8000`), `ASPNETCORE_URLS=http://+:8000`.

---

## 2. Solution & project layout

```
nexus-identity-service/
├── Nexus.Identity.sln
├── src/
│   └── Nexus.Identity.Api/
│       └── Nexus.Identity.Api.csproj   (net10.0, Microsoft.NET.Sdk.Web)
├── tests/
│   └── Nexus.Identity.Api.Tests/
│       └── Nexus.Identity.Api.Tests.csproj   (net10.0, xUnit)
├── docs/
│   ├── IMPLEMENTATION_PLAN.md   (this file)
│   ├── CONTRACT_TESTING.md
│   ├── redocly.yaml
│   └── site/                    (generated, gitignored)
├── README.md
├── CHANGELOG.md
├── TROUBLESHOOTING.md
├── CONTRIBUTING.md
├── LICENSE
├── .gitignore
├── .dockerignore
├── .editorconfig
└── Dockerfile
```

**csproj baseline (api):**

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <LangVersion>latest</LangVersion>
  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);1591</NoWarn>
  <RootNamespace>Nexus.Identity.Api</RootNamespace>
  <AssemblyName>Nexus.Identity.Api</AssemblyName>
</PropertyGroup>
```

File-scoped namespaces throughout. `.editorconfig` enforces 4-space indent, CRLF, `var` preferences, sorted usings.

---

## 3. Feature-folder structure (under `src/Nexus.Identity.Api/`)

```
Program.cs
Features/
├── Auth/
│   ├── Models/        (TokenRequest, TokenResponse, RefreshRequest, RevokeRequest, JwksResponse, RefreshTokenEntity)
│   ├── Validators/    (TokenRequestValidator, RefreshRequestValidator, RevokeRequestValidator)
│   ├── Repository/    (IRefreshTokenRepository, RefreshTokenRepository)
│   ├── Service/       (IAuthService, AuthService, IJwtIssuer, JwtIssuer, IJwksProvider, JwksProvider, ISigningKeyStore, FileSigningKeyStore)
│   └── Controller/    (AuthController)
└── Admins/
    ├── Models/        (Admin, CreateAdminRequest, PatchAdminRequest, AdminListItem, PagedAdmins)
    ├── Validators/    (CreateAdminRequestValidator, PatchAdminRequestValidator)
    ├── Repository/    (IAdminRepository, AdminRepository)
    ├── Service/       (IAdminService, AdminService, IPasswordHasher, BCryptPasswordHasher)
    └── Controller/    (AdminsController)

Infrastructure/
├── Auth/              (AdminRolePolicy, CurrentUser, ClaimsExtensions)
├── Persistence/       (IdentityDbContext, RowVersionInterceptor, Migrations/)
├── Resilience/        (ResilienceOptions binding) — only used by AuditPublisher in this service
├── Audit/             (IAuditPublisher, HttpAuditPublisher, AuditEntryDto)
├── Observability/     (SerilogBootstrap, OtelBootstrap, HealthCheckRegistration)
├── Errors/            (ProblemDetailsMiddleware, DomainException, ErrorCodes)
├── Pagination/        (PageRequest, PagedResult<T>, SortParser)
└── Startup/           (BannerWriter, StartupSummaryWriter)

Configuration/
├── JwtIssuerOptions.cs        (Issuer, Audience, AccessTokenLifetime, RefreshTokenLifetime, SigningKeysDirectory, ActiveKid)
├── RateLimitingOptions.cs
├── CorsOptions.cs
├── ResilienceOptions.cs
├── AuditClientOptions.cs
├── ObservabilityOptions.cs
└── SeederOptions.cs           (DevSeedAdminEnabled, DevSeedAdminUsername)

appsettings.json
appsettings.Development.json
appsettings.Production.json
```

---

## 4. Persistence (EF Core 10, code-first)

- DbContext: `IdentityDbContext` against database **`NexusIdentity`** on the shared SQL Server host.
- Entities & tables (PascalCase types, snake-free SQL):
  - `Admin` → `Admins` (Id `uniqueidentifier` PK, Username `nvarchar(64)` UQ, DisplayName, PasswordHash, IsActive `bit`, CreatedAtUtc, UpdatedAtUtc, RowVersion `rowversion`).
  - `RefreshToken` → `RefreshTokens` (Id PK, AdminId FK→Admins.Id, TokenHash `varbinary(32)` UQ, IssuedAtUtc, ExpiresAtUtc, RevokedAtUtc null, ReplacedByTokenId null).
  - `SigningKey` → `SigningKeys` (Kid PK `nvarchar(64)`, AlgorithmJson, PublicJwkJson, PrivateKeyPemEncrypted `varbinary(max)`, CreatedAtUtc, ActivatedAtUtc, RetiredAtUtc null).
- Repository pattern: every feature exposes an `I<Name>Repository` interface; concrete implementation depends on `IdentityDbContext`.
- ETag = base64(`RowVersion`). On PATCH, `If-Match` header → `byte[]` compared to entity's RowVersion; mismatch → `409 ETAG_MISMATCH`.
- Migrations under `Infrastructure/Persistence/Migrations/`. Apply on startup in Development only (`db.Database.Migrate()` behind `Persistence:AutoMigrate=true`).

---

## 5. Validation (FluentValidation)

- One validator per request DTO, co-located in the feature's `Validators/` folder.
- `builder.Services.AddValidatorsFromAssemblyContaining<Program>();`
- Auto-invocation via endpoint filter `ValidationFilter<T>` that resolves `IValidator<T>` from DI and short-circuits with `422 *_VALIDATION` ProblemDetails containing `errors[]`.
- Identity validators:
  - `CreateAdminRequestValidator` — username `[a-z][a-z0-9._-]{2,63}`, password ≥12 chars, displayName 1–128 chars.
  - `PatchAdminRequestValidator` — at least one of `displayName`, `isActive`, `password`.
  - `TokenRequestValidator` — `username` + `password` required.
  - `RefreshRequestValidator`, `RevokeRequestValidator` — `refreshToken` required.

---

## 6. Error model (RFC 9457 ProblemDetails)

Middleware `ProblemDetailsMiddleware` catches `DomainException` and unhandled exceptions, emits `application/problem+json`. Schema:

```json
{ "type": "...", "title": "...", "status": 400, "code": "...", "detail": "...",
  "instance": "...", "traceId": "...", "errors": [ { "field": "...", "message": "..." } ] }
```

Error codes used by this service (mirror the spec examples):

| HTTP | `code` | When |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed JSON |
| 401 | `UNAUTHENTICATED` | Missing/invalid bearer on `/admins` |
| 401 | `INVALID_CREDENTIALS` | `/auth/token` bad credentials |
| 401 | `REFRESH_INVALID` | `/auth/refresh` unknown/expired/revoked |
| 403 | `UNAUTHORIZED` | Authenticated but not `Admin` role |
| 404 | `NOT_FOUND` | Admin id not found |
| 409 | `ADMIN_USERNAME_DUPLICATE` | `createAdmin` collision |
| 409 | `ETAG_MISMATCH` | `If-Match` mismatch on patch |
| 422 | `ADMIN_VALIDATION` | FluentValidation failure |
| 429 | `RATE_LIMITED` | Rate limit hit |

`type` URI base: `https://errors.nexusha.local/<slug>`. `traceId` = current `Activity.Id`.

---

## 7. AuthN/Z — **this service ISSUES tokens** (substitutes the standard JwtBearer section)

- **JWT issuance** (`IJwtIssuer`): RS256, header `kid` = active key id, claims: `sub` (admin id), `preferred_username`, `name`, `role=Admin`, `iss`, `aud`, `iat`, `exp`, `jti`. Access-token lifetime configurable (default 15 min).
- **Signing keys** (`ISigningKeyStore` → `FileSigningKeyStore` or `SqlSigningKeyStore`): persist 2048-bit RSA keys; rotation strategy:
  - Active key + at-most-one previous key remain published in JWKS.
  - Rotation cadence configurable (`JwtIssuer:RotationDays`, default 30). Rotation creates a new key, marks it active; the previous key stays in JWKS for one access-token lifetime + grace, then retires.
  - Manual rotation: `POST /api/v1/internal/keys/rotate` (gated by `Internal` policy, off by default; document only).
- **JWKS endpoint** `GET /api/v1/auth/jwks` — anonymous, returns `{ keys: [...] }` from non-retired keys. Cache-Control: `public, max-age=300`.
- **Refresh tokens**: opaque 256-bit random base64url, **hashed (SHA-256)** at rest in `RefreshTokens.TokenHash`. Sliding window: each refresh issues a new refresh token, marks the old as `RevokedAtUtc=now, ReplacedByTokenId=<new>`. Default refresh lifetime 14 days.
- **Revoke endpoint** is idempotent — unknown/already-revoked → still `204`.
- **Local validation** on `/admins/**` endpoints — same JwtBearer config as sibling services (Authority loopback `http://localhost:8000`, MetadataAddress `…/api/v1/auth/jwks`, ValidateIssuer/Audience). Role policy `AdminOnly` = `RequireRole("Admin")`.
- **Password hashing**: `BCrypt.Net-Next`, cost 12.

---

## 8. Resilience (Microsoft.Extensions.Http.Resilience)

Only outbound HTTP dependency: **Audit service** (`AuditClient`). Configure a named typed client with `AddStandardResilienceHandler()`. Values bound from `Resilience:Audit`:

```json
"Resilience": {
  "Audit": {
    "TotalRequestTimeoutSeconds": 10,
    "AttemptTimeoutSeconds": 3,
    "Retry": { "MaxRetryAttempts": 3, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
    "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 }
  }
}
```

---

## 9. Audit publishing

This service still emits audit entries for admin lifecycle events (Admin created, deactivated, password reset, refresh-token revoked, etc.).

- `IAuditPublisher.PublishAsync(AuditEntryDto entry, CancellationToken ct)`
- `HttpAuditPublisher` is a typed `HttpClient` (above) that POSTs `/api/v1/audit` with **required** `Idempotency-Key` (UUIDv7 generated per call) and `Authorization: Bearer <caller-admin-token>` (extracted from current `HttpContext`).
- Publish is awaited but **failures are logged and swallowed** — never blocks the user action. Circuit-breaker open → log warning, continue. (Plan note for reviewers: this is acceptable for Phase 1; revisit with outbox pattern later.)
- `AuditEntryDto`: `entityType`, `entityId`, `entityCode?`, `action` (`Created|Updated|StatusChanged|Purged`), `actorId`, `actorUsername`, `occurredAtUtc`, `summary`, `diff` (optional JSON).

---

## 10. Observability

- **Serilog**: console sink (compact JSON in Production, plain colored in Development) + rolling file sink at `Logging:File:Path` (default `/var/log/nexus/identity/identity-.log`, daily rolling, 7-day retention). Enrichers: `FromLogContext`, `WithMachineName`, `WithEnvironmentName`, `WithProperty("service","nexus-identity")`.
- **OpenTelemetry .NET**: Traces + Metrics + Logs. Instrumentation: `AspNetCore`, `HttpClient`, `EntityFrameworkCore` (via `OpenTelemetry.Instrumentation.EntityFrameworkCore`), `Runtime`, `Process`. Resource attributes: `service.name=nexus-identity`, `service.version=<assembly informational version>`.
- **OTLP exporter**: gRPC, endpoint from `Observability:Otlp:Endpoint`. Unset in Development; default `http://otel-collector:4317` in Production.
- **Prometheus**: `OpenTelemetry.Exporter.Prometheus.AspNetCore` exposes `GET /metrics`. Counter/Histogram metrics include built-in HTTP server, process/runtime, and a custom `nexus_identity_tokens_issued_total{result="success|failure"}`.
- **Health checks** (`Microsoft.Extensions.Diagnostics.HealthChecks`):
  - `GET /health/live` — process liveness, always 200 if app responsive.
  - `GET /health/ready` — SQL Server (`AspNetCore.HealthChecks.SqlServer`) + Audit service `HEAD /api/v1/audit?size=1` (degraded-not-failed).
  - `GET /health` — combined (`ResponseWriter` returns JSON with both).
  - `GET /metrics` — Prometheus scrape (does **not** require auth).

---

## 11. Startup banner & summary

Use `Spectre.Console`. On `Program.cs` after `app.Build()` and before `app.Run()`:

1. ASCII banner (Figlet "Nexus Identity") in cyan.
2. Panel "Configuration" with: service name, version, environment, host, port, base URL, JWT issuer, JWT audience, active kid, signing-key rotation cadence.
3. Panel "Dependencies": SQL Server connection state (probe with 2s timeout; show ✓ green / ✗ red), Audit service URL + reachability, OTLP endpoint.
4. Panel "Toggles": rate limiting on/off + permit/window, CORS origins, Swagger UI URL, ReDoc URL, `/metrics` URL, health endpoints.

If DB probe fails in Production with `Persistence:FailFast=true`, exit with non-zero code.

---

## 12. Rate limiting

`Microsoft.AspNetCore.RateLimiting` with **fixed-window IP partition**:

```csharp
options.AddPolicy("ip-fixed", httpContext =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions {
            PermitLimit = opts.PermitLimit,           // 100
            Window = TimeSpan.FromSeconds(opts.WindowSeconds), // 60
            QueueLimit = 0
        }));
```

`RateLimiting:Enabled=false` by default → policy registered but bypass middleware skips it. When enabled, applied globally except `/health/**`, `/metrics`, `/api/v1/auth/jwks`. 429 response uses ProblemDetails with `RATE_LIMITED` and `Retry-After` header.

---

## 13. CORS

```json
"Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false }
```

Default policy named `default`. Applied globally. Note: when `AllowCredentials=true`, `AllowedOrigins` cannot contain `*` — validator enforces this at startup.

---

## 14. Configuration files

**`appsettings.json` (base, committed)** — full structure with placeholders:

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://+:8000" } } },
  "ConnectionStrings": { "Default": "<provided-later>" },
  "JwtIssuer": {
    "Issuer": "https://api.nexusha.local/identity",
    "Audience": "nexus-ha",
    "AccessTokenLifetimeMinutes": 15,
    "RefreshTokenLifetimeDays": 14,
    "RotationDays": 30,
    "SigningKeyStore": "Sql"
  },
  "Persistence": { "AutoMigrate": false, "FailFast": false },
  "RateLimiting": { "Enabled": false, "PermitLimit": 100, "WindowSeconds": 60 },
  "Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false },
  "Resilience": { "Audit": { "TotalRequestTimeoutSeconds": 10, "AttemptTimeoutSeconds": 3,
    "Retry": { "MaxRetryAttempts": 3, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
    "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 } } },
  "AuditClient": { "BaseUrl": "http://nexus-audit-service:12000" },
  "Observability": {
    "Otlp": { "Endpoint": "", "Protocol": "Grpc" },
    "Prometheus": { "Enabled": true }
  },
  "Logging": { "File": { "Path": "logs/identity-.log", "RetainedFileCountLimit": 7 } },
  "Seeder": { "DevSeedAdminEnabled": false, "DevSeedAdminUsername": "admin" }
}
```

**`appsettings.Development.json`** — `Persistence:AutoMigrate=true`, `Seeder:DevSeedAdminEnabled=true`, console sink set to plain coloured output.
**`appsettings.Production.json`** — `Observability:Otlp:Endpoint=http://otel-collector:4317`, `Persistence:FailFast=true`.

All options bound with `services.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

## 15. OpenAPI surface

- `Swashbuckle.AspNetCore` produces OpenAPI at `/swagger/v1/swagger.json`. Swagger UI at `/swagger`. ReDoc UI middleware (`Swashbuckle.AspNetCore.ReDoc`) at `/redoc`.
- Configure `SwaggerGen` to read XML doc comments (from `GenerateDocumentationFile`). Add `OperationFilter` to attach `Idempotency-Key`, `If-Match` header parameters where applicable, matching the reference spec.
- CI step (out of scope but documented): `spectral lint specs/identity.openapi.json` and a diff against the generated `/swagger/v1/swagger.json` — fail build on divergence.

---

## 16. Static documentation (Redocly CLI)

`docs/redocly.yaml`:

```yaml
extends: [recommended]
apis:
  identity@v1:
    root: ../../../specs/identity.openapi.json
```

`docs/package.json` exposes `npm run docs:build` → `redocly build-docs ../../../specs/identity.openapi.json -o site/index.html`. `docs/site/` is gitignored.

---

## 17. Dockerfile (multi-stage, Alpine, no HEALTHCHECK)

```dockerfile
# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Nexus.Identity.sln", "./"]
COPY ["src/Nexus.Identity.Api/Nexus.Identity.Api.csproj", "src/Nexus.Identity.Api/"]
COPY ["tests/Nexus.Identity.Api.Tests/Nexus.Identity.Api.Tests.csproj", "tests/Nexus.Identity.Api.Tests/"]
RUN dotnet restore "Nexus.Identity.sln"
COPY . .
RUN dotnet publish "src/Nexus.Identity.Api/Nexus.Identity.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:8000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8000
ENTRYPOINT ["dotnet", "Nexus.Identity.Api.dll"]
```

`.dockerignore`: `**/bin`, `**/obj`, `**/.vs`, `docs/site`, `logs`, `*.user`, `.git`, `tests`.

---

## 18. Test project (xUnit + FluentAssertions + NSubstitute + Bogus + coverlet)

```
tests/Nexus.Identity.Api.Tests/
├── Features/
│   ├── Auth/
│   │   ├── Validators/   (TokenRequestValidatorTests, RefreshRequestValidatorTests)
│   │   ├── Service/      (AuthServiceTests, JwtIssuerTests, JwksProviderTests)
│   │   └── Controller/   (AuthControllerTests)
│   └── Admins/
│       ├── Validators/   (CreateAdminRequestValidatorTests, PatchAdminRequestValidatorTests)
│       ├── Repository/   (AdminRepositoryTests — InMemory or SQLite-in-memory provider)
│       ├── Service/      (AdminServiceTests)
│       └── Controller/   (AdminsControllerTests)
├── Infrastructure/
│   ├── Errors/           (ProblemDetailsMiddlewareTests)
│   └── Audit/            (HttpAuditPublisherTests — NSubstitute HttpMessageHandler)
├── Fakers/               (AdminFaker, RefreshTokenFaker — Bogus)
└── Nexus.Identity.Api.Tests.csproj
```

- `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `NSubstitute`, `Bogus`, `coverlet.collector`, `Microsoft.EntityFrameworkCore.InMemory` (for repo tests).
- Target: every public method on validators, services, repositories, and controllers has at least one happy-path and one failure test. Coverage target ≥80% line — not enforced as a gate in Phase 1 but reported by `dotnet test --collect "XPlat Code Coverage"`.
- **Integration tests are deferred** (`tests/Nexus.Identity.Api.IntegrationTests/` is not created in Phase 1).

---

## 19. Documentation comments

XML doc comments are mandatory on every public type and public member of: validators, repositories, services, controllers, options classes, middleware. `GenerateDocumentationFile=true` ensures Swashbuckle consumes them. Lint guideline: enable IDE warning `CS1591` only via `.editorconfig` `dotnet_diagnostic.CS1591.severity = suggestion` (kept lenient because `TreatWarningsAsErrors=false`).

---

## 20. Top-level service artifacts

- **`README.md`** — sections: Overview, Tech stack, Prerequisites (`.NET 10 SDK`, SQL Server, Docker), Quick start (`dotnet run`, `docker build`/`run`), Configuration reference, API docs links (`/swagger`, `/redoc`, `docs/site/`), Endpoint summary table.
- **`CHANGELOG.md`** — Keep-a-Changelog format, starts with `## [0.1.0] - Unreleased`.
- **`TROUBLESHOOTING.md`** — common failures: DB unreachable on startup, JWKS empty, refresh token rejected, rate limit triggered, OTLP exporter errors, container fails to start as non-root (permissions on logs path).
- **`CONTRIBUTING.md`** — branching model, commit convention (Conventional Commits), local dev loop, how to add a feature folder, how to add a migration, how to update OpenAPI surface.
- **`LICENSE`** — MIT, `Copyright (c) 2026 Nexus HA Engineering`.
- **`.gitignore`** — standard VS/dotnet + `docs/site/`, `logs/`, `*.user`, `appsettings.*.local.json`.
- **`.editorconfig`** — 4-space, CRLF, `file_scoped_namespace`, `var` preferences.

---

## 21. Dev seeding

When `Seeder:DevSeedAdminEnabled=true`, on startup the service:

1. Checks `Admins` table count.
2. If empty, creates an admin with username `Seeder:DevSeedAdminUsername` (default `admin`) and a random 24-char password.
3. **Logs the password to console exactly once at WARN level** (Development only). Never persists the plaintext.

This entire path is excluded by `IHostEnvironment.IsDevelopment()` AND the config flag — both must hold.

---

## 22. Verification

Run, in order, locally:

```powershell
dotnet build Nexus.Identity.sln
dotnet test  Nexus.Identity.sln --collect "XPlat Code Coverage"
docker build -t nexus-identity-service:dev .
docker run --rm -p 8000:8000 -e ConnectionStrings__Default="<your-sql>" nexus-identity-service:dev
# In another shell:
curl -s http://localhost:8000/health/ready
curl -s http://localhost:8000/api/v1/auth/jwks
```

Plus, in `docs/`:

```powershell
npm install -g @redocly/cli
npx @redocly/cli lint ../../specs/identity.openapi.json
npm run docs:build
```

Acceptance:

- All xUnit tests pass.
- `/swagger/v1/swagger.json` exposes every operation in [`../../../specs/identity.openapi.json`](../../../specs/identity.openapi.json) with matching `operationId`s.
- `docs/site/index.html` renders without errors.
- `docker run` boots; banner + config panel display; `/health/ready` returns 200; `/metrics` returns Prometheus exposition format.
- Manual contract test pass per [`CONTRACT_TESTING.md`](CONTRACT_TESTING.md).

---

## Open items for the dev to confirm with the architect

1. Should `SigningKey.PrivateKeyPemEncrypted` be encrypted at rest via ASP.NET Data Protection? — Plan assumes yes.
2. Should refresh tokens be **rotated mandatorily** on every refresh (current plan) or only sliding-extended? — Plan: rotate mandatorily.
3. JWKS cache lifetime in downstream services is fixed by `Cache-Control: max-age=300`. Confirm 5-minute key staleness window is acceptable.
