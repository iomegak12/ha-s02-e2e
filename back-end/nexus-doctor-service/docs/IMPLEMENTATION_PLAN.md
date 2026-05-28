# Nexus HA — Doctor Service: Implementation Plan

> **Audience:** the developer building `nexus-doctor-service` independently.
> **Authoritative references:** [`../../../specs/doctors.openapi.json`](../../../specs/doctors.openapi.json), [`../../../docs/LLD_Nexus_HA.md`](../../../docs/LLD_Nexus_HA.md), [`../../../docs/HLD_Nexus_HA.md`](../../../docs/HLD_Nexus_HA.md), [`../../../docs/ADR_Nexus_HA.md`](../../../docs/ADR_Nexus_HA.md).
> **Independence rule:** self-contained, no project references, no shared NuGet, no shared source.

---

## 1. Scope

| OpenAPI tag | Operations | Feature folder |
|---|---|---|
| `Doctors` | `listDoctors`, `createDoctor`, `getDoctorById`, `patchDoctor` | `Features/Doctors` |
| `DoctorLifecycle` | `verifyDoctor`, `approveDoctor`, `activateDoctor`, `deactivateDoctor` | `Features/DoctorLifecycle` |
| `DoctorDocuments` | `listDoctorDocuments`, `uploadDoctorDocument`, `getDoctorDocument`, `reviewDoctorDocument`, `deleteDoctorDocument` | `Features/DoctorDocuments` |
| `DoctorBranches` | `listDoctorBranches`, `linkDoctorToBranch`, `unlinkDoctorFromBranch` | `Features/DoctorBranches` |

Listening port: **10000**.

**Lifecycle:** `Pending → Verified → Approved → Active → Deactivated`. Reactivation from `Deactivated` to `Active` is permitted (one-way arc back); not via the same operationId — `activateDoctor` is reused. Verification requires **all required documents in `Verified` state**.

---

## 2. Solution & project layout

```
nexus-doctor-service/
├── Nexus.Doctor.sln
├── src/Nexus.Doctor.Api/Nexus.Doctor.Api.csproj    (net10.0, Web SDK)
├── tests/Nexus.Doctor.Api.Tests/Nexus.Doctor.Api.Tests.csproj
├── docs/, README.md, CHANGELOG.md, TROUBLESHOOTING.md, CONTRIBUTING.md, LICENSE
├── .gitignore, .dockerignore, .editorconfig
└── Dockerfile
```

csproj baseline: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=false`, `GenerateDocumentationFile=true`. File-scoped namespaces.

---

## 3. Feature-folder structure (under `src/Nexus.Doctor.Api/`)

```
Program.cs
Features/
├── Doctors/
│   ├── Models/        (Doctor, CreateDoctorRequest, PatchDoctorRequest, DoctorListItem, PagedDoctors)
│   ├── Validators/    (CreateDoctorRequestValidator, PatchDoctorRequestValidator)
│   ├── Repository/    (IDoctorRepository, DoctorRepository)
│   ├── Service/       (IDoctorService, DoctorService, ILicenseDuplicateDetector)
│   └── Controller/    (DoctorsController)
├── DoctorLifecycle/
│   ├── Service/       (IDoctorLifecycleService, DoctorLifecycleService, DoctorStateMachine)
│   └── Controller/    (DoctorLifecycleController)
├── DoctorDocuments/
│   ├── Models/        (DoctorDocument, UploadDoctorDocumentRequest, ReviewDoctorDocumentRequest, PagedDoctorDocuments)
│   ├── Validators/    (UploadDoctorDocumentRequestValidator, ReviewDoctorDocumentRequestValidator)
│   ├── Repository/    (IDoctorDocumentRepository, DoctorDocumentRepository)
│   ├── Storage/       (IDocumentStorage, LocalFileSystemDocumentStorage, StoredDocumentRef)
│   ├── Service/       (IDoctorDocumentService, DoctorDocumentService)
│   └── Controller/    (DoctorDocumentsController)
└── DoctorBranches/
    ├── Models/        (DoctorBranchLink, LinkDoctorToBranchRequest, PagedDoctorBranches)
    ├── Validators/    (LinkDoctorToBranchRequestValidator)
    ├── Repository/    (IDoctorBranchRepository, DoctorBranchRepository)
    ├── Service/       (IDoctorBranchService, DoctorBranchService)
    └── Controller/    (DoctorBranchesController)

Infrastructure/
├── Auth/              (JwtBearerConfig — RS256 via Identity JWKS, AdminRolePolicy, CurrentUser)
├── Persistence/       (DoctorDbContext, RowVersionInterceptor, Migrations/)
├── Resilience/
├── Clients/           (IBranchesClient, BranchesClient)
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
├── BranchesClientOptions.cs
├── AuditClientOptions.cs
├── ObservabilityOptions.cs
└── DocumentStorageOptions.cs  (Provider=LocalFileSystem, RootPath, MaxBytes, AllowedContentTypes)

appsettings.json, appsettings.Development.json, appsettings.Production.json
```

---

## 4. Persistence (EF Core 10, code-first)

DbContext `DoctorDbContext` → database **`NexusDoctors`**.

- `Doctor` → `Doctors` (Id PK, FullName, Specialty, LicenseNumber `nvarchar(64)` UQ filtered (`State <> 'Deactivated'`), Email, Phone, State `nvarchar(16)` (`Pending|Verified|Approved|Active|Deactivated`), CreatedAtUtc, UpdatedAtUtc, RowVersion).
- `DoctorDocument` → `DoctorDocuments` (Id PK, DoctorId FK, Type `nvarchar(32)` — `MedicalLicense|Degree|GovId|Other`, IsRequired `bit`, FileName, ContentType, SizeBytes, StorageKey `nvarchar(512)`, Sha256 `binary(32)`, Status `nvarchar(16)` — `Pending|Verified|Rejected`, ReviewNotes, ReviewedByAdminId null, ReviewedAtUtc null, UploadedAtUtc, DeletedAtUtc null, RowVersion). Filtered unique index `(DoctorId, Type) WHERE DeletedAtUtc IS NULL AND Type='MedicalLicense'`.
- `DoctorBranchLink` → `DoctorBranchLinks` (Id PK, DoctorId FK, BranchId, IsPrimary, LinkedAtUtc, UnlinkedAtUtc null). Filtered unique `(DoctorId, BranchId) WHERE UnlinkedAtUtc IS NULL`.

ETag = base64(RowVersion). `If-Match` → `409 ETAG_MISMATCH` on mismatch.

---

## 5. Validation (FluentValidation)

- `CreateDoctorRequestValidator` — `fullName` 1–200, `specialty` required, `licenseNumber` `[A-Z0-9-]{4,64}`, `email`/`phone` format.
- `PatchDoctorRequestValidator` — at least one field; `licenseNumber` immutable once Verified (enforce in service, not validator).
- `UploadDoctorDocumentRequestValidator` — `type` enum; file shape validated in controller via multipart pipeline (size, content-type).
- `ReviewDoctorDocumentRequestValidator` — `decision` enum `Verified|Rejected`, `notes` required when `Rejected`.
- `LinkDoctorToBranchRequestValidator` — `branchId` GUID.

Auto-invocation via `ValidationFilter<T>` endpoint filter.

---

## 6. Error model (RFC 9457 ProblemDetails)

| HTTP | code | When |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed JSON or multipart |
| 401 | `UNAUTHENTICATED` | Missing/invalid bearer |
| 403 | `UNAUTHORIZED` | Authenticated but not `Admin` |
| 404 | `NOT_FOUND` | Doctor/Document/Link unknown |
| 409 | `DUPLICATE_LICENSE` | License number collision on create |
| 409 | `ETAG_MISMATCH` | `If-Match` mismatch on patch |
| 409 | `LIFECYCLE_INVALID` | Illegal state transition |
| 409 | `LIFECYCLE_BLOCKED` | `verifyDoctor` when ≥1 required doc not Verified |
| 409 | `DOCUMENT_LOCKED` | Document operation after doctor reaches Verified (upload/delete forbidden) |
| 409 | `DOCUMENT_DUPLICATE` | Re-uploading the same SHA-256 |
| 409 | `BRANCH_LINK_DUPLICATE` | Re-linking an already-linked branch |
| 409 | `PRIMARY_BRANCH_REQUIRED` | Removing the only primary link on Active doctor |
| 413 | `DOCUMENT_TOO_LARGE` | Upload >10 MB |
| 415 | `DOCUMENT_UNSUPPORTED_TYPE` | Upload not `application/pdf`, `image/png`, `image/jpeg` |
| 422 | `DOCTOR_VALIDATION` / `DOCUMENT_VALIDATION` | FluentValidation failure |
| 429 | `RATE_LIMITED` | Rate limit hit |

---

## 7. AuthN/Z

JwtBearer: Authority `http://nexus-identity-service:8000`, `MetadataAddress=<authority>/api/v1/auth/jwks`, Audience `nexus-ha`, Issuer `https://api.nexusha.local/identity`. Policy `AdminOnly` on all controllers. `JwtBearerOptions.MapInboundClaims=false`.

---

## 8. Resilience

Typed clients `BranchesClient`, `AuditClient`, each registered with `AddStandardResilienceHandler()` bound to `Resilience:Branches` / `Resilience:Audit`. Same shape as Identity/Patient plans.

---

## 9. Audit publishing

`IAuditPublisher` → `HttpAuditPublisher` (typed client, with `Idempotency-Key` UUIDv7 + forwarded admin bearer). Failures logged WARN, swallowed.

Events emitted: `Doctor.Created`, `Doctor.Updated`, `Doctor.StateChanged` (`from→to`), `DoctorDocument.Uploaded`, `DoctorDocument.Reviewed` (`decision`), `DoctorDocument.Deleted`, `DoctorBranch.Linked`, `DoctorBranch.Unlinked`.

---

## 10. Observability

Serilog (console + rolling file `/var/log/nexus/doctor/doctor-.log`), OpenTelemetry OTLP gRPC, Prometheus `/metrics`. Custom metrics: `nexus_doctor_state_transitions_total{from,to}`, `nexus_doctor_documents_uploaded_total{type,result}`. Health checks `/health/live`, `/health/ready` (SQL Server + Branches HEAD + document storage path writability), `/health`.

---

## 11. Startup banner & summary

`Spectre.Console` Figlet "Nexus Doctor". Panels: Configuration (port 10000, env, JWT authority/audience, document storage root, max bytes, allowed content types), Dependencies (SQL, Branches, Audit, OTLP), Toggles (rate limiting, CORS, Swagger/ReDoc/metrics URLs).

---

## 12. Rate limiting

Fixed-window IP, `PermitLimit=100`, `WindowSeconds=60`, off by default. Multipart upload requests are exempt from per-second policies but counted in window. 429 → `RATE_LIMITED` + `Retry-After`.

---

## 13. CORS

Default `{ *, *, *, false }`. Validator: `AllowCredentials=true` ⇒ origins must not be `*`.

---

## 14. Configuration files

`appsettings.json`:

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://+:10000" } } },
  "ConnectionStrings": { "Default": "<provided-later>" },
  "Auth": { "Authority": "http://nexus-identity-service:8000", "Audience": "nexus-ha",
            "Issuer": "https://api.nexusha.local/identity" },
  "Persistence": { "AutoMigrate": false, "FailFast": false },
  "RateLimiting": { "Enabled": false, "PermitLimit": 100, "WindowSeconds": 60 },
  "Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false },
  "BranchesClient": { "BaseUrl": "http://nexus-branch-service:11000" },
  "AuditClient":    { "BaseUrl": "http://nexus-audit-service:12000" },
  "Resilience": { "Branches": { /* same shape */ }, "Audit": { /* same shape */ } },
  "DocumentStorage": {
    "Provider": "LocalFileSystem",
    "RootPath": "/var/data/nexus/doctor-docs",
    "MaxBytes": 10485760,
    "AllowedContentTypes": ["application/pdf", "image/png", "image/jpeg"]
  },
  "Observability": { "Otlp": { "Endpoint": "", "Protocol": "Grpc" }, "Prometheus": { "Enabled": true } },
  "Logging": { "File": { "Path": "logs/doctor-.log", "RetainedFileCountLimit": 7 } }
}
```

`appsettings.Development.json`: `Persistence:AutoMigrate=true`, local writable `DocumentStorage:RootPath`.
`appsettings.Production.json`: `Observability:Otlp:Endpoint=http://otel-collector:4317`, `Persistence:FailFast=true`.

All options bound with `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

## 15. OpenAPI surface

Swashbuckle `/swagger`, ReDoc `/redoc`. Multipart upload op (`uploadDoctorDocument`) requires `OperationFilter` to attach `multipart/form-data` requestBody schema (already present in source spec). XML doc comments feed descriptions. CI: `spectral lint specs/doctors.openapi.json` + diff vs generated.

---

## 16. Static documentation (Redocly CLI)

`docs/redocly.yaml`:

```yaml
extends: [recommended]
apis:
  doctors@v1:
    root: ../../../specs/doctors.openapi.json
```

`docs/site/` gitignored.

---

## 17. Dockerfile (multi-stage, Alpine, no HEALTHCHECK)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Nexus.Doctor.sln", "./"]
COPY ["src/Nexus.Doctor.Api/Nexus.Doctor.Api.csproj", "src/Nexus.Doctor.Api/"]
COPY ["tests/Nexus.Doctor.Api.Tests/Nexus.Doctor.Api.Tests.csproj", "tests/Nexus.Doctor.Api.Tests/"]
RUN dotnet restore "Nexus.Doctor.sln"
COPY . .
RUN dotnet publish "src/Nexus.Doctor.Api/Nexus.Doctor.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 10000
ENTRYPOINT ["dotnet", "Nexus.Doctor.Api.dll"]
```

`.dockerignore`: `**/bin`, `**/obj`, `**/.vs`, `docs/site`, `logs`, `*.user`, `.git`, `tests`.

Note: production volume mount for `DocumentStorage:RootPath` documented in `README.md`.

---

## 18. Test project (xUnit + FluentAssertions + NSubstitute + Bogus + coverlet)

```
tests/Nexus.Doctor.Api.Tests/
├── Features/
│   ├── Doctors/                  (Validators, Repository EF InMemory, Service, Controller, LicenseDuplicateDetectorTests)
│   ├── DoctorLifecycle/          (DoctorStateMachineTests — all illegal transitions, LIFECYCLE_BLOCKED when docs incomplete, Controller)
│   ├── DoctorDocuments/
│   │   ├── Validators/           (Upload, Review)
│   │   ├── Repository/           (DoctorDocumentRepositoryTests)
│   │   ├── Storage/              (LocalFileSystemDocumentStorageTests — temp dir, SHA-256 dedup, size/type guards)
│   │   ├── Service/              (DoctorDocumentServiceTests — DOCUMENT_LOCKED after Verified)
│   │   └── Controller/           (DoctorDocumentsControllerTests — 413, 415, multipart streaming)
│   └── DoctorBranches/           (Validators, Repository, Service — PRIMARY_BRANCH_REQUIRED, Controller)
├── Infrastructure/
│   ├── Errors/                   (ProblemDetailsMiddlewareTests)
│   ├── Clients/                  (BranchesClientTests via NSubstitute HttpMessageHandler)
│   └── Audit/                    (HttpAuditPublisherTests)
├── Fakers/                       (DoctorFaker, DoctorDocumentFaker, DoctorBranchLinkFaker)
└── Nexus.Doctor.Api.Tests.csproj
```

Packages as per Patient plan. Coverage target ≥80% line, reported not gated.

---

## 19. Documentation comments

XML doc comments on every public type/member of validators, repositories, services, controllers, options, middleware, storage abstraction. `GenerateDocumentationFile=true`, `.editorconfig` keeps `CS1591` at `suggestion`.

---

## 20. Top-level service artifacts

`README.md` (incl. volume mount instructions for documents), `CHANGELOG.md` (`## [0.1.0] - Unreleased`), `TROUBLESHOOTING.md` (storage path permissions, multipart upload 413 vs 415, JWKS refresh, branches down), `CONTRIBUTING.md`, `LICENSE` (MIT 2026 Nexus HA Engineering), `.gitignore`, `.editorconfig`.

---

## 21. Doctor-specific business rules (developer must enforce)

- **Lifecycle state machine** (`DoctorStateMachine`):
  - `Pending → Verified` via `verifyDoctor` only when all `IsRequired=true` documents are `Verified`. Otherwise `409 LIFECYCLE_BLOCKED`.
  - `Verified → Approved` via `approveDoctor`.
  - `Approved → Active` via `activateDoctor`.
  - `Active → Deactivated` via `deactivateDoctor`.
  - `Deactivated → Active` via `activateDoctor` (re-activation permitted).
  - Any other transition → `409 LIFECYCLE_INVALID`.
- **Document lock**: once doctor reaches `Verified`, `uploadDoctorDocument` and `deleteDoctorDocument` return `409 DOCUMENT_LOCKED`. `reviewDoctorDocument` remains allowed (re-review). Deleting before verification is a **soft delete** (`DeletedAtUtc`).
- **License immutability**: once doctor is `Verified` or later, `patchDoctor` rejecting changes to `licenseNumber` with `409 DOCUMENT_LOCKED` (reuse code, message clarifies field).
- **Multipart upload**: streamed via `request.ReadFormAsync()` with `MultipartBodyLengthLimit=10MB`. Compute SHA-256 while streaming to a temp file; reject duplicates (`409 DOCUMENT_DUPLICATE`) and oversize/unsupported (`413`/`415`).
- **`IDocumentStorage`** is the only abstraction for binary content. `LocalFileSystemDocumentStorage` writes to `RootPath/<doctorId>/<sha256-prefix>/<storageKey>`. Returning `StoredDocumentRef { StorageKey, SizeBytes, Sha256 }`. Future swap to S3/Azure Blob requires a new impl only.
- **Primary branch invariant**: same as patient — `Active` doctor must have exactly one primary active link; removing it without a successor → `409 PRIMARY_BRANCH_REQUIRED`.

---

## 22. Verification

```powershell
dotnet build Nexus.Doctor.sln
dotnet test  Nexus.Doctor.sln --collect "XPlat Code Coverage"
docker build -t nexus-doctor-service:dev .
docker run --rm -p 10000:10000 -v ${PWD}/_docs:/var/data/nexus/doctor-docs `
  -e ConnectionStrings__Default="<sql>" nexus-doctor-service:dev
curl -s http://localhost:10000/health/ready
```

Plus in `docs/`:

```powershell
npx @redocly/cli lint ../../specs/doctors.openapi.json
npm run docs:build
```

Acceptance: tests green; `/swagger/v1/swagger.json` exposes all 16 operationIds from [`../../../specs/doctors.openapi.json`](../../../specs/doctors.openapi.json); container boots with banner; manual contract tests pass per [`CONTRACT_TESTING.md`](CONTRACT_TESTING.md).

---

## Open items for the dev to confirm

1. After `Deactivated`, can the doctor's documents be re-uploaded? — Plan assumes **no** (`DOCUMENT_LOCKED` persists once reached `Verified`).
2. Should `reviewDoctorDocument` be allowed on a soft-deleted document? — Plan assumes **no** (404).
3. Should we store original filename verbatim or sanitized? — Plan: store both (`FileName` original; storage path uses sanitized + sha prefix).
