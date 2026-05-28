# Changelog

All notable changes to `nexus-doctor-service` are documented here.

## [Unreleased]

### Phase 9 — Packaging + compose + cross-service smoke

- `nexus-doctor-service` added to `back-end/docker-compose.yml` (port 10000) — depends on SQL Server (healthy) + Identity + Audit + Branches + OTel collector. Named volume `doctor_docs` mounts at `/var/data/nexus/doctor-docs` (owned by the `nexus` user inside the image) so uploaded credential documents survive container restarts.
- Live-stack smoke `Nexus.IntegrationTests.Doctors.DoctorsSmokeTests` covers `/health/live` and asserts all 16 Swagger operationIds (CRUD + 4 lifecycle + 3 branch-link + 5 document ops) are exposed.
- Headline `EndToEndFlowTests.Full_flow_branch_patient_doctor_document_audit` creates a doctor → multipart-uploads a `MedicalLicense` PDF → reviews it Verified → verifies the doctor → asserts every audit event lands in the audit trail.

### Phase 7 — Doctor documents (multipart upload + storage + SHA-256 dedup + review)

- `DocumentsOptions` bound from `Documents:*` — `RootPath` (default `/var/data/nexus/doctor-docs/`), `MaxFileBytes` (10 MB), `AllowedContentTypes` (`application/pdf`, `image/png`, `image/jpeg`).
- `IDocumentStorage` + `LocalFileSystemDocumentStorage` — streams the upload into the on-disk path **and** computes SHA-256 in the same pass via `CryptoStream` over the destination `FileStream`. Returns `StoredDocument(Sha256Hex, SizeBytes)`. No full-buffer.
- `IDoctorDocumentRepository` + EF impl with `ListAsync`, `GetAsync`, `FindActiveBySha256Async` (filtered on `DeletedAtUtc IS NULL`), `AddAsync`.
- `DoctorDocumentsService` (5 ops):
  - **listDoctorDocuments** — live docs only.
  - **uploadDoctorDocument** — content-type check (`415 DOCUMENT_UNSUPPORTED_TYPE`), size cap (`413 DOCUMENT_TOO_LARGE` — enforced both at the wrapping `LimitStream` and post-store), SHA-256 dedup via DB filtered-unique `(DoctorId, Sha256)`. Race-safe: on `DbUpdateException(UNIQUE)` rolls back the file write and returns the concurrent row. `MedicalLicense` is auto-marked `IsRequired=true`.
  - **getDoctorDocument** — 404 on miss or soft-deleted.
  - **reviewDoctorDocument** — `Uploaded → Verified | Rejected` only; `Rejected` requires `rejectionReason`; double-review returns `409 LIFECYCLE_INVALID_TRANSITION`.
  - **deleteDoctorDocument** — soft-delete (stamps `DeletedAtUtc`), best-effort storage delete; `409 DOCUMENT_LOCKED` for verified docs.
- **Document-locked invariant**: uploads + deletes refused once the doctor is `Verified` / `Approved` / `Active` (`409 DOCUMENT_LOCKED`).
- **`verifyDoctor` gating**: `DoctorsService.VerifyAsync` now requires **at least one** `IsRequired=true` document with `Status=Verified` before allowing the `Pending → Verified` transition (`409 LIFECYCLE_BLOCKED` otherwise).
- **Multipart endpoint**: `DoctorDocumentsEndpoints.MapDoctorDocuments` uses `MultipartReader` + `MultipartRequestHelper` directly — no `IFormFile` binding, no full buffering. Streams the file part into the storage layer. Antiforgery disabled. Per-request idempotency-key on writes.
- **Audit events**: `DoctorDocument.Uploaded`, `DoctorDocument.Reviewed`, `DoctorDocument.Deleted` — all flow through the Phase 3 outbox publisher chain.
- **Metrics**: `DoctorMetrics.DocumentsUploaded{kind,result}`, `DoctorMetrics.DocumentsReviewed{decision}` incremented.
- Wired in `Program.cs`: `DocumentsOptions`, singleton `LocalFileSystemDocumentStorage`, scoped repo + service, `MapDoctorDocuments()`.
- Tests: **32 unit tests pass** — 11 new `DoctorDocumentsServiceTests` (upload + dedup + unsupported-type + size-limit + unknown-kind + document-locked on verified doctor + review verified/rejected/double-review + delete soft + delete blocks verified), plus 21 doctor-core (updated to seed a verified required doc before `VerifyAsync` to clear the new gating).

### Phase 6 — Doctors core (11 of 16 operations)

- **Schema**: `Doctor` extended with `FirstName`, `LastName`, `PrimaryBranchId`; `FullName` retained as derived column for search. Migration `AddDoctorCoreFields` applied to `NexusDoctors`.
- **BranchesClient**: copied verbatim from the Phase 5 Patients canonical version (typed `HttpClient` + `AddStandardResilienceHandler` — 5 s/attempt, 15 s total, 3 jittered retries, 30 % failure-ratio circuit breaker, 30 s break). Forwards caller bearer; translates upstream failures to `503 DEPENDENCY_UNAVAILABLE`. Readiness probe wired against Branches `/health/live` (Degraded).
- **PublicCodeGenerator** mints `DOC-YYYY-BRN-NNNNNN` from the Phase 1 `DOCTOR_CODE_SEQUENCE` table.
- **DoctorsService** implements 11 ops:
  - **listDoctors / getDoctorById / createDoctor / patchDoctor** — full CRUD with `If-Match`/ETag, `BranchesClient` validation on `primaryBranchId`, license-dedup via `FindActiveByLicenseAsync` (filtered-unique index `WHERE Status <> Deactivated`), `409 DUPLICATE_LICENSE` on collision, `409 ETAG_MISMATCH`.
  - **Lifecycle**: `verifyDoctor` (Pending → Verified), `approveDoctor` (Verified → Approved), `activateDoctor` (Approved → Active OR Deactivated → Active — re-activation edge per spec), `deactivateDoctor` (Active → Deactivated). Illegal jumps return `409 LIFECYCLE_INVALID_TRANSITION`. Activation auto-creates the primary `DoctorBranchLink` for the doctor's `PrimaryBranchId`.
  - **Branch links**: `listDoctorBranches`, `linkDoctorToBranch` (validates branch via `BranchesClient`, `409 ALREADY_LINKED`, primary promotion + demotion of existing primaries), `unlinkDoctorFromBranch` (idempotent; blocks unlinking the primary of an Active doctor with `409 PRIMARY_BRANCH_REQUIRED`).
- **Audit events**: `Doctor.Created`, `Doctor.Updated`, `Doctor.StateChanged` (per transition), `DoctorBranch.Linked`, `DoctorBranch.Unlinked` — all flow through the Phase 3 outbox publisher chain.
- **Endpoints** mapped via `DoctorsEndpoints.MapDoctors()` under `/api/v1/doctors` with `AdminOnly`, `IdempotencyKeyFilter` on writes, `ValidationFilter<T>` on bodies.
- **Validation**: `DoctorCreateRequestValidator`, `DoctorPatchRequestValidator`, `DoctorBranchLinkRequestValidator` per spec bounds.
- **Metrics**: `DoctorMetrics.DoctorsCreated`, `StateTransitions{from,to}`, `BranchesLinked`.
- Swagger UI + ReDoc mounted at `/swagger` and `/redoc`.
- **Tests: 20 unit tests pass** — repo (4) + code sequence (2) + service (14 covering create + branch-unknown + duplicate-license, patch + etag-mismatch, verify, approve-blocked-when-not-verified, full lifecycle pending→active→deactivated→reactivated, activate-auto-link, link-promotes-primary, link-already-linked, unlink-blocks-primary-of-active, unlink-idempotent, list-by-status).
- Phase 7 (documents subsystem — multipart upload + `IDocumentStorage` + SHA-256 dedup + review + document-locked invariants) is intentionally deferred to its own phase.

### Phase 3 — Audit publisher + PENDING_AUDIT outbox + reconciler

- Same publisher chain as Branches: `AuditClientOptions`, `AuditEventDto`, `IAuditPublisher`, `NullAuditPublisher`, `HttpAuditPublisher` (typed HttpClient + `AddStandardResilienceHandler`), `OutboxAuditPublisher`, `IPendingAuditOutbox` / `PendingAuditOutbox`, `PendingAuditWorker` BackgroundService.
- Source service: `nexus-doctor` (sent as `X-Source-Service` on every audit POST).
- The `PendingAuditEntry` table already shipped in Phase 1's `DoctorDbContext` migration — no new migration needed.
- Readiness probe added for the audit URL.

### Phase 2 — Cross-cutting infrastructure

- Same `Errors` / `Auth` / `Idempotency` / `Observability` / CORS / rate-limiting stack as Branches.
- `ErrorCodes` adds doctor-specific codes: `DOCTOR_VALIDATION`, `DUPLICATE_LICENSE`, `LIFECYCLE_INVALID_TRANSITION`, `LIFECYCLE_BLOCKED`, `PRIMARY_BRANCH_REQUIRED`, `BRANCH_UNKNOWN`, `ALREADY_LINKED`, `ETAG_MISMATCH`, `DOCUMENT_LOCKED`, `DOCUMENT_TOO_LARGE`, `DOCUMENT_UNSUPPORTED_TYPE`, `DOCUMENT_DUPLICATE`, `DEPENDENCY_UNAVAILABLE`.
- `DoctorMetrics` custom counters: `nexus_doctors_created_total`, `nexus_doctor_state_transitions_total{from,to}`, `nexus_doctor_branches_linked_total`, `nexus_doctor_documents_uploaded_total` (Phase 7), `nexus_doctor_documents_reviewed_total` (Phase 7).
- Serilog service name `nexus-doctor`, file path `logs/doctor-.log`.
- Live boot smoke verified: `/health/live` 200, `/metrics` 200.

### Phase 1 — Persistence & domain model

- Renamed root namespace to `Nexus.Doctors.Api` (plural) to avoid collision with the `Doctor` entity.
- Entities: `Doctor` (with `DoctorStatus` enum `Pending / Verified / Approved / Active / Deactivated`, RowVersion), `DoctorDocument` (with `DocumentStatus` enum + `DocumentKinds` constants), `DoctorBranchLink` (soft-deletable), `DoctorCodeSequence` (per-branch/per-year counter for `DOC-YYYY-BRN-NNNNNN`), `PendingAuditEntry` (outbox row).
- `DoctorDbContext` (database `NexusDoctors`) with: unique `PublicCode`; filtered-unique `LicenseNumber` `WHERE [Status] <> 4` (allows re-use after deactivation); filtered-unique `(DoctorId, Sha256)` on documents `WHERE [DeletedAtUtc] IS NULL` for SHA-256 dedup at the DB level (Phase 7 source of truth); primary-branch lookup index; outbox indexes.
- `AuditedTimestampInterceptor` stamps `CreatedAtUtc` / `UpdatedAtUtc` automatically.
- Initial migration `InitialDoctorSchema`.
- Repositories: `IDoctorRepository` (Add / GetById / find-active-by-license / Query — full-text search across name/code/license/phone/email), `IDoctorCodeSequenceRepository.NextAsync`.
- Tests: 6 — doctor round-trip, license-find skips Deactivated, license-find finds Active, paging+status filter, code-sequence increment + per-branch/year scoping.

### Phase 0 — Scaffolding

- Solution skeleton: `Nexus.Doctor.sln`, `src/Nexus.Doctor.Api`, `tests/Nexus.Doctor.Api.Tests`.
- Minimal Kestrel host on port `10000` with the Spectre.Console "Nexus Doctors" banner and startup summary panels.
- Baseline configuration (`appsettings.json` + Development + Production overlays) including a `Documents:RootPath` knob for the local file-system document storage.
- `Dockerfile` provisions the `nexus` user with ownership of `/var/data/nexus/doctor-docs/` and exposes it as a `VOLUME`.
- Top-level service artefacts: `README.md`, `CHANGELOG.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, `LICENSE`, `.gitignore`, `.dockerignore`, `.editorconfig`, `dotnet-tools.json`.
