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

---

# Nexus HA — Branches + Patients + Doctors: Phased Implementation Strategy

## Context

Audit and Identity now ship; the next slice of the Nexus HA platform is the three domain services that together fulfil the core BRD lifecycle requirements:

- `nexus-branch-service` — master data for hospital branches; gating dependency for the other two.
- `nexus-patient-service` — patient registry with `Draft → Active → Archived` lifecycle and a 30-day purge.
- `nexus-doctor-service` — doctor registry with `Pending → Verified → Approved → Active → Deactivated` lifecycle, verification documents (multipart upload), and re-activation.

All three are independent .NET 10 / ASP.NET Core services. Per the LLD §16 independence rule, infrastructure is **copy-pasted** from the existing audit / identity templates — no project references, no shared NuGet, no shared source.

**Locked design choices (Q1–Q4):**
- **Sequencing:** Branches first (gating dependency), then Patients & Doctors as two parallel tracks once Phase 4 lands.
- **Scope:** full feature set per OpenAPI spec — including doctor multipart upload + `IDocumentStorage` + SHA-256 dedup, patient 30-day purge + PII redaction in old audit entries.
- **Tests:** extend the existing `back-end/tests/Nexus.Integration.Tests/` project (one `StackFixture`, new `Branches/`, `Patients/`, `Doctors/` folders alongside `Identity/`, `Audit/`, `CrossService/`).
- **Granularity:** 10 phases (0–9). Each phase ends green (build + tests) and shippable.

## Service targets (single-line summary)

| Service | Port | DB | Ops | Notable mechanisms |
|---|---|---|---|---|
| Branches | 11000 | `NexusBranches` | 5 | filtered-unique `Code` index, RowVersion/ETag, soft-delete, new code `BRANCH_CODE_DUPLICATE` |
| Patients | 9000 | `NexusPatients` | 9 | `Draft→Active→Archived`, Phone+DOB / Email+DOB dedup, primary-branch invariant, `PAT-YYYY-BRN-NNNNNN` codes, 30-day purge |
| Doctors | 10000 | `NexusDoctors` | 16 | `Pending→Verified→Approved→Active→Deactivated` (re-activation), license dedup, `DOC-YYYY-BRN-NNNNNN` codes, multipart docs + `IDocumentStorage` + SHA-256 dedup, document-locked invariants |

## Cross-service runtime dependencies

- All three → Identity (JWKS at boot, periodic refresh) — copy `nexus-audit-service/.../Infrastructure/Auth/*`.
- All three → Audit service via a resilient `HttpAuditPublisher` (copy from `nexus-identity-service/.../Infrastructure/Audit/*`) backed by a **local `PENDING_AUDIT` outbox table + `PendingAuditWorker` reconciler** (per LLD §14 — Identity does not have the outbox today; the three new services do).
- Patients + Doctors → Branches via a `BranchesClient` typed `HttpClient` (5 s timeout, 3 retries, Polly circuit-breaker at ≥30 % failure ratio over 10 s, 30 s break). Built once in Phase 5 and copy-pasted into Doctors at Phase 6.

## Reusable templates (copy-paste, do NOT project-reference)

| Concern | Source path | Notes |
|---|---|---|
| RFC 9457 errors | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Errors/*` | + new code `BRANCH_CODE_DUPLICATE` in Branches |
| JWT + JWKS + AdminOnly | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Auth/*` | unchanged |
| Serilog + OTel + Prometheus + Health | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Observability/*` | service name + custom metrics differ per service |
| Idempotency filter + source-service resolver | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Idempotency/*` | only the `Idempotency-Key` filter (no replay state machine — that lives in the audit service) |
| Pagination | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Pagination/*` | unchanged |
| Persistence bootstrap | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Persistence/PersistenceRegistration.cs` | unchanged |
| Audit publisher (HttpClient + resilience + fail-open) | `back-end/nexus-identity-service/src/Nexus.Identity.Api/Infrastructure/Audit/*` | augment with `PENDING_AUDIT` outbox + reconciler |
| Dockerfile | `back-end/nexus-audit-service/Dockerfile` | swap port + dll name per service |
| Startup banner + summary | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Infrastructure/Startup/*` | unchanged |
| csproj baseline | `back-end/nexus-audit-service/src/Nexus.Audit.Api/Nexus.Audit.Api.csproj` | drop Audit-specific packages |

---

## Phase rollout

### Phase 0 — Triple scaffolding
**Goal:** three empty services that build, boot, and show banners on ports 9000/10000/11000.

- Create `Nexus.{Branch,Patient,Doctor}.sln`, `src/Nexus.{X}.Api/Nexus.{X}.Api.csproj` (net10.0 Web SDK), `tests/Nexus.{X}.Api.Tests/*.csproj`.
- Copy `Dockerfile`, `dotnet-tools.json`, `.editorconfig`, `.gitignore`, `.dockerignore`, `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE` shapes from audit.
- Minimal `Program.cs` + Spectre banners "Nexus Branches" / "Nexus Patients" / "Nexus Doctors".
- `appsettings.json` / `.Development.json` / `.Production.json` per service.

**Verify:** `dotnet build` × 3 → 0 warnings, 0 errors; `dotnet run` shows banner; `GET /` returns 200.

**Tests:** extend `Nexus.Integration.Tests` with empty `Branches/`, `Patients/`, `Doctors/` folders so the project compiles ahead of test classes.

---

### Phase 1 — Persistence & domain models (all three)
**Goal:** EF Core code-first persistence ready; first migrations applied to each DB.

Per service:
- `Features/{X}/Models/*` — entity records, DTOs, paged envelopes.
- `Infrastructure/Persistence/{X}DbContext` + initial migration.
- `Features/{X}/Repository/I{X}Repository` + EF impl with `AsNoTracking()` reads, append + update + delete paths as the spec requires.
- Patients: `PATIENT_CODE_SEQUENCE` table (BranchCode, Year, LastValue PK).
- Doctors: `DOCTOR_CODE_SEQUENCE` table, `DOCTOR_DOCUMENT` table.
- Both Patients and Doctors: `PENDING_AUDIT` table (the reconciler comes in Phase 3).
- All three: `RowVersionInterceptor` (mirrored from identity) so PATCH endpoints can use ETag concurrency.

**Verify:** `dotnet ef database update` runs cleanly against each DB on the live SQL container; repository round-trip tests against EF InMemory.

**Tests:** `Nexus.Integration.Tests/{Service}/PersistenceSmokeTests.cs` — each service responds to a trivial read; SQL tables exist after first boot.

---

### Phase 2 — Cross-cutting (errors, validation, auth, idempotency, observability, health)
**Goal:** identical infrastructure stack across the trio.

- Copy `Infrastructure/Errors/*` → adjust `ErrorCodes` per service (add `BRANCH_CODE_DUPLICATE`, `DUPLICATE_IDENTITY`, `LIFECYCLE_INVALID_TRANSITION`, `PRIMARY_BRANCH_REQUIRED`, `BRANCH_UNKNOWN`, `ALREADY_LINKED`, `ETAG_MISMATCH`, `DUPLICATE_LICENSE`, `LIFECYCLE_BLOCKED`, `DOCUMENT_LOCKED`, `DOCUMENT_TOO_LARGE`, `DOCUMENT_UNSUPPORTED_TYPE`, `DOCUMENT_DUPLICATE` as relevant).
- Copy `Infrastructure/Auth/*` → wire `AdminOnly` policy + JWKS resolver pointing at `http://nexus-identity-service:8000/.well-known/jwks.json`.
- Copy `Infrastructure/Idempotency/IdempotencyKeyFilter.cs` + `SourceServiceResolver.cs` (used on POSTs that take an `Idempotency-Key`).
- Copy `Infrastructure/Observability/*` → service-specific `MeterName` constants (`Nexus.Branches`, `Nexus.Patients`, `Nexus.Doctors`).
- Copy `Infrastructure/Pagination/*`.
- Copy `Infrastructure/Startup/*` and customise banner text.
- `HealthCheckRegistration` for each: SQL Server + JWKS URI.
- FluentValidation registered; `ValidationFilter<T>` available.

**Verify:** Each `/health/ready` returns 200; unauthenticated `GET /api/v1/...` returns 401 in problem+json; oversized payload returns 422 problem+json with `code` + `traceId`.

**Tests:** `{Service}/CrossCuttingTests.cs` — 401/403/422 shape, problem+json content type, health endpoints.

---

### Phase 3 — Audit publisher + PENDING_AUDIT outbox + reconciler (all three)
**Goal:** fail-open + durable buffer pattern per LLD §14, applied identically to Branches, Patients, Doctors.

Per service:
- Copy `nexus-identity-service/.../Infrastructure/Audit/HttpAuditPublisher.cs` and `AuditRegistration.cs` (typed HttpClient + `AddStandardResilienceHandler` + bearer-forwarding + fail-open).
- Add `Infrastructure/Audit/Outbox/PendingAuditEntry.cs` (FK to nothing — fully self-contained payload + idempotency key + attempt count + next-retry-utc).
- Add `Infrastructure/Audit/Outbox/PendingAuditWorker.cs` — `BackgroundService` that selects due rows (`UPDLOCK, READPAST` to avoid double-publish under multi-instance), POSTs to audit, deletes on 2xx, exponential-backoff on failure.
- Application-layer pattern: business handler enqueues an audit DTO in the same EF transaction as the business write. The publisher attempts a synchronous POST after `SaveChangesAsync`; on failure it falls back to the outbox row already persisted.

**Verify:** Stop the audit container mid-test → outbox row appears; restart audit → reconciler drains it; row removed from `PENDING_AUDIT`; entry queryable via the audit service.

**Tests:** `{Service}/AuditOutboxTests.cs` — fail-open returns success, outbox flush after audit comes back, idempotency-key replay safe.

---

### Phase 4 — Branches full feature surface (GATE)
**Goal:** all 5 Branches operations live, behaving per `specs/branches.openapi.json`.

- `Features/Branches/Service/BranchesService` + `Features/Branches/Controller/BranchesEndpoints`:
  - `GET /api/v1/branches` (`listBranches`) with paging + `isActive` filter.
  - `POST /api/v1/branches` (`createBranch`) — `Idempotency-Key` required; filtered-unique on `Code` → `409 BRANCH_CODE_DUPLICATE` on dup.
  - `GET /api/v1/branches/{id}` (`getBranchById`) — emits `ETag` header.
  - `PATCH /api/v1/branches/{id}` (`patchBranch`) — `If-Match` required → `409 ETAG_MISMATCH` on stale.
  - `DELETE /api/v1/branches/{id}` (`deactivateBranch`) — soft-delete (IsActive=false); code remains reusable once row is inactive thanks to filtered index.
- `BranchCreated`, `BranchUpdated`, `BranchDeactivated` audit events flow through Phase 3 outbox.

**Verify:** Swagger exposes all 5 operationIds; CRUD round-trips against live stack; concurrent PATCH with stale ETag returns 409.

**Tests:** `Nexus.Integration.Tests/Branches/{List,Create,Get,Patch,Deactivate,Concurrency,CodeUniqueness}Tests.cs`.

**After this phase Branches is frozen.** Patients & Doctors tracks can now run in parallel.

---

### Phase 5 — Patients full surface (CRUD + lifecycle + duplicates + branch linking)
**Goal:** all 9 Patient operations live.

- `Infrastructure/Branches/BranchesClient.cs` — typed `HttpClient` (Polly: 5 s per attempt, 3 retries with jitter on idempotent GET, circuit-breaker ≥30 % failure ratio over 10 s, 30 s break). Health probe wired into `/health/ready`. **This client is the master copy reused in Phase 6 for Doctors.**
- `Features/Patients/Service/*`:
  - `PublicCodeGenerator` — atomic `INSERT/UPDATE` against `PATIENT_CODE_SEQUENCE` inside the business transaction; gaps on rollback documented as acceptable.
  - State machine `Draft → Active → Archived` enforced in the domain aggregate (not the controller).
  - Duplicate detection on `(Phone + DOB)` and `(Email + DOB)` (toggleable per config) → `409 DUPLICATE_IDENTITY`.
  - Primary-branch invariant: every `Active` patient has exactly one primary active link.
- `Features/Patients/Controller/PatientsEndpoints` — all 9 OpenAPI ops, `AdminOnly`, `Idempotency-Key` on writes, `If-Match` on PATCH.
- Audit events: `Patient.Created`, `Patient.Updated`, `Patient.StateChanged`, `PatientBranch.Linked`, `PatientBranch.Unlinked`.
- Custom metric: `nexus_patient_state_transitions_total{from,to}`.

**Verify:** Full OpenAPI surface exercised; Branches container stopped → `linkPatientToBranch` returns `503 DEPENDENCY_UNAVAILABLE` after circuit opens.

**Tests:** `Patients/{Crud,Lifecycle,Duplicates,BranchLinking,PrimaryBranchInvariant,PublicCode,BranchesUnavailable}Tests.cs`.

---

### Phase 6 — Doctors core (CRUD + lifecycle + license dedup + branch linking)
**Goal:** 11 of 16 Doctor operations live (everything **except** the documents subsystem).

- Copy `BranchesClient` from Phase 5 verbatim into the doctor service.
- `PublicCodeGenerator` against `DOCTOR_CODE_SEQUENCE`.
- State machine `Pending → Verified → Approved → Active → Deactivated` with the **re-activation edge** `Deactivated → Active` (rejected for Patients but permitted for Doctors per spec).
- License number filtered-unique index (`WHERE Status <> 'Deactivated'`) → `409 DUPLICATE_LICENSE`; license immutable once the row reaches `Verified`.
- `Features/Doctors/Controller/DoctorsEndpoints` — CRUD + verify/approve/activate/deactivate + branch link/unlink (11 ops). Documents come in Phase 7.
- Audit events: `Doctor.Created`, `Doctor.Updated`, `Doctor.StateChanged`, `DoctorBranch.Linked`, `DoctorBranch.Unlinked`.
- Custom metric: `nexus_doctor_state_transitions_total{from,to}`.

**Verify:** Lifecycle transitions tested in both directions; license immutability post-Verified enforced; re-activation works.

**Tests:** `Doctors/{Crud,Lifecycle,LicenseImmutability,Reactivation,BranchLinking}Tests.cs`.

---

### Phase 7 — Doctor documents (multipart + storage + dedup + review)
**Goal:** the remaining 5 ops (`listDoctorDocuments`, `uploadDoctorDocument`, `getDoctorDocument`, `reviewDoctorDocument`, `deleteDoctorDocument`).

- `Infrastructure/Documents/IDocumentStorage.cs` — async stream-in/stream-out + delete.
- `Infrastructure/Documents/LocalFileSystemDocumentStorage.cs` — writes to `/var/data/nexus/doctor-docs/<doctorId>/<sha-prefix>/<storageKey>`. Streams from `MultipartReader` directly (no `IFormFile` binding, no full-buffer).
- `Features/Doctors/Documents/Service/DoctorDocumentsService`:
  - SHA-256 computed in-stream as bytes flow to disk (single pass).
  - Dedup via unique index on `(DoctorId, Sha256)` → on `DbUpdateException` return the existing row (race-safe).
  - 10 MB size cap → `413 DOCUMENT_TOO_LARGE`.
  - Allowed content types: `application/pdf`, `image/png`, `image/jpeg` → `415 DOCUMENT_UNSUPPORTED_TYPE`.
  - Review workflow: `Uploaded → Verified | Rejected` (note required on Rejected).
  - **Document-locked invariant:** once the doctor reaches `Verified`, further uploads + deletes return `409 DOCUMENT_LOCKED`; only review remains legal.
  - Soft-delete documents via `DeletedAtUtc`; review on a soft-deleted doc → `404 NOT_FOUND`.
- Document-gated verification: `verifyDoctor` checks all `IsRequired=true` documents are `Verified` → `409 LIFECYCLE_BLOCKED` otherwise.
- Audit events: `DoctorDocument.Uploaded`, `DoctorDocument.Reviewed`, `DoctorDocument.Deleted`.
- Custom metric: `nexus_doctor_documents_uploaded_total{type,result}`.

**Risks called out:**
1. SHA-256 dedup race — DB unique index is the source of truth, not the app-level check.
2. Multipart streaming under Kestrel — must use `MultipartReader` directly; do **not** bind to `IFormFile`.
3. Container volume permissions — `nexus` user must own `/var/data/nexus/doctor-docs/` in the image.

**Tests:** `Doctors/Documents/{Upload,Dedup,Review,VerifiedLock,SizeLimit,ContentType,ConcurrentDedup,SoftDelete}Tests.cs`.

---

### Phase 8 — Patient archive → 30-day purge + audit PII redaction
**Goal:** daily UTC purge job + PII redaction in old audit entries (LLD §9.6 + §15.2).

- `Infrastructure/Purge/PatientPurgeWorker.cs` — `BackgroundService` triggered daily at UTC midnight (configurable cron / interval). Finds rows where `Status=Archived AND ArchivedAtUtc < now - 30 days`. Hard-deletes the patient + its branch links inside one transaction. Emits a single `Patient.Purged` audit event via the same outbox pipeline.
- `Infrastructure/Audit/PiiRedactor.cs` — invoked from the purge transaction: enumerates prior audit entries for the purged patient via the audit service's query API, then patches `BeforeJson`/`AfterJson` to redact PII (hash phone/email, null DOB). The audit service does not expose mutation today; **this requires a small additive endpoint on audit (TBD or a direct DB write — confirm in implementation kickoff)**.
- Idempotent: re-running on the same day with no archived rows older than 30 days is a no-op.

**Verify:** Fast-forward clock fixture archives a patient 31 days ago, runs the worker once, observes hard-delete + a `Purged` audit event + redacted prior entries.

**Tests:** `Patients/{PurgeWorker,PiiRedaction}Tests.cs`.

**Open question:** PII redaction needs a writable hook into the audit service. Either (a) add a single admin-only `PATCH /api/v1/audit/{id}/redact` endpoint to the audit service, or (b) the purge worker writes directly to `NexusAudit.AuditEntries` via a second connection string. Lean on (a) for architectural cleanliness.

---

### Phase 9 — Packaging, compose, cross-service smoke
**Goal:** three new services in the live stack; integration suite covers them end-to-end.

- `back-end/docker-compose.yml` adds three services:
  - `nexus-branch-service` → 11000:11000, `ConnectionStrings__Default` → `NexusBranches` on `sqlserver`, Auth + OTel + Prometheus env, `depends_on: sqlserver healthy + identity + otel-collector`.
  - `nexus-patient-service` → 9000:9000, same + `Branches__BaseUrl=http://nexus-branch-service:11000`, `depends_on: branches started`.
  - `nexus-doctor-service` → 10000:10000, same + named volume `doctor-docs:/var/data/nexus/doctor-docs/` for document storage.
- `back-end/.env.example` + `.env` updated with any new defaults.
- Each service `Dockerfile` smoke-built (mirrors audit's pattern).
- READMEs + `CHANGELOG.md` finalised per service.
- Redocly `docs/redocly.yaml` + `docs/package.json` per service rooted at the matching OpenAPI spec.
- `Nexus.IntegrationTests/StackFixture` extended to wait on five `/health/ready` endpoints (identity + audit + branches + patients + doctors).
- `Nexus.IntegrationTests/CrossService/EndToEndFlowTests.cs` — **headline end-to-end proof**: create branch → create patient linked to it → create doctor linked to it → upload doctor document → verify doctor → archive patient → assert audit trail contains every step.
- Per-service test folders consolidated and run as part of `dotnet test back-end/tests/Nexus.IntegrationTests.sln`.

**Verify:** `docker compose up -d` brings up the full eight-service stack (5 apps + SQL + OTel collector + observability); `dotnet test` against the live stack passes for every new test class plus the existing Identity/Audit/CrossService suites.

---

## Critical files / paths (master index)

**Per new service** (`back-end/nexus-{branch,patient,doctor}-service/`):
- `Nexus.{Branch,Patient,Doctor}.sln`
- `src/Nexus.{X}.Api/Program.cs`
- `src/Nexus.{X}.Api/Features/{X}/{Models,Validators,Repository,Service,Controller}/*`
- `src/Nexus.{X}.Api/Infrastructure/{Errors,Auth,Persistence,Observability,Pagination,Idempotency,Startup,Audit}/*`
- `src/Nexus.{X}.Api/Configuration/*Options.cs`
- `src/Nexus.{X}.Api/Infrastructure/Persistence/Migrations/*` (EF generated)
- `src/Nexus.{X}.Api/appsettings*.json`
- `tests/Nexus.{X}.Api.Tests/**` (per-service unit/integration shims)
- `Dockerfile`, `.dockerignore`, `.editorconfig`, `.gitignore`, `dotnet-tools.json`
- `docs/redocly.yaml`, `docs/package.json`

**Patient-specific:**
- `src/Nexus.Patient.Api/Infrastructure/Branches/BranchesClient.cs` (Polly handler)
- `src/Nexus.Patient.Api/Infrastructure/Purge/PatientPurgeWorker.cs`
- `src/Nexus.Patient.Api/Infrastructure/Audit/PiiRedactor.cs`

**Doctor-specific:**
- `src/Nexus.Doctor.Api/Infrastructure/Branches/BranchesClient.cs` (copy of Patients')
- `src/Nexus.Doctor.Api/Infrastructure/Documents/IDocumentStorage.cs`
- `src/Nexus.Doctor.Api/Infrastructure/Documents/LocalFileSystemDocumentStorage.cs`
- `src/Nexus.Doctor.Api/Features/Doctors/Documents/**`

**Stack-wide:**
- `back-end/docker-compose.yml` (extended)
- `back-end/.env`, `.env.example` (extended)
- `back-end/tests/Nexus.Integration.Tests/{Branches,Patients,Doctors,CrossService}/**`
- `back-end/tests/Nexus.Integration.Tests/Infrastructure/StackFixture.cs` (extended to wait on 5 services)

## End-to-end verification (after Phase 9)

1. `docker compose up -d` from `back-end/`.
2. Wait until all five services report `/health/ready → 200`.
3. `dotnet test back-end/tests/Nexus.IntegrationTests.sln` — full Identity + Audit + Branches + Patients + Doctors + CrossService suite green.
4. Spec sign-off:
   - `npx @redocly/cli lint specs/branches.openapi.json` (clean)
   - `npx @redocly/cli lint specs/patients.openapi.json` (clean)
   - `npx @redocly/cli lint specs/doctors.openapi.json` (clean)
   - Each service's `/swagger/v1/swagger.json` exposes the operationIds declared in its spec.
5. Manual contract walkthroughs in each service's `docs/CONTRACT_TESTING.md` pass against the live stack.
6. Observability: Grafana dashboards for Patients (state-transitions counter) and Doctors (state-transitions + documents-uploaded counters) populate.

## Phase dependencies + risk register

```
0 → 1 → 2 → 3 → [4 GATE] → { 5, 6 } → 7 → 8 → 9
                                      ↑      ↑
                            6 unlocks 7      8 needs audit-redaction hook
```

| # | Risk | Phase | Mitigation |
|---|---|---|---|
| 1 | SHA-256 dedup race on doctor documents | 7 | DB unique index `(DoctorId, Sha256)` is source of truth; catch `DbUpdateException` and return the existing row |
| 2 | Primary-branch invariant under concurrent link/unlink | 5 | Enforce inside the domain aggregate using RowVersion; surface `ETAG_MISMATCH` not silent overwrite |
| 3 | Outbox reconciler double-publish under multiple instances | 3 | `UPDLOCK, READPAST` (or skip-locked semantics) when selecting due rows |
| 4 | Multipart streaming memory blow-up | 7 | Use `MultipartReader` directly; do not bind to `IFormFile`; `RequestSizeLimit(10_485_760)` |
| 5 | Public code sequence gaps on rollback | 5, 6 | Document gaps as acceptable; sequence row updated inside the same transaction as the entity insert |
| 6 | Audit PII redaction needs a writable hook | 8 | Add `PATCH /api/v1/audit/{id}/redact` to the audit service (admin-only) — confirm at implementation kickoff |
| 7 | `BranchesClient` copy drift between Patients and Doctors | 6 | Treat the Phase-5 version as canonical; Phase-6 copy is verbatim; track any change as a paired commit |
