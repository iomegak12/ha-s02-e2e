# Nexus HA — Doctor Service: Manual API Contract Testing

> Manual contract-testing playbook for `nexus-doctor-service`.
> Spec under test: [`../../../specs/doctors.openapi.json`](../../../specs/doctors.openapi.json).

---

## 1. Prerequisites

```powershell
$baseUrl = "http://localhost:10000"
$identityUrl = "http://localhost:8000"
$access = ((curl -s -X POST "$identityUrl/api/v1/auth/token" -H "Content-Type: application/json" -d "{`"username`":`"admin`",`"password`":`"<pw>`"}") | ConvertFrom-Json).accessToken
$branchId = "<from-branch-service>"
```

Need a small PDF sample (`samples/license.pdf`) ≤10 MB for upload tests; also a 12-MB file for 413 test; and a `.txt` for 415 test.

---

## 2. Spec-vs-implementation checklist

- [ ] `/swagger/v1/swagger.json` lints clean.
- [ ] All 16 `operationId`s present.
- [ ] Multipart upload op accepts `multipart/form-data` per spec.
- [ ] All 4xx use `application/problem+json`.
- [ ] `If-Match` and `Idempotency-Key` accepted where defined.

---

## 3. Operation walkthrough

### 3.1 `createDoctor` — `POST /api/v1/doctors`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"fullName`":`"Dr. Meera Iyer`",`"specialty`":`"Cardiology`",`"licenseNumber`":`"KMC-2034-981`",`"email`":`"meera@example.com`",`"phone`":`"+919811112222`"}"
```

Expect `201` with `state=Pending`. Capture `id` + `ETag`. Repeat → `409 DUPLICATE_LICENSE`.

### 3.2 `listDoctors`

```powershell
curl -i "$baseUrl/api/v1/doctors?page=1&size=20&state=Pending&search=Meera" -H "Authorization: Bearer $access"
```

Expect `200`, `PagedDoctors`.

### 3.3 `getDoctorById`

```powershell
curl -i "$baseUrl/api/v1/doctors/<id>" -H "Authorization: Bearer $access"
```

Capture `ETag`. Unknown id → `404`.

### 3.4 `patchDoctor`

```powershell
curl -i -X PATCH "$baseUrl/api/v1/doctors/<id>" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" `
  -H "If-Match: `"<etag>`"" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"specialty`":`"Interventional Cardiology`"}"
```

Expect `200`. Stale `If-Match` → `409 ETAG_MISMATCH`.

### 3.5 `uploadDoctorDocument`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/documents" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -F "type=MedicalLicense" -F "file=@samples/license.pdf;type=application/pdf"
```

Expect `201`, `DoctorDocument` body with `status=Pending`.

Failure paths:
- 13-MB file → `413 DOCUMENT_TOO_LARGE`.
- `.txt` file → `415 DOCUMENT_UNSUPPORTED_TYPE`.
- Re-upload exact same file → `409 DOCUMENT_DUPLICATE`.

### 3.6 `listDoctorDocuments`

```powershell
curl -i "$baseUrl/api/v1/doctors/<id>/documents" -H "Authorization: Bearer $access"
```

Expect `200`, `PagedDoctorDocuments` with the uploaded doc.

### 3.7 `getDoctorDocument` (metadata)

```powershell
curl -i "$baseUrl/api/v1/doctors/<id>/documents/<docId>" -H "Authorization: Bearer $access"
```

Expect `200` metadata.

### 3.8 `reviewDoctorDocument`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/documents/<docId>/review" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"decision`":`"Verified`"}"
```

Expect `200`, doc `status=Verified`. `Rejected` with empty `notes` → `422 DOCUMENT_VALIDATION`.

### 3.9 `deleteDoctorDocument` (only before Verified)

```powershell
curl -i -X DELETE "$baseUrl/api/v1/doctors/<id>/documents/<docId>" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `204` if doctor still `Pending` and doc not Verified. After doctor reaches `Verified` → `409 DOCUMENT_LOCKED`.

### 3.10 `verifyDoctor`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/verify" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `200`, `state=Verified` when **all required docs are Verified**. Otherwise → `409 LIFECYCLE_BLOCKED`.

### 3.11 `approveDoctor`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/approve" -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `200`, `state=Approved`.

### 3.12 `linkDoctorToBranch`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/branches" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"branchId`":`"$branchId`",`"isPrimary`":true}"
```

Expect `201`. Re-link → `409 BRANCH_LINK_DUPLICATE`. Unknown branch → `404`.

### 3.13 `activateDoctor`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/activate" -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `200`, `state=Active`. Without a primary active branch → `409 PRIMARY_BRANCH_REQUIRED` (precondition).

### 3.14 `listDoctorBranches`

```powershell
curl -i "$baseUrl/api/v1/doctors/<id>/branches" -H "Authorization: Bearer $access"
```

Expect `200`.

### 3.15 `unlinkDoctorFromBranch`

```powershell
curl -i -X DELETE "$baseUrl/api/v1/doctors/<id>/branches/$branchId" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `204`. Removing only primary → `409 PRIMARY_BRANCH_REQUIRED`.

### 3.16 `deactivateDoctor`

```powershell
curl -i -X POST "$baseUrl/api/v1/doctors/<id>/deactivate" -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `200`, `state=Deactivated`. Re-running `activateDoctor` brings it back to `Active`.

---

## 4. Cross-cutting

| Concern | How | Expected |
|---|---|---|
| AuthN | omit bearer | `401 UNAUTHENTICATED` |
| AuthZ | non-Admin role | `403 UNAUTHORIZED` |
| Rate limiting | enable + flood | `429 RATE_LIMITED` |
| CORS | preflight OPTIONS | `200`, `Access-Control-Allow-Origin: *` |
| Health | `GET /health/ready` | `200` JSON + per-dep entries |
| Metrics | `GET /metrics` | `200`, Prometheus text |
| Storage | upload doc, inspect `DocumentStorage:RootPath` | file present, owner non-root |
| Audit emission | upload → query audit `DoctorDocument.Uploaded` | entry visible |

---

## 5. Postman collection

Optional `docs/postman/nexus-doctor.postman_collection.json` mirroring §3 with env vars `baseUrl`, `accessToken`, `doctorId`, `documentId`, `branchId`, `etag`.

---

## 6. Sign-off

| Tester | Date | Build SHA |
|---|---|---|
| | | |
