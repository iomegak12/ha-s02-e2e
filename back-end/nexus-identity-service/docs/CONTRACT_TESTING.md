# Nexus HA — Identity Service: Manual API Contract Testing

> Manual contract-testing playbook for `nexus-identity-service`.
> Spec under test: [`../../../specs/identity.openapi.json`](../../../specs/identity.openapi.json).
> Use this before opening a PR and as a smoke test against any environment.

---

## 1. Prerequisites

- Service running locally (`docker run -p 8000:8000 nexus-identity-service:dev`) or against a target environment.
- Tools: `curl` (or Postman). All requests use `application/json` unless stated.
- Replace `{{baseUrl}}` with `http://localhost:8000` for local, or the target env URL.
- A dev-seeded admin (see `IMPLEMENTATION_PLAN.md §21`) or a pre-created admin's credentials.

```powershell
$baseUrl = "http://localhost:8000"
$adminUser = "admin"
$adminPass = "<from-startup-log-or-env>"
```

---

## 2. Spec-vs-implementation checklist

Tick each box before releasing.

- [ ] `/swagger/v1/swagger.json` round-trips through `redocly lint` with **zero errors**.
- [ ] Every `operationId` from the source spec exists in the generated spec.
- [ ] Every example in the spec is reproducible with the curl scripts below.
- [ ] All error responses use `application/problem+json` and include `traceId`.
- [ ] `If-Match` and `Idempotency-Key` headers are accepted where the spec defines them.

---

## 3. Operation walkthrough

### 3.1 `issueToken` — `POST /api/v1/auth/token`

**Happy path (200):**

```powershell
curl -i -X POST "$baseUrl/api/v1/auth/token" `
  -H "Content-Type: application/json" `
  -d "{`"username`":`"$adminUser`",`"password`":`"$adminPass`"}"
```

Expect: `200`, body containing `accessToken`, `tokenType=Bearer`, `expiresIn`, `refreshToken`.

Capture for next calls:

```powershell
$resp = (curl -s -X POST "$baseUrl/api/v1/auth/token" -H "Content-Type: application/json" -d "{`"username`":`"$adminUser`",`"password`":`"$adminPass`"}") | ConvertFrom-Json
$access  = $resp.accessToken
$refresh = $resp.refreshToken
```

**Failure (401 `INVALID_CREDENTIALS`):** wrong password → expect `application/problem+json`, `code=INVALID_CREDENTIALS`.

**Failure (422 `ADMIN_VALIDATION`):** empty body → expect 422 with `errors[]`.

---

### 3.2 `refreshToken` — `POST /api/v1/auth/refresh`

**Happy (200):**

```powershell
curl -i -X POST "$baseUrl/api/v1/auth/refresh" `
  -H "Content-Type: application/json" `
  -d "{`"refreshToken`":`"$refresh`"}"
```

Expect: new `accessToken` + new `refreshToken`. Old `$refresh` is now revoked.

**Failure (401 `REFRESH_INVALID`):** re-submit the **old** refresh token → expect 401.

---

### 3.3 `revokeToken` — `POST /api/v1/auth/revoke`

```powershell
curl -i -X POST "$baseUrl/api/v1/auth/revoke" `
  -H "Content-Type: application/json" `
  -d "{`"refreshToken`":`"$refresh`"}"
```

Expect: `204`. Re-run the same call → still `204` (idempotent).

---

### 3.4 `getJwks` — `GET /api/v1/auth/jwks`

```powershell
curl -i "$baseUrl/api/v1/auth/jwks"
```

Expect: `200`, JSON `{ "keys": [ { "kty":"RSA", "kid":"...", "alg":"RS256", "use":"sig", "n":"...", "e":"AQAB" } ] }`. `Cache-Control: public, max-age=300` header present.

---

### 3.5 `listAdmins` — `GET /api/v1/admins`

```powershell
curl -i "$baseUrl/api/v1/admins?page=1&size=20&isActive=true" `
  -H "Authorization: Bearer $access"
```

Expect: `200`, `PagedAdmins` shape with `items`, `page`, `size`, `total`.

**401 path:** omit `Authorization` → `401 UNAUTHENTICATED`.

---

### 3.6 `createAdmin` — `POST /api/v1/admins`

```powershell
$idem = [guid]::NewGuid().ToString()
curl -i -X POST "$baseUrl/api/v1/admins" `
  -H "Authorization: Bearer $access" `
  -H "Content-Type: application/json" `
  -H "Idempotency-Key: $idem" `
  -d "{`"username`":`"rohit.shah`",`"displayName`":`"Rohit Shah`",`"password`":`"ChangeMe!2026xyz`"}"
```

Expect: `201`, `Location` header set, `Admin` body.

**409 `ADMIN_USERNAME_DUPLICATE`:** repeat the same call with a different `Idempotency-Key`.

**422 `ADMIN_VALIDATION`:** password ≤11 chars → 422 with `errors[]`.

**Idempotency replay:** repeat with the **same** `Idempotency-Key` → identical 201 response (or 200 with stored result, per implementation).

---

### 3.7 `getAdminById` — `GET /api/v1/admins/{id}`

```powershell
$id = "<id-from-create>"
curl -i "$baseUrl/api/v1/admins/$id" -H "Authorization: Bearer $access"
```

Capture `ETag` response header (e.g. `ETag: "AAAAAAAAH9k="`) for the patch test.

**404:** random UUID → `404 NOT_FOUND`.

---

### 3.8 `patchAdmin` — `PATCH /api/v1/admins/{id}`

```powershell
curl -i -X PATCH "$baseUrl/api/v1/admins/$id" `
  -H "Authorization: Bearer $access" `
  -H "Content-Type: application/json" `
  -H "If-Match: `"AAAAAAAAH9k=`"" `
  -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"displayName`":`"Rohit S.`"}"
```

Expect: `200`, updated body, new `ETag`.

**409 `ETAG_MISMATCH`:** reuse the old `If-Match` → `409` with `code=ETAG_MISMATCH`.

---

### 3.9 `deactivateAdmin` — `DELETE /api/v1/admins/{id}`

```powershell
curl -i -X DELETE "$baseUrl/api/v1/admins/$id" `
  -H "Authorization: Bearer $access" `
  -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect: `204`. Repeat → still `204` (idempotent). Re-`GET` the admin → `isActive=false`.

---

## 4. Cross-cutting tests

| Concern | How to test | Expected |
|---|---|---|
| Rate limiting (when enabled) | Set `RateLimiting:Enabled=true`, fire 101 GETs to `/api/v1/admins` in <60s | The 101st returns `429 RATE_LIMITED` with `Retry-After` |
| CORS | `curl -i -X OPTIONS $baseUrl/api/v1/admins -H "Origin: https://example.com" -H "Access-Control-Request-Method: GET"` | `200` with `Access-Control-Allow-Origin: *` |
| ProblemDetails shape | Any 4xx response | `application/problem+json`, fields `type/title/status/code/detail/instance/traceId` |
| Health | `curl $baseUrl/health/ready` | `200` JSON with `status=Healthy` and entry per dependency |
| Metrics | `curl $baseUrl/metrics` | `200` `text/plain` Prometheus exposition |
| Audit emission | Create an admin, then `GET /api/v1/audit?entityType=Admin&action=Created` against audit service | New entry visible |

---

## 5. Postman collection (optional)

A Postman v2.1 collection mirroring §3 lives at `docs/postman/nexus-identity.postman_collection.json` (developer to create). Use environment variables `baseUrl`, `adminUser`, `adminPass`, `accessToken`, `refreshToken`, `adminId`, `etag`.

---

## 6. Sign-off

When every box in §2 is ticked and every walkthrough in §3 produces the expected status code and body shape, sign and date below.

| Tester | Date | Build SHA |
|---|---|---|
| | | |
