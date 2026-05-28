# Changelog

All notable changes to `nexus-identity-service` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

### Added

- Phase 0: solution + project scaffolding (`Nexus.Identity.sln`, `Nexus.Identity.Api`, `Nexus.Identity.Api.Tests`).
- Minimal `Program.cs` exposing `GET /health/live` on port 8000.
- Top-level docs: `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE`.
- `.gitignore`, `.dockerignore`, `.editorconfig`.
- Phase 1: cross-cutting infrastructure.
  - Strongly-typed options (`JwtIssuer`, `RateLimiting`, `Cors`, `Resilience`, `AuditClient`, `Observability`, `Seeder`) bound via `AddOptions().ValidateDataAnnotations().ValidateOnStart()`.
  - RFC 9457 `application/problem+json` error model: `ErrorCodes`, `DomainException`, `ProblemDetailsMiddleware`, FluentValidation `ValidationFilter`.
  - Pagination + sort primitives (`PageRequest`, `PagedResult<T>`, `SortParser`).
  - Serilog bootstrap (console + daily rolling file with 7-day retention) and OpenTelemetry bootstrap (AspNetCore/Http/Runtime instrumentation, OTLP + Prometheus exporters, custom `Nexus.Identity` meter with `nexus_identity_tokens_issued_total` counter).
  - Health checks (`/health`, `/health/live`, `/health/ready`) with `ready` tag filter and rate-limit bypass.
  - Spectre.Console banner + 3-panel startup summary (Configuration / Dependencies / Toggles).
  - Swashbuckle Swagger + ReDoc UIs with Bearer security scheme and `HeaderConventionsOperationFilter` (Idempotency-Key on POST, If-Match on PATCH/PUT/DELETE).
  - CORS default policy and global `PartitionedRateLimiter` (per-IP fixed window, bypass for `/health`, `/metrics`, `/api/v1/auth/jwks`, rejection emits ProblemDetails + `Retry-After`).
  - `appsettings.{json,Development.json,Production.json}` populated per §14 of the plan.
  - Diagnostic endpoint `/api/v1/_diag/boom` validating the ProblemDetails pipeline (to be removed in Phase 4).
- Phase 2: persistence layer (EF Core 10 + SQL Server).
  - Entities: `Admin`, `RefreshToken`, `SigningKey` (under `Infrastructure/Persistence/Entities/`).
  - `IdentityDbContext` with unique indexes on `Admins.Username` and `RefreshTokens.TokenHash`, cascade FK from `RefreshTokens` to `Admins`, and `rowversion` concurrency token on `Admin`.
  - `RowVersionInterceptor` (`SaveChangesInterceptor`) stamps `CreatedAtUtc` / `UpdatedAtUtc` on admin writes.
  - `PersistenceRegistration` wires the DbContext with `EnableRetryOnFailure()` and optional `MigrateAsync` on startup gated by `Persistence:AutoMigrate` and `Persistence:FailFast`.
  - `PersistenceOptions` configuration class (binds `Persistence` section).
  - Initial migration `InitialIdentitySchema` under `Infrastructure/Persistence/Migrations/`.
  - SQL Server health check tagged `ready` (skipped when connection string is empty).
  - Local tool manifest with `dotnet-ef` 10.0.0 for migration authoring.
- Phase 3: Auth vertical slice.
  - DTOs (`TokenRequest`, `TokenResponse`, `RefreshRequest`, `JwksResponse`, `JwkKey`) and FluentValidation validators (`TokenRequestValidator`, `RefreshRequestValidator`).
  - `IRefreshTokenRepository` / `RefreshTokenRepository` (EF Core) under `Features/Auth/Repository/`.
  - `ISigningKeyStore` / `SqlSigningKeyStore` — self-bootstraps a 2048-bit RSA key on first use under a process-wide semaphore.
  - `IJwksProvider` / `JwksProvider` — serves published (non-retired) JWKs with `Cache-Control: public, max-age=300`.
  - `IJwtIssuer` / `JwtIssuer` — RS256 access tokens via `JsonWebTokenHandler` with `sub`, `preferred_username`, `name`, `role=Admin`, `iss`, `aud`, `iat`, `nbf`, `exp`, `jti`.
  - `IAuthService` / `AuthService` — credential auth (BCrypt verify), refresh-token rotation (SHA-256 hash at rest, old token marked `RevokedAtUtc` + `ReplacedByTokenId`), idempotent revoke.
  - `Infrastructure/Security/{IPasswordHasher,BCryptPasswordHasher}` — BCrypt with work factor 12.
  - `AuthEndpoints.MapAuthEndpoints()` — `POST /api/v1/auth/{token,refresh,revoke}` + `GET /.well-known/jwks.json` (rate-limit bypass updated to match the well-known path).
  - Packages: `BCrypt.Net-Next` 4.0.3, `Microsoft.IdentityModel.JsonWebTokens` 8.3.1, `Microsoft.IdentityModel.Tokens` 8.3.1.
- Phase 4: Admins vertical slice.
  - DTOs (`AdminDto`, `AdminCreateRequest`, `AdminPatchRequest`, `PagedAdminResponse`) under `Features/Admins/Models/`, wire-shapes matching the OpenAPI contract.
  - Validators (`AdminCreateRequestValidator`, `AdminPatchRequestValidator`) — username regex `^[a-z0-9._-]+$` (3–64), display name 2–120, password 8–128, patch must include at least one of `displayName` / `isActive`.
  - `IAdminRepository` / `AdminRepository` (EF Core) with paged list, active filter, and sort whitelist (`createdAtUtc`, `username`, `displayName`).
  - `IAdminService` / `AdminService` with create (duplicate-username → 409 `ADMIN_USERNAME_DUPLICATE`), patch (If-Match ETag → 409 `ETAG_MISMATCH`), idempotent soft delete, and `sort=field:asc|desc` parser (422 `ADMIN_VALIDATION` on bad field/direction).
  - `AdminsEndpoints.MapAdminsEndpoints()` — `GET/POST /api/v1/admins`, `GET/PATCH/DELETE /api/v1/admins/{id}`, all behind `RequireAuthorization("AdminOnly")`, emits `ETag` header (base64 row-version) on single-entity responses.
  - JWT bearer authentication wired with `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.0; `TokenValidationParameters` configured for issuer/audience/lifetime/key validation, role claim `role`, name claim `preferred_username`.
  - `LocalSigningKeyResolver` (singleton) resolves `RsaSecurityKey`s from `ISigningKeyStore` with a 5-minute in-memory cache (rebuilds public keys from each stored JWK), wired into `JwtBearerOptions` via `Configure<LocalSigningKeyResolver>` to avoid `BuildServiceProvider()`.
  - `AdminOnly` authorization policy = `RequireAuthenticatedUser().RequireRole("Admin")`.
  - Removed the temporary `/api/v1/_diag/boom` diagnostic endpoint.
- Phase 5: outbound audit emission.
  - `AuditEntryDto`, `IAuditPublisher`, `NullAuditPublisher`, `HttpAuditPublisher`, `AuditRegistration` under `Infrastructure/Audit/`.
  - Typed `HttpClient` (`AuditClient`) registered with `AddStandardResilienceHandler` (timeouts, retry with jitter, circuit breaker) sourced from `Resilience:Audit`.
  - Caller bearer token is forwarded; a fresh UUID v7 `Idempotency-Key` is sent on every POST.
  - Readiness probe `HEAD {AuditClient:BaseUrl}/api/v1/audit?size=1` registered as `Degraded` (not `Unhealthy`) under the `ready` tag so user traffic is unaffected if audit is unreachable.
  - `AuditClient.Enabled` toggle (default `true`); set `false` in `appsettings.Development.json` because the audit service is not yet built — selects `NullAuditPublisher` and skips the readiness probe.
  - `AdminService` injects `IAuditPublisher` + `IHttpContextAccessor` and emits `Created` / `Updated` / `StatusChanged` events on Admin lifecycle changes, capturing actor from JWT (`sub` + `preferred_username`).
  - Publishing is fire-and-forget: failures are logged and swallowed inside `HttpAuditPublisher`; user requests are never blocked by audit transport latency or outages.
  - Added package `Microsoft.Extensions.Http.Resilience` 9.0.0.
- Phase 6: xUnit test suite (76 tests, all green).
  - Test packages added: `FluentAssertions` 6.12.2, `NSubstitute` 5.1.0, `Bogus` 35.6.1, `Microsoft.EntityFrameworkCore.InMemory` 10.0.0, `Microsoft.EntityFrameworkCore.Sqlite` 10.0.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.0.
  - `Fakers/AdminFaker.cs`, `Fakers/RefreshTokenFaker.cs` — Bogus-based entity builders.
  - Validator tests for `TokenRequest`, `RefreshRequest`, `AdminCreateRequest`, `AdminPatchRequest` (regex, length boundaries, at-least-one-of rule).
  - Service tests:
    - `AuthServiceTests` — invalid creds (401 `INVALID_CREDENTIALS`), inactive user, happy-path token pair, refresh rotation (old token revoked + `ReplacedByTokenId` set), `REFRESH_INVALID` paths (unknown / revoked / expired), idempotent revoke.
    - `JwtIssuerTests` — claim set (`sub`, `preferred_username`, `name`, `role`, `jti`), iss/aud, RS256 + kid, signature verification with public key.
    - `JwksProviderTests` — well-formed JWK passthrough, malformed JWK skipped with warning.
    - `AdminServiceTests` — duplicate-username 409, happy-path create emits `Created`, ETag-mismatch 409, status-change emits `StatusChanged`, field-only emits `Updated`, no-op patch emits nothing, idempotent deactivate, bad-sort 422.
  - Repository tests: `AdminRepositoryTests` covering `GetById`, `GetByUsername`, `ExistsByUsername`, paged `ListAsync` ordering + `isActive` filter, `AddAsync` (EF InMemory).
  - Infrastructure tests:
    - `ProblemDetailsMiddlewareTests` — RFC 9457 payload shape, `errors` array, unhandled-exception → 500 `INTERNAL_ERROR`, no-throw passthrough.
    - `HttpAuditPublisherTests` — POSTs `/api/v1/audit`, sends UUID v7 `Idempotency-Key`, forwards caller bearer, swallows non-success status / `HttpRequestException` / cancellation.
  - Integration test: `AdminFlowIntegrationTests` (one happy-path WAF test) — `/health/live` 200; seeded admin → `POST /api/v1/auth/token` → `POST /api/v1/admins` (201 + ETag) → `PATCH /api/v1/admins/{id}` (200). Hosts via `WebApplicationFactory<Program>` with EF Core InMemory provider (isolated internal service provider) and `AuditClient:Enabled=false`.
- Phase 7: container image + static API documentation.
  - `Dockerfile` — multi-stage Alpine build: `sdk:10.0-alpine` (restore → publish Release) → `aspnet:10.0-alpine` runtime; non-root `nexus` user; `EXPOSE 8000`; `ASPNETCORE_URLS=http://+:8000`; `ASPNETCORE_ENVIRONMENT=Production`; `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`.
  - `docs/redocly.yaml` — Redocly `recommended` ruleset, `identity@v1` API root pointing at `specs/identity.openapi.json`.
  - `docs/package.json` — exposes `npm run docs:build` (`redocly build-docs … -o site/index.html`), `docs:lint`, and `docs:preview` scripts; dev-dependency on `@redocly/cli ^1.34.0`.
- Phase 8: final verification (all §22 checks pass).
  - `dotnet build Nexus.Identity.sln --configuration Release` — 0 warnings, 0 errors.
  - `dotnet test Nexus.Identity.sln --collect "XPlat Code Coverage" --configuration Release` — 76/76 passed; Cobertura XML produced.
  - `docker build -t nexus-identity-service:dev .` — image built (219 MB), runtime stage uses `icu-libs` + `tzdata` on Alpine for full globalization support (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`); Dockerfile restores the API project only (tests excluded by `.dockerignore`).
  - `docker run … nexus-identity-service:dev` — banner + config panel displayed; `GET /health/live` → 200 Healthy; `GET /.well-known/jwks.json` → 200 with 1 active key; `GET /metrics` → 200 Prometheus exposition format; `GET /health/ready` → Degraded (SQL Healthy, audit-service unreachable as expected in standalone smoke test).
  - `npx @redocly/cli lint …/identity.openapi.json` — spec valid; 2 cosmetic warnings (localhost server URL, proprietary license URL — both in spec, not in code).
  - `npm run docs:build` — `docs/site/index.html` bundled (155 KiB).
