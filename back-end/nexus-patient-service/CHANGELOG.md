# Changelog

All notable changes to `nexus-patient-service` are documented here.

## [Unreleased]

### Phase 9 — Packaging + compose + cross-service smoke

- `nexus-patient-service` added to `back-end/docker-compose.yml` (port 9000) — depends on SQL Server (healthy) + Identity + Audit + Branches + OTel collector. Connection string targets `NexusPatients`; audit publisher and Branches client wired via env. Purge worker disabled by default.
- Live-stack smoke `Nexus.IntegrationTests.Patients.PatientsSmokeTests` covers `/health/live`, all 9 Swagger operationIds, 401 without bearer.
- Headline `EndToEndFlowTests.Full_flow_branch_patient_doctor_document_audit` creates → activates → archives a patient and asserts the audit trail.

### Phase 8 — 30-day archive purge + audit PII redaction

- **`PurgeOptions`** bound from `Purge:*` (Enabled=false default, RetentionDays=30, IntervalMinutes=60, BatchSize=50, RedactionPlaceholder=`[REDACTED]`).
- **`IAuditRedactorClient` / `AuditRedactorClient`** — typed `HttpClient` that POSTs `/api/v1/audit/redact` on the audit service. Sets `Idempotency-Key` (UUIDv7) + `X-Source-Service=nexus-patient`. Fail-tolerant: non-2xx responses return `0` and the purge continues.
- **`PatientPurgeWorker`** (`BackgroundService`, disabled by default):
  - Wakes every `IntervalMinutes`; opens a fresh DI scope per cycle.
  - Selects up to `BatchSize` patients where `Status=Archived AND ArchivedAtUtc < now - RetentionDays` (oldest first).
  - Per patient: best-effort PII scrub via `IAuditRedactorClient` → hard-delete patient + branch links inside a single EF transaction → publish `Patient.Purged` audit event through the existing Phase 3 outbox publisher.
  - On any per-patient failure (including audit-side outages) the worker logs and continues to the next row; redactor failures do NOT block the hard-delete.
  - Emits `PatientMetrics.PatientsPurged` once per successful purge.
- Wired in `Program.cs`: `PurgeOptions`, typed `AuditRedactorClient` HttpClient, `AddHostedService<PatientPurgeWorker>`.
- Tests: 5 new `PatientPurgeWorkerTests` (rows past retention purged, no-op when none due, proceeds when redactor throws, BatchSize respected, branch links removed alongside) — 28/28 unit tests pass.

### Phase 5 — Patients full feature surface

- **Schema**: Patient entity extended with spec-mandated fields (`FirstName`, `LastName`, address block `AddressLine1/2`, `City`, `State`, `PostalCode`, `Country` ISO alpha-2, and `PrimaryBranchId` GUID). `FullName` retained as a derived `FirstName + " " + LastName` column for the existing free-text search index. Migration `AddPatientCoreFields` applied to `NexusPatients`.
- **BranchesClient** (canonical — Doctors will copy verbatim in Phase 6):
  - `IBranchesClient` + `BranchesClient` typed HttpClient hitting `nexus-branch-service` `/api/v1/branches/{id}`.
  - `BranchesClientOptions` bound from `Branches:*` (BaseUrl, AttemptTimeoutSeconds=5, TotalRequestTimeoutSeconds=15, MaxRetryAttempts=3).
  - `BranchesClientRegistration.Add` wires Polly via `AddStandardResilienceHandler`: jittered exponential retry, 30 % failure-ratio circuit breaker over 10 s, 30 s break. Forwards caller bearer via `IHttpContextAccessor`.
  - Translates upstream failures to `DomainException(DEPENDENCY_UNAVAILABLE, 503)`. Readiness probe added against the Branches `/health/live` (Degraded — not Unhealthy).
- **PublicCodeGenerator** mints `PAT-YYYY-BRN-NNNNNN` codes from the Phase 1 `PATIENT_CODE_SEQUENCE` table inside the business transaction.
- **PatientsService** implements all 9 operations:
  - **listPatients** — paged + `status` + `branchId` + `q` free-text filter.
  - **createPatient** — validates the `primaryBranchId` against the live Branches service (422 `BRANCH_UNKNOWN` if missing / inactive), runs duplicate detection on `(phone, DOB)` and `(email, DOB)` (`409 DUPLICATE_IDENTITY`), mints the public code, emits `Patient.Created` audit.
  - **getPatientById** — emits `ETag`; 404 on miss.
  - **patchPatient** — honours `If-Match` (`409 ETAG_MISMATCH`), validates new `primaryBranchId` if changed, emits `Patient.Updated` with before/after diff.
  - **activatePatient** — `Draft → Active` only; rejects `Archived → Active` with `409 LIFECYCLE_INVALID_TRANSITION`; auto-creates a primary `PatientBranchLink` for the patient's `PrimaryBranchId` when missing.
  - **archivePatient** — terminal `→ Archived`, stamps `ArchivedAtUtc` for the Phase 8 purge worker.
  - **listPatientBranches** — only live links.
  - **linkPatientToBranch** — validates branch via `BranchesClient`, refuses duplicate live links with `409 ALREADY_LINKED`, demotes prior primary when `isPrimary=true` is set.
  - **unlinkPatientFromBranch** — idempotent; blocks unlinking the primary of an `Active` patient with `409 PRIMARY_BRANCH_REQUIRED`.
- **Endpoints** mapped via `PatientsEndpoints.MapPatients()` under `/api/v1/patients` with `AdminOnly`, `IdempotencyKeyFilter` on writes, `ValidationFilter<T>` on bodies.
- **Validation**: `PatientCreateRequestValidator` enforces spec bounds (gender enum, ISO date, ISO alpha-2 country, address bounds); `PatientPatchRequestValidator` requires at-least-one-field.
- **Metrics**: `PatientMetrics.PatientsCreated`, `StateTransitions{from,to}`, `BranchesLinked` incremented at the right places.
- Swagger UI + ReDoc mounted at `/swagger` and `/redoc`.
- Tests: **23 unit tests pass** — repo (5) + outbox (4) + service (14 covering create + branch-unknown + duplicate-identity, get/patch-404/etag, activate-auto-link + archived→active rejection, archive, link+primary-promotion + already-linked + branch-unknown, unlink-blocks-primary + idempotent, list-by-status).

### Phase 3 — Audit publisher + PENDING_AUDIT outbox + reconciler

- Same publisher chain as Branches: `AuditClientOptions`, `AuditEventDto`, `IAuditPublisher`, `NullAuditPublisher`, `HttpAuditPublisher` (typed HttpClient + `AddStandardResilienceHandler`), `OutboxAuditPublisher`, `IPendingAuditOutbox` / `PendingAuditOutbox`, `PendingAuditWorker` BackgroundService.
- Source service: `nexus-patient` (sent as `X-Source-Service` on every audit POST).
- The `PendingAuditEntry` table already shipped in Phase 1's `PatientDbContext` migration — no new migration needed.
- Readiness probe added for the audit URL (reports `Degraded` not `Unhealthy`).

### Phase 2 — Cross-cutting infrastructure

- Same `Errors` / `Auth` / `Idempotency` / `Observability` / CORS / rate-limiting stack as Branches.
- `ErrorCodes` adds patient-specific codes: `PATIENT_VALIDATION`, `DUPLICATE_IDENTITY`, `LIFECYCLE_INVALID_TRANSITION`, `PRIMARY_BRANCH_REQUIRED`, `BRANCH_UNKNOWN`, `ALREADY_LINKED`, `ETAG_MISMATCH`, `DEPENDENCY_UNAVAILABLE` (for the Phase-5 BranchesClient).
- `PatientMetrics` custom counters: `nexus_patients_created_total`, `nexus_patient_state_transitions_total{from,to}`, `nexus_patient_branches_linked_total`, `nexus_patients_purged_total` (used by Phase 8 worker).
- Serilog service name `nexus-patient`, file path `logs/patient-.log`.
- Live boot smoke verified: `/health/live` 200, `/metrics` 200.

### Phase 1 — Persistence & domain model

- Renamed root namespace to `Nexus.Patients.Api` (plural) to avoid collision with the `Patient` entity.
- Entities: `Patient` (with `PatientStatus` enum `Draft / Active / Archived`, RowVersion, ETag), `PatientBranchLink` (composite PK + soft-delete via `UnlinkedAtUtc`), `PatientCodeSequence` (per-branch/per-year counter for `PAT-YYYY-BRN-NNNNNN`), `PendingAuditEntry` (outbox row — reconciler in Phase 3).
- `PatientDbContext` (database `NexusPatients`) with: unique `PublicCode`; filtered-unique `(Phone, DateOfBirth)` and `(Email, DateOfBirth)` for duplicate detection; `(Status, ArchivedAtUtc)` index for the purge job; primary-branch lookup index on `(PatientId, IsPrimary, UnlinkedAtUtc)`; outbox `UX_PendingAudit_IdempotencyKey` + `IX_PendingAudit_NextRetryUtc`.
- `AuditedTimestampInterceptor` stamps `CreatedAtUtc` / `UpdatedAtUtc` automatically.
- Initial migration `InitialPatientSchema`.
- Repositories: `IPatientRepository` (Add / GetById / find-by-phone-DOB / find-by-email-DOB / Query / SaveChanges), `IPatientCodeSequenceRepository.NextAsync` (atomic per-branch/per-year increment, case-normalised).
- Tests: 7 — patient round-trip, dedup lookups, paging+status filter, code-sequence increment + per-branch/year scoping + case normalisation.

### Phase 0 — Scaffolding

- Solution skeleton: `Nexus.Patient.sln`, `src/Nexus.Patient.Api`, `tests/Nexus.Patient.Api.Tests`.
- Minimal Kestrel host on port `9000` with the Spectre.Console "Nexus Patients" banner and startup summary panels.
- Baseline configuration (`appsettings.json` + Development + Production overlays).
- Top-level service artefacts: `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE`, `.gitignore`, `.dockerignore`, `.editorconfig`, `Dockerfile`, `dotnet-tools.json`.
