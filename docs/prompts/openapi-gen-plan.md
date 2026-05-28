# Plan: Nexus HA OpenAPI Specifications

Produce **OpenAPI 3.1.0 JSON specs** for all six Nexus HA components — one self-contained file per microservice (6 total) — covering complete request/response/error contracts derived from `docs/LLD_Nexus_HA.md` (with HLD/BRD as supporting references).

## Decisions (user-confirmed)
- **Spec version:** OpenAPI 3.1.0
- **Layout:** One spec file per service (6 files), no shared components file — each spec is self-contained
- **Format:** JSON
- **Endpoint coverage:** LLD §8 endpoints + obvious CRUD completions (e.g., missing `GET`/`PATCH`/`DELETE` implied by the resource model)
- **BFF shape:** Pure 1:1 proxy mirror of backend service paths
- **Security:** Bearer JWT on all backend services; BFF uses Bearer **plus** a cookie/session scheme; public endpoints (`/auth/token`, `/auth/refresh`, `/.well-known/jwks.json`) marked `security: []`
- **Errors:** Per-endpoint relevant errors only (not the full Appendix A on every operation); always `application/problem+json`
- **Examples:** One realistic example per request and per response (including error responses)
- **Extras included:** `servers` block (local/dev/prod), `tags` with descriptions, `operationId` on every operation, `Idempotency-Key` header on unsafe writes, reusable pagination parameters (`page`, `size`)

## Target Files
1. `specs/identity.openapi.json` — Identity & Admin service
2. `specs/patients.openapi.json` — Patients service
3. `specs/doctors.openapi.json` — Doctors service
4. `specs/branches.openapi.json` — Branches service
5. `specs/audit.openapi.json` — Audit service
6. `specs/bff.openapi.json` — BFF (proxy mirror of all backend paths)

## Standard Structure (applied uniformly to every file)
- **info:** title, version `1.0.0`, description, contact, license
- **servers:** local (`http://localhost:PORT`), dev, prod placeholders
- **tags:** grouped by resource (e.g., Auth, Admins, Patients, Branches-Association)
- **security:** Bearer JWT default; BFF adds cookie/session scheme
- **components.schemas:**
  - Domain resources (Patient, Doctor, Branch, Admin, AuditEntry, Document, RefreshToken, JWKS)
  - DTOs: Create/Update/Patch variants, state-change request bodies
  - Enums: PatientStatus, DoctorStatus, DocumentStatus, AuditAction, EntityType
  - `ProblemDetails` (RFC 9457) with `code`, `traceId`, `errors[]` extensions
  - Pagination envelope (`PagedResult<T>` pattern via discriminated wrapper)
- **components.parameters:** `PageParam`, `SizeParam`, `SortParam`, `IdempotencyKeyHeader`, `IfMatchETag`
- **components.responses:** Common reusable problem+json responses (`Unauthorized401`, `Forbidden403`, `NotFound404`, `Conflict409`, `Validation422`, `RateLimited429`, `DependencyUnavailable503`)
- **components.securitySchemes:** `bearerAuth` (http/bearer JWT); BFF additionally `sessionCookie` (apiKey in cookie)

## Per-Service Coverage Summary

### identity.openapi.json
- Public: `POST /api/v1/auth/token`, `POST /api/v1/auth/refresh`, `GET /.well-known/jwks.json`
- Secured: `POST /api/v1/auth/revoke`, `GET|POST|PATCH /api/v1/admins`, `GET /api/v1/admins/{id}`, `DELETE` (deactivate) completion
- Errors: 400, 401, 403, 404, 409 (duplicate username), 422, 429

### patients.openapi.json
- CRUD: `POST`, `GET /{id}`, `GET` (list with filters: status, branchId, page, size, sort), `PATCH /{id}`, `DELETE /{id}` (LLD-completion: archive-only soft delete)
- State: `POST /{id}/activate`, `POST /{id}/archive`
- Branch association: `POST /{id}/branches`, `DELETE /{id}/branches/{branchId}`, `GET /{id}/branches` (completion)
- Errors per endpoint: 400, 401, 403, 404, 409 (DUPLICATE_IDENTITY, LIFECYCLE_INVALID_TRANSITION), 422 (BRANCH_UNKNOWN), 429, 503

### doctors.openapi.json
- CRUD: as patients
- Documents: `POST /{id}/documents` (multipart), `GET /{id}/documents`, `GET /{id}/documents/{docId}`, `DELETE /{id}/documents/{docId}` (completion)
- State: `/verify`, `/approve`, `/activate`, `/deactivate`
- Branch association: same shape as patients
- Errors: includes 409 LIFECYCLE_INVALID_TRANSITION emphasized on state endpoints

### branches.openapi.json
- CRUD: `GET`, `GET /{id}`, `POST`, `PATCH /{id}`, `DELETE /{id}` (completion — soft via IsActive)
- Errors: 400, 401, 403, 404, 409 (duplicate Code), 422, 429

### audit.openapi.json
- Internal: `POST /api/v1/audit` (Idempotency-Key required)
- Query: `GET /api/v1/audit` (entityType, entityId, from, to, page, size)
- Completion: `GET /api/v1/audit/{id}` for single-entry fetch
- Errors: 400, 401, 403, 404, 422, 429

### bff.openapi.json
- Session: `POST /api/v1/session/login`, `POST /api/v1/session/logout`, `POST /api/v1/session/refresh`
- 1:1 proxy mirror of all paths from identity/patients/doctors/branches/audit under same `/api/v1/...` prefix
- Security: `sessionCookie` OR `bearerAuth`
- Errors: adds 502/503/504 to surface upstream/proxy failures

## Implementation Phases
1. **Phase A — Shared design templates** *(internal, not a file)*: lock down ProblemDetails schema, pagination envelope, enums, common parameters, common responses. These will be inlined identically into each self-contained file.
2. **Phase B — Backend service specs (parallel-safe):** draft `identity`, `branches`, `audit`, `patients`, `doctors` JSON files.
3. **Phase C — BFF spec:** assemble the proxy mirror, layering session endpoints and dual security schemes on top of the mirrored paths.
4. **Phase D — Verification:** lint each file with an OpenAPI validator; confirm coverage against LLD §8 and the BRD traceability matrix (LLD §17).

## Relevant Files
- `docs/LLD_Nexus_HA.md` — primary source (§6 standards, §7 errors, §8 endpoints, §9 schema, §10–11 auth, §12 NFRs, §13 BFF, §14 audit, Appendix A errors)
- `docs/HLD_Nexus_HA.md` — fallback context for ambiguities
- `docs/BRD_Patient_Doctor_Registry.docx` — only consulted if a functional requirement is unclear

## Verification
1. Each JSON file parses as valid OpenAPI 3.1.0 (e.g., `npx @redocly/cli lint` or `swagger-cli validate`)
2. Every LLD §8 endpoint appears in the corresponding spec with matching method/path
3. Every state-transition endpoint declares `409 LIFECYCLE_INVALID_TRANSITION` in responses
4. Every unsafe write declares the `Idempotency-Key` header parameter
5. All error responses use `application/problem+json` and reference the standard `ProblemDetails` schema
6. Public endpoints carry `security: []`; all others inherit `bearerAuth`
7. Pagination params and reusable responses are defined once per file (consistent naming)
8. Examples render correctly in Swagger UI / Redoc

## Scope Boundaries
- **In scope:** Six OpenAPI JSON files, complete request/response/error contracts, examples, reusable components within each file.
- **Out of scope:** Code generation, server stubs, client SDKs, infrastructure config, CI wiring for the linter, generated HTML docs.

## Open / Assumed Items (flag during drafting, not blocking)
- Exact port numbers in `servers` block — will use sensible defaults (5001–5006) and clearly mark as placeholders
- Doctor-document storage response shape (LLD lists it as an open item §19) — will model `storagePath` as opaque string
- Reactivation of Archived patients within 30-day window (LLD §19 open item) — will **not** add a reactivate endpoint; flag in spec description
