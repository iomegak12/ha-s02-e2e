# Changelog

All notable changes to `nexus-audit-service` are documented here.

The format is loosely based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
the project follows semantic versioning once the first tagged release ships.

## [Unreleased]

### Phase 8 — PII redaction hook for the patient-purge worker

- New endpoint **`POST /api/v1/audit/redact`** (admin-only, idempotent via `Idempotency-Key` header):
  - Body: `{ entityType, entityId, redactionPlaceholder? }`.
  - Sets `Summary = redactionPlaceholder` (default `[REDACTED]`) and nulls `DiffJson` on every audit entry matching `(entityType, entityId)`.
  - Response: `{ entityType, entityId, rowsRedacted }`.
- This is the **only** mutation allowed on the otherwise append-only audit log. It powers the patient-service 30-day purge worker (Phase 8 in the patient service).
- `IAuditEntryRepository.RedactByEntityAsync` + EF impl added; `IAuditService.RedactByEntityAsync` delegates.
- `RedactAuditEntriesRequestValidator` enforces `entityType` ≤ 64, non-empty `entityId`, placeholder ≤ 256.
- Test: `AuditEntryRepositoryTests.RedactByEntityAsync_replaces_summary_and_nulls_diff_for_matching_rows` — 47/47 unit tests pass.


### Phase 6 — Packaging, static docs, contract sign-off

- Redocly setup under `docs/`: `redocly.yaml` (extends `recommended`, root = `specs/audit.openapi.json`) and `package.json` with `docs:lint` / `docs:build` / `docs:preview` scripts wired to `@redocly/cli`. Static site output (`docs/site/`) is gitignored.
- Spec lint: `npx @redocly/cli lint ../../../specs/audit.openapi.json` → "API description is valid" (2 cosmetic warnings about the spec's own `servers[0]` localhost URL — pre-existing in the authoritative spec, not service-owned).
- README expanded with a static-docs section, a verification recipe, and pointers to all four docs in the service tree.
- Coverage: 66.7% line. Feature code (services, repositories, validators, filters, idempotency state machine, trim worker) is densely covered by the 46-test suite; the gap is the `Program.cs` composition root (Serilog / OTel / rate-limiter / Swagger wiring). Below the 80% target stated in the implementation plan — acknowledged trade-off; raising it would require black-box smoke tests against a running container.
- Docker: `Dockerfile` shape mirrors the working `nexus-identity-service` image (multi-stage `sdk:10.0-alpine` → `aspnet:10.0-alpine`, non-root `nexus` user, `EXPOSE 12000`). `docker build` was not executed locally because the Docker daemon is offline in this environment — flagged as a manual verification step.
- Contract sign-off: the `docs/CONTRACT_TESTING.md` playbook is unchanged; the running service exposes all three `operationId`s (`appendAuditEntry`, `queryAuditEntries`, `getAuditEntryById`) at `/swagger/v1/swagger.json` as asserted by `AuditEndpointsIntegrationTests.SwaggerJson_exposes_all_three_operationIds`. End-to-end walk requires the live stack (Identity + SQL Server) and is left as the deployment-time smoke.

### Phase 5 — Observability, rate limiting, CORS, trim worker

- Configuration: `RateLimitingOptions`, `CorsOptions`, `ObservabilityOptions` (+ `OtlpOptions`, `PrometheusOptions`), bound with validation (CORS validates `*` origins are not combined with credentials).
- Logging: `SerilogBootstrap` — coloured console in Dev / compact JSON in Prod, plus a daily rolling file (`logs/audit-.log`, 7-day retention), enriched with `service`, `MachineName`, `EnvironmentName`. `UseSerilogRequestLogging` registered in the pipeline.
- Telemetry: `OtelBootstrap` wires OpenTelemetry traces (AspNetCore + HttpClient) and metrics (AspNetCore + HttpClient + Runtime + the `Nexus.Audit` meter) with OTLP gRPC export (gated on `Observability:Otlp:Endpoint`) and a Prometheus exporter exposed at `/metrics`.
- Custom metrics (`Infrastructure/Observability/AuditMetrics.cs`): `nexus_audit_entries_appended_total{sourceService,entityType,action}`, `nexus_audit_idempotency_replays_total{sourceService}`, `nexus_audit_idempotency_conflicts_total{sourceService}`. `AuditService` increments all three at the correct decision points.
- CORS: `AddDefaultPolicy` driven by `CorsOptions` (`*` shortcuts respected), enabled via `UseCors`.
- Rate limiting: fixed-window `PartitionedRateLimiter` — bypasses `/health*`, `/metrics`, `/swagger*`, `/redoc*`; partitions append endpoint by `X-Source-Service` (`AppendPermitLimit=500/min`), all other authenticated paths by IP (`PermitLimit=100/min`). Rejections raise a `DomainException` mapped to `429 RATE_LIMITED` with `Retry-After`. Off by default per Phase 1.
- Background worker (`Infrastructure/Idempotency/IdempotencyTrimWorker.cs`): `BackgroundService` that deletes idempotency records older than `Idempotency:RetentionDays` every `Idempotency:TrimIntervalMinutes`. Resilient to transient errors and to the missing `AuditDbContext` (skipped when no connection string). Audit entries themselves are never deleted.
- Startup banner panels now reflect the actual config (rate-limit state, CORS origins, OTLP endpoint, Prometheus toggle).
- Tests: 2 new — `TrimOnceAsync` removes only expired records (7-day cutoff exercised at the boundary) and leaves audit entries untouched. All 46 tests green.

### Phase 4 — Audit service & controller (3 ops)

- `IAuditService` / `AuditService` implementing the idempotency state machine:
  - new key → insert entry + idempotency record in a single context, server-stamping `Id` / `ReceivedAtUtc` / `SourceService` / `IdempotencyKey`.
  - identical replay → return the original entry, surfaced as `X-Idempotent-Replay: true` + HTTP 200.
  - different payload, same `(SourceService, Idempotency-Key)` → `409 IDEMPOTENCY_CONFLICT`.
  - Payload identity established by SHA-256 over the canonical camelCase JSON of the request (`AuditService.ComputePayloadHash`).
- `AuditEndpoints.MapAuditEndpoints` registers the three OpenAPI operations under `/api/v1/audit`, guarded by `AdminOnly`, with `IdempotencyKeyFilter` + `ValidationFilter<AppendAuditEntryRequest>` chained onto `appendAuditEntry`.
- `AuditHeaderOperationFilter` decorates the `appendAuditEntry` Swagger doc with the required `Idempotency-Key` and optional `X-Source-Service` header parameters.
- Swashbuckle + ReDoc UIs exposed at `/swagger` and `/redoc`; `AddSecurityDefinition("Bearer")` + global requirement so the UI offers a token field.
- DI: `IAuditService`, `IdempotencyKeyFilter`, and `ValidationFilter<AppendAuditEntryRequest>` registered. Phase-3 probe endpoint retired.
- `PersistenceRegistration.Add` now skips SQL Server registration when `ConnectionStrings:Default` is empty (allows tests to substitute providers cleanly).
- Tests: 13 new — 6 service-level (insert / replay / conflict / cross-source key reuse / hash stability) + 7 HTTP-level (missing-key, happy path, replay, conflict, 422, 404, paged query, swagger.json exposes all 3 operationIds). Auth integration tests repointed to the audit endpoints. All 44 tests green.

### Phase 3 — AuthN/Z

- `AuthOptions` (Authority / Audience / Issuer / optional JwksUrl override) bound with data-annotation validation.
- `JwksKeyResolver` wraps `ConfigurationManager<JsonWebKeySet>` with a small custom `IConfigurationRetriever`, refreshing keys every 15 minutes (1-minute throttle on miss). Exposed as an `IssuerSigningKeyResolver`.
- `AuthRegistration` wires JWT bearer (`MapInboundClaims=false`, `NameClaimType=preferred_username`, `RoleClaimType=role`, 30 s skew) and the `AdminOnly` policy (`RequireAuthenticatedUser().RequireRole("Admin")`).
- `CurrentUser` projection (sub → GUID, preferred_username, IsAdmin) for downstream stamping.
- `HealthCheckRegistration` exposes `/health/live`, `/health/ready` (SQL Server + JWKS URI), and `/health` with JSON responses.
- Phase-3 smoke endpoint `GET /api/v1/probe` guarded by `AdminOnly`; superseded by the audit controller in Phase 4.
- Tests: 4 new integration tests using `WebApplicationFactory<Program>` + `JwtBearerEvents.OnMessageReceived` shim to validate 401 / 403 / 200 / health-unprotected. All 31 tests green.

### Phase 2 — Validation, errors, idempotency filter

- RFC 9457 error pipeline: `ErrorCodes`, `DomainException` (+ `ProblemError`), `ProblemDetailsMiddleware` mapping both domain and unhandled exceptions to `application/problem+json` with `code`, `traceId`, `type`, `instance`.
- `ValidationFilter<T>` endpoint filter resolving any `IValidator<T>` from DI and surfacing field-level errors as `422 AUDIT_VALIDATION`.
- `AppendAuditEntryRequestValidator` (FluentValidation): bounds matching the DB schema, canonical-action membership, 64 KB `DiffJson` cap, and the clock-skew rule (`occurredAtUtc` ∈ [`now − 30d`, `now + MaxClockSkewMinutes`]).
- Idempotency wiring (consumed by Phase 4 controllers): `IdempotencyOptions`, `IdempotencyContext`, `SourceServiceResolver` (header → JWT `iss` → `unknown`, always lower-cased), and `IdempotencyKeyFilter` rejecting missing / empty / oversize keys with `400 IDEMPOTENCY_KEY_MISSING`.
- DI: `TimeProvider.System`, `AddValidatorsFromAssemblyContaining<Program>`, `IdempotencyOptions` bound with `ValidateDataAnnotations`. `ProblemDetailsMiddleware` registered at the top of the pipeline.
- Tests: 19 new — validator (10), idempotency filter (5), problem-details middleware (4). All 27 tests green.

### Phase 1 — Persistence & domain model

- Domain models: `AuditEntry`, `IdempotencyRecord`, `AppendAuditEntryRequest`, `AuditEntryListItem`, `PagedAuditEntries`, `AuditQuery`, plus the `AuditActions` constants.
- `AuditDbContext` (database `NexusAudit`) with the four `AuditEntries` indexes (entity lookup, actor lookup, occurred-desc, filtered-unique `(SourceService, IdempotencyKey)`) and `IdempotencyRecords` uniqueness + created-at indexes.
- EF Core migration `InitialAuditSchema` generated under `Infrastructure/Persistence/Migrations/`.
- Repositories: `IAuditEntryRepository` / `AuditEntryRepository` (Add / GetById / Query — no mutation paths), `IIdempotencyRepository` / `IdempotencyRepository` (Find / Add, scoped by lowercased `SourceService`).
- Pagination helpers (`PageRequest` capped at 100, `PagedResult<T>`).
- DI: `PersistenceRegistration.Add` plus `ApplyMigrationsAsync` honouring `Persistence:AutoMigrate` + `Persistence:FailFast`.
- Tests: repository round-trip, query filters (entity/actor/date-range/source-service), source-service scoping for idempotency — 8 tests green using EF InMemory.

### Phase 0 — Scaffolding

- Solution skeleton: `Nexus.Audit.sln`, `src/Nexus.Audit.Api`, `tests/Nexus.Audit.Api.Tests`.
- Minimal Kestrel host on port `12000` with the Spectre.Console "Nexus Audit" banner and startup summary panels.
- Baseline configuration (`appsettings.json` + Development + Production overlays) shaped per `docs/IMPLEMENTATION_PLAN.md` §14.
- Top-level service artefacts: `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE` (MIT 2026), `.gitignore`, `.dockerignore`, `.editorconfig`, `Dockerfile`, `dotnet-tools.json`.
