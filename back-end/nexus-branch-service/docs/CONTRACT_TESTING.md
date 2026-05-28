# Nexus HA — Branch Service: Manual API Contract Testing

> Manual contract-testing playbook for `nexus-branch-service`.
> Spec under test: [`../../../specs/branches.openapi.json`](../../../specs/branches.openapi.json).

---

## 1. Prerequisites

```powershell
$baseUrl = "http://localhost:11000"
$identityUrl = "http://localhost:8000"
$access = ((curl -s -X POST "$identityUrl/api/v1/auth/token" -H "Content-Type: application/json" -d "{`"username`":`"admin`",`"password`":`"<pw>`"}") | ConvertFrom-Json).accessToken
```

---

## 2. Spec-vs-implementation checklist

- [ ] `/swagger/v1/swagger.json` lints clean.
- [ ] All 5 `operationId`s present: `listBranches`, `createBranch`, `getBranchById`, `patchBranch`, `deactivateBranch`.
- [ ] 4xx → `application/problem+json` with `traceId`.
- [ ] `If-Match` and `Idempotency-Key` accepted where defined.

---

## 3. Operation walkthrough

### 3.1 `createBranch` — `POST /api/v1/branches`

```powershell
curl -i -X POST "$baseUrl/api/v1/branches" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"code`":`"BLR-INDIRANAGAR`",`"name`":`"Bengaluru – Indiranagar`",`"addressLine1`":`"100 Feet Rd`",`"city`":`"Bengaluru`",`"state`":`"KA`",`"postalCode`":`"560038`",`"country`":`"IN`",`"phone`":`"+918012345678`",`"email`":`"blr-ind@nexusha.local`"}"
```

Expect `201`, `Location` header, full `Branch` body, `isActive=true`. Capture `id` + `ETag`.

Repeat with same `code`, new `Idempotency-Key` → `409 BRANCH_CODE_DUPLICATE`.

Empty `code` → `422 BRANCH_VALIDATION`.

### 3.2 `listBranches`

```powershell
curl -i "$baseUrl/api/v1/branches?page=1&size=20&isActive=true&search=Indiranagar" -H "Authorization: Bearer $access"
```

Expect `200`, `PagedBranches`. With `isActive=false`, returns inactive too.

### 3.3 `getBranchById`

```powershell
curl -i "$baseUrl/api/v1/branches/<id>" -H "Authorization: Bearer $access"
```

Capture `ETag`. Unknown id → `404 NOT_FOUND`.

### 3.4 `patchBranch`

```powershell
curl -i -X PATCH "$baseUrl/api/v1/branches/<id>" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" `
  -H "If-Match: `"<etag>`"" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"name`":`"Bengaluru – Indiranagar (Main)`"}"
```

Expect `200`, new `ETag`. Stale `If-Match` → `409 ETAG_MISMATCH`. Attempt to patch `code` → `422 BRANCH_VALIDATION`.

### 3.5 `deactivateBranch`

```powershell
curl -i -X DELETE "$baseUrl/api/v1/branches/<id>" `
  -H "Authorization: Bearer $access" -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect `204`. Re-run → still `204` (idempotent). `GET /api/v1/branches/<id>` shows `isActive=false`.

---

## 4. Cross-cutting

| Concern | How | Expected |
|---|---|---|
| AuthN | omit bearer | `401 UNAUTHENTICATED` |
| AuthZ | non-Admin role token | `403 UNAUTHORIZED` |
| Rate limiting | enable + flood | `429 RATE_LIMITED` + `Retry-After` |
| CORS | preflight OPTIONS | `200`, `Access-Control-Allow-Origin: *` |
| Health | `GET /health/ready` | `200`, JSON per dep |
| Metrics | `GET /metrics` | `200`, Prometheus text |
| Audit emission | create branch → query audit `Branch.Created` | entry visible |

---

## 5. Postman collection

Optional `docs/postman/nexus-branch.postman_collection.json` mirroring §3.

---

## 6. Sign-off

| Tester | Date | Build SHA |
|---|---|---|
| | | |
