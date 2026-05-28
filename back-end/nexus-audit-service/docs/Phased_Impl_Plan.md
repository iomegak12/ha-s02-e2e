# Nexus HA — Audit Service: Phased Implementation Strategy

## Context

Build `nexus-audit-service` at `c:\000 - HA - S02\e2e\back-end\nexus-audit-service\` as a self-contained .NET 10 / ASP.NET Core microservice on port **12000**. It is the append-only audit sink for the platform (no outbound HTTP, no self-publishing). The authoritative inputs are:

- `back-end/nexus-audit-service/docs/IMPLEMENTATION_PLAN.md` (engineering blueprint)
- `back-end/nexus-audit-service/docs/CONTRACT_TESTING.md` (manual API contract playbook)
- `docs/LLD_Nexus_HA.md` §14 (audit subsystem in the broader architecture)
- `specs/audit.openapi.json` (the spec the service must conform to)

The sibling `nexus-identity-service` already exists with the same feature-folder layout — reuse its solution skeleton, csproj baseline, `.editorconfig`, Dockerfile shape, Spectre.Console banner and Serilog/OTel bootstrap patterns as the template (do **not** project-reference or share NuGets — independence rule).

The strategy below splits work into **7 phases**, each shippable and independently verifiable. Each phase ends green (build + tests + smoke) before the next begins.

---

## Phase 0 — Scaffolding & Solution Skeleton

**Goal:** an empty service that builds, boots, and shows the banner.

- Create `Nexus.Audit.sln`, `src/Nexus.Audit.Api/Nexus.Audit.Api.csproj` (net10.0 Web SDK), `tests/Nexus.Audit.Api.Tests/Nexus.Audit.Api.Tests.csproj`. Copy baseline from `back-end/nexus-identity-service/src/Nexus.Identity.Api/Nexus.Identity.Api.csproj`.
- Top-level files: `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE` (MIT 2026), `.gitignore`, `.dockerignore`, `.editorconfig`, `Dockerfile`, `dotnet-tools.json`.
- `Program.cs` minimal Kestrel on `http://+:12000`, Spectre.Console Figlet "Nexus Audit" banner with empty Configuration / Dependencies / Toggles panels.
- `appsettings.json` + `Development` + `Production` overlays per IMPLEMENTATION_PLAN §14.
- Wire `TreatWarningsAsErrors=true`, nullable enabled, `GenerateDocumentationFile=true`.

**Verify:** `dotnet build`, `dotnet run` shows banner; `dotnet test` runs (zero tests yet).

---

## Phase 1 — Persistence & Domain Model

**Goal:** EF Core code-first persistence ready, migrations applied.

- `Features/Audit/Models/`: `AuditEntry` (immutable record/entity), `AppendAuditEntryRequest`, `AuditEntryListItem`, `PagedAuditEntries`, `IdempotencyRecord`.
- `Infrastructure/Persistence/AuditDbContext` with the two entities, indexes from §4 of the plan (entity-lookup, actor-lookup, occurred-desc, filtered-unique `(SourceService, IdempotencyKey)`).
- `Infrastructure/Persistence/Migrations/` initial migration; honour `Persistence:AutoMigrate` (Dev) and `Persistence:FailFast` (Prod).
- `Features/Audit/Repository/`: `IAuditEntryRepository` exposing only `AddAsync`, `GetByIdAsync`, `QueryAsync` (no update/delete — append-only); `IIdempotencyRepository` with `FindAsync(sourceService, key)` and `AddAsync`.
- Pagination helper at `Infrastructure/Pagination/`.

**Tests:** `Features/Audit/Repository/*Tests` against EF InMemory — round-trip insert, query filters, idempotency lookup. Fakers (`Bogus`) for entities.

**Verify:** `dotnet test` green; running the service against a local SQL Server creates `NexusAudit` with expected schema.

---

## Phase 2 — Validation, Errors, Idempotency Filter

**Goal:** the input contract and the error shape are fully enforced before any business logic exists.

- `Infrastructure/Errors/`: `ProblemDetailsMiddleware`, `DomainException`, `ErrorCodes` (BAD_REQUEST, IDEMPOTENCY_KEY_MISSING, UNAUTHENTICATED, UNAUTHORIZED, NOT_FOUND, IDEMPOTENCY_CONFLICT, AUDIT_VALIDATION, RATE_LIMITED). RFC 9457 shape with `code` + `traceId`.
- `Features/Audit/Validators/AppendAuditEntryRequestValidator` (FluentValidation): field bounds + clock-skew rule (`occurredAtUtc` within `[ReceivedAtUtc − 30d, ReceivedAtUtc + MaxClockSkewMinutes]`).
- `Infrastructure/Idempotency/IdempotencyKeyFilter` (endpoint filter): rejects missing `Idempotency-Key` with `400 IDEMPOTENCY_KEY_MISSING`; surfaces resolved `SourceService` (from `X-Source-Service` header, fallback to JWT `iss`, lowercased).
- `Configuration/IdempotencyOptions`, bound with `ValidateDataAnnotations().ValidateOnStart()`.

**Tests:** validator tests (incl. skew), `IdempotencyKeyFilterTests`, `ProblemDetailsMiddlewareTests`.

**Verify:** unit tests green.

---

## Phase 3 — AuthN/Z

**Goal:** JWT bearer wired against Identity's JWKS; `AdminOnly` policy guards all 3 endpoints.

- `Infrastructure/Auth/`: `JwtBearerConfig` (Authority `http://nexus-identity-service:8000`, MetadataAddress `<authority>/api/v1/auth/jwks`, Audience `nexus-ha`, Issuer `https://api.nexusha.local/identity`, `MapInboundClaims=false`), `AdminRolePolicy`, `ServiceOrAdminPolicy`, `CurrentUser` accessor.
- `Configuration/JwtBearerOptions` bound from `Auth:*`.
- Health: `/health/ready` checks JWKS reachability + SQL Server.

**Tests:** `JwtBearer` policy tests via `TestServer` or NSubstitute auth handler (admin / non-admin / missing token → 200/403/401).

**Verify:** running the service alongside `nexus-identity-service`, a token from `/api/v1/auth/token` authorizes a stubbed protected endpoint.

---

## Phase 4 — Audit Service & Controller (the 3 operations)

**Goal:** all three OpenAPI operations live and behaving per spec.

- `Features/Audit/Service/AuditService` implementing the idempotency state machine:
  - lookup by `(SourceService, IdempotencyKey)` → if hit and payload identical → return stored entry + `X-Idempotent-Replay: true` header; if hit and payload differs → `409 IDEMPOTENCY_CONFLICT`; if miss → insert entry + idempotency record in one transaction.
  - server-stamp `ReceivedAtUtc`; normalize `SourceService` (lowercased literal); store `IdempotencyKey`.
- `Features/Audit/Controller/AuditController`:
  - `POST /api/v1/audit` → `appendAuditEntry` (AdminOnly + IdempotencyKeyFilter + ValidationFilter).
  - `GET /api/v1/audit` → `queryAuditEntries` with filters `entityType`, `entityId`, `action`, `actorId`, `from`, `to`, `sourceService`, `page`, `size`, `sort` (default `occurredAtUtc desc`).
  - `GET /api/v1/audit/{id}` → `getAuditEntryById` (404 on miss).
- Swashbuckle OpenAPI: `OperationFilter` to mark `Idempotency-Key` required and `X-Source-Service` optional on `appendAuditEntry`; expose `/swagger` and `/redoc`.

**Tests:** `AuditServiceTests` (insert / replay / conflict), `AuditControllerTests` (header semantics, replay header echo, query filter wiring).

**Verify:** Swagger UI shows all 3 operationIds matching `specs/audit.openapi.json`; manual `curl` per CONTRACT_TESTING.md §3 happy path + replay + conflict + missing-key all behave.

---

## Phase 5 — Observability, Rate Limiting, CORS, Trim Worker

**Goal:** the cross-cutting NFRs from IMPLEMENTATION_PLAN §10–§13 are in place.

- `Infrastructure/Observability/`: `SerilogBootstrap` (console JSON Prod / coloured Dev + rolling file `logs/audit-.log`, 7-day retention), `OtelBootstrap` (OTLP gRPC, AspNetCore + HttpClient + EFCore + Runtime + Process instrumentation), Prometheus `/metrics`, `HealthCheckRegistration` (`/health/live`, `/health/ready`, `/health`).
- Custom metrics: `nexus_audit_entries_appended_total{sourceService,entityType,action}`, `nexus_audit_idempotency_replays_total{sourceService}`, `nexus_audit_idempotency_conflicts_total{sourceService}`.
- Rate limiting (`Configuration/RateLimitingOptions`): off by default; when on, **source-service partition** for append endpoint (PermitLimit 500/min) and IP-fixed for the rest.
- CORS (`Configuration/CorsOptions`): permissive `* * *` default (Phase 1).
- `Infrastructure/Idempotency/IdempotencyTrimWorker` (`BackgroundService`) deletes idempotency records older than `RetentionDays` every `TrimIntervalMinutes`. Audit entries untouched.
- Banner panels now populated with real config values.

**Tests:** `IdempotencyTrimWorker` with a fake clock (records past TTL removed; entries preserved); metrics counters increment.

**Verify:** `/metrics` exposes counters; `/health/ready` reflects SQL + JWKS state; rate limit toggle gates the append endpoint when enabled.

---

## Phase 6 — Packaging, Static Docs, Contract Sign-off

**Goal:** container ships; docs site renders; manual contract suite passes.

- Finalize `Dockerfile` (sdk:10.0-alpine build → aspnet:10.0-alpine runtime, non-root `nexus` user, `EXPOSE 12000`, env `ASPNETCORE_*`).
- `.dockerignore` per plan §17.
- `docs/redocly.yaml` + `package.json` for `npm run docs:build` against `../../../specs/audit.openapi.json`; gitignore `docs/site/`.
- Fill `README.md` (overview, "this service is the sink — it does not publish", quick start, config matrix), `TROUBLESHOOTING.md` (DB unreachable / JWKS refresh fail / idempotency conflicts / clock skew), `CHANGELOG.md` initial entry.
- Walk through `CONTRACT_TESTING.md` end-to-end and sign off §6.

**Verify:** `dotnet build` + `dotnet test --collect "XPlat Code Coverage"` (≥80% line); `docker build` + `docker run` boots with banner; `curl /health/ready` returns 200; `npx @redocly/cli lint` clean; all rows in CONTRACT_TESTING.md §2 checklist tick.

---

## Critical files (paths under `c:\000 - HA - S02\e2e\back-end\nexus-audit-service\`)

- `Nexus.Audit.sln`
- `src/Nexus.Audit.Api/Program.cs`
- `src/Nexus.Audit.Api/Features/Audit/{Models,Validators,Repository,Service,Controller}/*`
- `src/Nexus.Audit.Api/Infrastructure/{Auth,Persistence,Observability,Errors,Pagination,Idempotency,Startup}/*`
- `src/Nexus.Audit.Api/Configuration/*Options.cs`
- `src/Nexus.Audit.Api/appsettings*.json`
- `tests/Nexus.Audit.Api.Tests/Features/Audit/**` and `Infrastructure/**`
- `Dockerfile`, `.dockerignore`, `.editorconfig`, `.gitignore`
- `docs/redocly.yaml`, `docs/package.json`

## Reusable references (sibling — copy, don't project-reference)

- `back-end/nexus-identity-service/src/Nexus.Identity.Api/Nexus.Identity.Api.csproj` — csproj baseline.
- `back-end/nexus-identity-service/src/Nexus.Identity.Api/Program.cs` — bootstrap order template (Serilog → OTel → AuthN → DI → middleware → Spectre banner).
- `back-end/nexus-identity-service/Dockerfile` — multi-stage alpine shape.
- `back-end/nexus-identity-service/dotnet-tools.json` — local tools (`dotnet-ef`).

## End-to-end verification

1. `dotnet build Nexus.Audit.sln` (warnings-as-errors).
2. `dotnet test Nexus.Audit.sln --collect "XPlat Code Coverage"` → ≥80% line coverage.
3. `docker build -t nexus-audit-service:dev .` then `docker run --rm -p 12000:12000 -e ConnectionStrings__Default=... nexus-audit-service:dev`.
4. `curl http://localhost:12000/health/ready` → 200.
5. Spec check: `/swagger/v1/swagger.json` exposes `appendAuditEntry`, `queryAuditEntries`, `getAuditEntryById` identical to `specs/audit.openapi.json`.
6. Walk `CONTRACT_TESTING.md` §3 against a running stack (Identity + Audit + SQL Server) — happy path, replay, conflict, missing key, validation skew, query filters, RBAC negative cases.
7. `npx @redocly/cli lint ../../specs/audit.openapi.json` + `npm run docs:build`.

## Open items to confirm before / during Phase 4

(From IMPLEMENTATION_PLAN §"Open items"):
1. Reads — stick with `AdminOnly` or add an `AuditReader` policy now?
2. `DiffJson` size cap — enforce 64 KB in the validator?
3. Store `X-Source-Service` literal-lowercased (default) or normalize further?
