# Nexus HA — Patient Service: Manual API Contract Testing

> Manual contract-testing playbook for `nexus-patient-service`.
> Spec under test: [`../../../specs/patients.openapi.json`](../../../specs/patients.openapi.json).

---

## 1. Prerequisites

- Service running locally (`docker run -p 9000:9000 nexus-patient-service:dev`).
- Branch service reachable at `BranchesClient:BaseUrl` (or stubbed).
- Audit service reachable (or accept WARN-and-continue behaviour).
- Admin bearer token obtained from Identity service.

```powershell
$baseUrl = "http://localhost:9000"
$identityUrl = "http://localhost:8000"
$resp = (curl -s -X POST "$identityUrl/api/v1/auth/token" -H "Content-Type: application/json" -d "{`"username`":`"admin`",`"password`":`"<pw>`"}") | ConvertFrom-Json
$access = $resp.accessToken
$existingBranchId = "<branch-uuid-from-branch-service>"
```

---

## 2. Spec-vs-implementation checklist

- [ ] `/swagger/v1/swagger.json` lints clean via `redocly`.
- [ ] All 9 `operationId`s present (`listPatients`, `createPatient`, `getPatientById`, `patchPatient`, `activatePatient`, `archivePatient`, `listPatientBranches`, `linkPatientToBranch`, `unlinkPatientFromBranch`).
- [ ] Errors use `application/problem+json` with `traceId`.
- [ ] `If-Match` and `Idempotency-Key` accepted where spec defines them.

---

## 3. Operation walkthrough

### 3.1 `createPatient` — `POST /api/v1/patients`

```powershell
$idem = [guid]::NewGuid()
curl -i -X POST "$baseUrl/api/v1/patients" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" -H "Idempotency-Key: $idem" `
  -d "{`"fullName`":`"Asha Rao`",`"dateOfBirth`":`"1992-05-04`",`"gender`":`"Female`",`"phone`":`"+919812345670`",`"email`":`"asha@example.com`"}"
```

Expect: `201` with `Location` and full `Patient` body in `state=Draft`. Capture `id` and `ETag`.

**409 `PATIENT_DUPLICATE_PHONE_DOB`:** repeat with same `phone` + `dateOfBirth`, new `Idempotency-Key` → 409.
**422 `PATIENT_VALIDATION`:** future `dateOfBirth` → 422 with `errors[]`.

### 3.2 `listPatients` — `GET /api/v1/patients`

```powershell
curl -i "$baseUrl/api/v1/patients?page=1&size=20&state=Draft&search=Asha" -H "Authorization: Bearer $access"
```

Expect: `200`, `PagedPatients` shape (`items, page, size, total`).

### 3.3 `getPatientById` — `GET /api/v1/patients/{id}`

```powershell
$id = "<from-create>"
curl -i "$baseUrl/api/v1/patients/$id" -H "Authorization: Bearer $access"
```

Expect: `200`, capture `ETag` header. `404` for unknown UUID.

### 3.4 `patchPatient` — `PATCH /api/v1/patients/{id}`

```powershell
curl -i -X PATCH "$baseUrl/api/v1/patients/$id" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" `
  -H "If-Match: `"<etag-from-get>`"" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"fullName`":`"Asha M. Rao`"}"
```

Expect: `200`, new `ETag`. Re-run with stale `If-Match` → `409 ETAG_MISMATCH`.

### 3.5 `linkPatientToBranch` — `POST /api/v1/patients/{id}/branches`

```powershell
curl -i -X POST "$baseUrl/api/v1/patients/$id/branches" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"branchId`":`"$existingBranchId`",`"isPrimary`":true}"
```

Expect: `201`, link body. Re-link the same branch → `409 BRANCH_LINK_DUPLICATE`. Link an unknown branch → `404`.

### 3.6 `activatePatient` — `POST /api/v1/patients/{id}/activate`

```powershell
curl -i -X POST "$baseUrl/api/v1/patients/$id/activate" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect: `200`, `state=Active`. Without any active primary link → `409 PRIMARY_BRANCH_REQUIRED` (precondition).

### 3.7 `listPatientBranches` — `GET /api/v1/patients/{id}/branches`

```powershell
curl -i "$baseUrl/api/v1/patients/$id/branches?page=1&size=20" -H "Authorization: Bearer $access"
```

Expect: `200`, paged links. Soft-deleted links absent (see Open Items #1 in implementation plan).

### 3.8 `unlinkPatientFromBranch` — `DELETE /api/v1/patients/{id}/branches/{branchId}`

```powershell
curl -i -X DELETE "$baseUrl/api/v1/patients/$id/branches/$existingBranchId" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect: `204`. Removing the only primary link on an `Active` patient → `409 PRIMARY_BRANCH_REQUIRED`.

### 3.9 `archivePatient` — `POST /api/v1/patients/{id}/archive`

```powershell
curl -i -X POST "$baseUrl/api/v1/patients/$id/archive" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect: `200`, `state=Archived`. Attempting `activatePatient` afterwards → `409 LIFECYCLE_INVALID` (Archived is terminal).

---

## 4. Cross-cutting

| Concern | How | Expected |
|---|---|---|
| AuthN | omit `Authorization` | `401 UNAUTHENTICATED` |
| AuthZ | non-Admin role token | `403 UNAUTHORIZED` |
| Rate limiting | enable + flood >100/min | `429 RATE_LIMITED` + `Retry-After` |
| CORS | preflight OPTIONS | `200` + `Access-Control-Allow-Origin: *` |
| Health | `GET /health/ready` | `200` + JSON per dep |
| Metrics | `GET /metrics` | `200`, Prometheus text |
| Audit emission | create patient → query audit for `Patient.Created` | entry present |
| Branches down | stop branch service, attempt `linkPatientToBranch` | `503` after circuit opens; `/health/ready` reports degraded |

---

## 5. Postman collection

Optional collection at `docs/postman/nexus-patient.postman_collection.json` mirroring §3. Env vars: `baseUrl`, `accessToken`, `patientId`, `branchId`, `etag`.

---

## 6. Sign-off

| Tester | Date | Build SHA |
|---|---|---|
| | | |
