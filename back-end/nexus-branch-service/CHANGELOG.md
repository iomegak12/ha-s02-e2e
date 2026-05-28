# Changelog

All notable changes to `nexus-branch-service` are documented here.

## [Unreleased]

### Phase 9 — Packaging + compose + cross-service smoke

- `nexus-branch-service` added to `back-end/docker-compose.yml` (port 11000) — depends on SQL Server (healthy) + Identity + Audit + OTel collector. Connection string targets `NexusBranches`, audit publisher points at `nexus-audit-service:12000`.
- Live-stack smoke `Nexus.IntegrationTests.Branches.BranchesSmokeTests` covers `/health/live`, Swagger surface (`listBranches`/`createBranch`/`getBranchById`/`patchBranch`/`deactivateBranch`), 401 without bearer, paged list with admin bearer.
- Headline `EndToEndFlowTests.Full_flow_branch_patient_doctor_document_audit` exercises Branches as the entry point of the cross-service flow.

### Phase 4 — Branches full feature surface

- DTOs: `BranchDto`, `BranchCreateRequest`, `BranchPatchRequest`, `PagedBranchResponse` (wire shape matches `specs/branches.openapi.json`).
- FluentValidation: `BranchCreateRequestValidator` enforces `code ^[A-Z]{2,5}$`, name 2–120, city 2–80; `BranchPatchRequestValidator` keeps `code` immutable and rejects empty patches.
- `IBranchesService` / `BranchesService` implements the 5 operations:
  - **listBranches** — paged + `isActive`/`city` filter.
  - **createBranch** — uppercases code, rejects duplicate active code with `409 BRANCH_CODE_DUPLICATE`, emits `BranchCreated` audit event, returns `ETag`.
  - **getBranchById** — emits `ETag`; 404 on miss.
  - **patchBranch** — honours `If-Match` (`409 ETAG_MISMATCH` on stale or DB concurrency exception), emits `BranchUpdated` audit with before/after diff.
  - **deactivateBranch** — soft-delete (`IsActive=false`), idempotent for already-inactive rows, emits `BranchDeactivated` audit. Code re-use after deactivation works thanks to the Phase 1 filtered-unique index.
- `BranchesEndpoints` minimal-API mapping under `/api/v1/branches` with `AdminOnly` policy, `IdempotencyKeyFilter` on writes, `ValidationFilter<T>` on create/patch.
- Swagger UI + ReDoc mounted at `/swagger` and `/redoc`.
- Metrics tagged via `BranchMetrics` counters (`nexus_branches_created_total` / `_updated_total` / `_deactivated_total`).
- Tests: 12 new `BranchesServiceTests` covering create + dedup + uppercase, get (incl 404), patch (incl 404 + ETag mismatch), deactivate (incl idempotent + 404), list filter, code reuse after deactivation. Repository + audit-outbox suites unchanged. Total 22 unit tests pass.

### Phase 3 — Audit publisher + PENDING_AUDIT outbox + reconciler

- **Schema**: `PendingAuditEntry` outbox table added to `BranchDbContext` with unique-key index on `IdempotencyKey` and lookup index on `NextRetryUtc`; migration `AddPendingAuditOutbox`.
- **Configuration**: `AuditClientOptions` (BaseUrl / Enabled / SourceService / ReconcilerIntervalSeconds / ReconcilerBatchSize) bound with validation.
- **Publisher chain** (per LLD §14 fail-open + durable buffer):
  - `AuditEventDto` — wire shape matching `specs/audit.openapi.json`.
  - `IAuditPublisher.PublishAsync(dto, ct)` — fire-and-forget. Never throws to callers.
  - `NullAuditPublisher` — substituted when `AuditClient:Enabled=false`.
  - `HttpAuditPublisher` — typed `HttpClient` named `AuditClient` registered with `AddStandardResilienceHandler` (5 s per-attempt timeout, 10 s total, 3 retries with jittered exponential backoff, 50% failure-ratio circuit breaker over 10 s, 30 s break). Forwards caller's bearer via `IHttpContextAccessor`; sets `Idempotency-Key` (UUIDv7) + `X-Source-Service=nexus-branch`. Exposes `TryPublishAsync(dto, ct)` / `TryPublishAsync(dto, idempotencyKey, ct)` returning `bool`.
  - `OutboxAuditPublisher` (default `IAuditPublisher` when enabled) — tries sync HTTP first; on `false` enqueues to outbox.
  - `IPendingAuditOutbox` / `PendingAuditOutbox` — EF impl: `EnqueueAsync`, `FetchDueAsync`, `DeleteAsync`, `BackoffAsync` (exponential 30s → 1m → 2m → 4m → 8m → 15m cap, captures `LastError`).
  - `PendingAuditWorker` — `BackgroundService` running every `ReconcilerIntervalSeconds`. Drains due rows, retries each via `HttpAuditPublisher.TryPublishAsync` using the row's original `IdempotencyKey` (idempotent retry per LLD §14). On success deletes; on failure backs off. Tolerates missing `BranchDbContext` (logs + retries; doesn't crash host).
- **Health**: `AuditRegistration.AddReadinessProbe` adds `audit-service` URL probe to `/health/ready` (reports `Degraded` not `Unhealthy` so the service stays ready when audit is briefly down).
- **Tests**: 4 new — outbox writes a row on HTTP failure; no row written on HTTP success; backoff increments + pushes `NextRetryUtc`; delete removes the row. All 9 unit tests green.

### Phase 2 — Cross-cutting infrastructure

- **Errors**: `ErrorCodes` (incl. `BRANCH_VALIDATION`, `BRANCH_CODE_DUPLICATE`, `ETAG_MISMATCH`); `DomainException` + `ProblemError`; `ProblemDetailsMiddleware` mapping both domain and unhandled exceptions to RFC 9457 `application/problem+json` with `code` + `traceId`; `ValidationFilter<T>` generic endpoint filter.
- **Auth**: `AuthOptions` (Authority / Audience / Issuer / optional JwksUrl); `JwksKeyResolver` (15-min auto-refresh, 1-min throttle); `AuthRegistration` wiring JWT bearer + `AdminOnly` policy; `CurrentUser` claims projection.
- **Idempotency**: `IdempotencyContext`, `SourceServiceResolver`, `IdempotencyKeyFilter` enforcing the `Idempotency-Key` header on writes (400 IDEMPOTENCY_KEY_MISSING on missing / empty / >128-char keys).
- **Observability**: `SerilogBootstrap` (coloured console Dev / JSON Prod + rolling file `logs/branch-.log` 7-day + OTLP sink); `OtelBootstrap` (AspNetCore + HttpClient + Runtime instrumentation, Prometheus exporter, OTLP exporter when configured); `BranchMetrics` custom counters (`nexus_branches_created_total`, `..._updated_total`, `..._deactivated_total`); `HealthCheckRegistration` exposes `/health/live`, `/health/ready` (SQL + JWKS), `/health`.
- **CORS + rate limiting**: `CorsOptions` (cross-validates `*` + credentials), `RateLimitingOptions` (off by default; fixed-window IP partitioning when on; bypasses `/health*`, `/metrics`, `/swagger*`, `/redoc*`). 429 rejections become `DomainException` → `RATE_LIMITED` problem+json + `Retry-After`.
- **Pipeline**: `ProblemDetailsMiddleware` → `UseSerilogRequestLogging` → CORS → RateLimiter → AuthN → AuthZ → health endpoints → Prometheus scrape endpoint.
- Live boot smoke verified: `/health/live` 200, `/metrics` 200.

### Phase 1 — Persistence & domain model

- Renamed root namespace to `Nexus.Branches.Api` (plural) to avoid collision with the `Branch` entity.
- `Branch` entity (with `IAuditedEntity` marker + RowVersion ETag column).
- `BranchDbContext` (database `NexusBranches`) with filtered-unique `Code` index on `IsActive=true` (allows code reuse after deactivation).
- `AuditedTimestampInterceptor` stamps `CreatedAtUtc` / `UpdatedAtUtc` automatically; SQL Server handles `rowversion`.
- Initial migration `InitialBranchSchema` under `Infrastructure/Persistence/Migrations/`.
- `IBranchRepository` / `BranchRepository` (Add / GetById tracked + no-track / GetActiveByCode / Query paged-filter / SaveChanges).
- Pagination helpers (`PageRequest` capped at 100, `PagedResult<T>`).
- DI: `PersistenceRegistration.Add` skips when connection string empty; `ApplyMigrationsAsync` honours `Persistence:AutoMigrate` + `FailFast`.
- Tests: 5 repository round-trip / filter / paging tests against EF InMemory.

### Phase 0 — Scaffolding

- Solution skeleton: `Nexus.Branch.sln`, `src/Nexus.Branch.Api`, `tests/Nexus.Branch.Api.Tests`.
- Minimal Kestrel host on port `11000` with the Spectre.Console "Nexus Branches" banner and startup summary panels.
- Baseline configuration (`appsettings.json` + Development + Production overlays).
- Top-level service artefacts: `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE` (MIT 2026), `.gitignore`, `.dockerignore`, `.editorconfig`, `Dockerfile`, `dotnet-tools.json`.
