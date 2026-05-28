# Nexus HA — BFF Service: Manual API Contract Testing

> Manual contract-testing playbook for `nexus-bff-service`.
> Spec under test: [`../../../specs/bff.openapi.json`](../../../specs/bff.openapi.json).

---

## 1. Prerequisites

- All 5 upstreams running (Identity 8000, Patient 9000, Doctor 10000, Branch 11000, Audit 12000).
- BFF running on 13000 with a writable keyring volume mounted.
- For local dev, set `SessionCookie:Secure=false` so `nexus.sid` is sent over HTTP.

```powershell
$baseUrl = "http://localhost:13000"
$jar = "cookies.txt"   # netscape format
Remove-Item $jar -ErrorAction Ignore
```

We use `curl -b $jar -c $jar` to persist cookies across calls.

---

## 2. Spec-vs-implementation checklist

- [ ] `/swagger/v1/swagger.json` lists every `operationId` from the source spec (first-party + Proxy:*).
- [ ] Browser never receives a JWT — only `Set-Cookie: nexus.sid=...; HttpOnly; Secure; SameSite=Lax`.
- [ ] Outbound proxied requests carry `Authorization: Bearer ...` (verify with Wireshark/`tcpdump` on the upstream side or via upstream access logs).
- [ ] Outbound proxied requests do **not** carry `Cookie` headers.
- [ ] Upstream ProblemDetails are passed through unmodified; transport failures map to BFF 502/503/504.

---

## 3. Operation walkthrough — First-party

### 3.1 `sessionLogin` — `POST /api/v1/session/login`

```powershell
curl -i -X POST "$baseUrl/api/v1/session/login" `
  -H "Content-Type: application/json" -c $jar `
  -d "{`"username`":`"admin`",`"password`":`"<pw>`"}"
```

Expect: `204` (or `200` per spec) with `Set-Cookie: nexus.sid=...` carrying `HttpOnly`, `Path=/`, `SameSite=Lax`, and (in production) `Secure`. **No** body containing a JWT.

Failure: bad password → `401 SESSION_REFRESH_FAILED` (or `INVALID_CREDENTIALS` per Identity passthrough, depending on implementation). No cookie set.

### 3.2 `getCurrentUser` — `GET /api/v1/me`

```powershell
curl -i -b $jar "$baseUrl/api/v1/me"
```

Expect: `200`, `CurrentUser` body `{ id, username, name, role:"Admin" }`. Without cookie → `401 SESSION_INVALID`.

### 3.3 `sessionRefresh` — `POST /api/v1/session/refresh`

```powershell
curl -i -X POST -b $jar -c $jar "$baseUrl/api/v1/session/refresh"
```

Expect: `204`, new `Set-Cookie: nexus.sid=...` (different blob). Old cookie value reused → `401 SESSION_REFRESH_FAILED`.

### 3.4 `sessionLogout` — `POST /api/v1/session/logout`

```powershell
curl -i -X POST -b $jar -c $jar "$baseUrl/api/v1/session/logout"
```

Expect: `204`, `Set-Cookie: nexus.sid=; Max-Age=0`. Idempotent — running it again returns `204` even with no cookie.

---

## 4. Operation walkthrough — Proxy

Re-login first to obtain a fresh cookie.

### 4.1 `proxyListBranches` — `GET /api/v1/proxy/branches`

```powershell
curl -i -b $jar "$baseUrl/api/v1/proxy/branches?page=1&size=20"
```

Expect: `200`, `PagedBranches`. Same body as calling branch service directly.

### 4.2 `proxyCreatePatient` — `POST /api/v1/proxy/patients`

```powershell
curl -i -X POST -b $jar "$baseUrl/api/v1/proxy/patients" `
  -H "Content-Type: application/json" -H "Idempotency-Key: $([guid]::NewGuid())" `
  -d "{`"fullName`":`"Asha Rao`",`"dateOfBirth`":`"1992-05-04`",`"gender`":`"Female`",`"phone`":`"+919812345670`"}"
```

Expect: `201`, full `Patient` body. Verify on patient service side that the request arrived with `Authorization: Bearer <admin-jwt>` and no `Cookie` header.

### 4.3 `proxyUploadDoctorDocument` — `POST /api/v1/proxy/doctors/{id}/documents` (multipart)

```powershell
curl -i -X POST -b $jar "$baseUrl/api/v1/proxy/doctors/<doctorId>/documents" `
  -H "Idempotency-Key: $([guid]::NewGuid())" `
  -F "type=MedicalLicense" -F "file=@samples/license.pdf;type=application/pdf"
```

Expect: `201`. Verify upstream (doctor service) received the multipart body in full (compare SHA-256). 13-MB file → `413` from upstream, passed through.

### 4.4 `proxyActivatePatient` — `POST /api/v1/proxy/patients/{id}/activate`

```powershell
curl -i -X POST -b $jar "$baseUrl/api/v1/proxy/patients/<patientId>/activate" `
  -H "Idempotency-Key: $([guid]::NewGuid())"
```

Expect: `200` if preconditions met, `409 LIFECYCLE_INVALID`/`PRIMARY_BRANCH_REQUIRED` otherwise — passthrough verbatim.

### 4.5 `proxyQueryAuditEntries` — `GET /api/v1/proxy/audit`

```powershell
curl -i -b $jar "$baseUrl/api/v1/proxy/audit?entityType=Patient&page=1&size=20"
```

Expect: `200`, `PagedAuditEntries`.

### 4.6 Other proxy operations

Repeat the pattern for all remaining proxy operationIds in [`../../../specs/bff.openapi.json`](../../../specs/bff.openapi.json). For each: confirm status code passthrough, `If-Match` passthrough on PATCH, `Idempotency-Key` passthrough on POST/PATCH/DELETE where the upstream defines it.

---

## 5. Cross-cutting

| Concern | How | Expected |
|---|---|---|
| No JWT to browser | Inspect every BFF response — no `accessToken`/`refreshToken` JSON, no Authorization echo | confirmed |
| Cookie flags | `Set-Cookie: nexus.sid=...` includes `HttpOnly`, `Secure` (Prod), `SameSite=Lax`, `Path=/` | confirmed |
| Idle timeout | Wait > `IdleTimeoutMinutes` of inactivity, then call `/api/v1/me` | `401 SESSION_INVALID` |
| Absolute timeout | Wait > `AbsoluteTimeoutHours`, then call any authenticated endpoint | `401 SESSION_INVALID` |
| Upstream down (Identity) | Stop Identity, call any proxy endpoint | `503 UPSTREAM_UNAVAILABLE` (access-token mint fails) |
| Upstream down (Patients) | Stop Patient, call `/proxy/patients` | `503 UPSTREAM_UNAVAILABLE` after circuit opens |
| Upstream 5xx | Force a 500 from upstream | `502 UPSTREAM_BAD_GATEWAY` after retries |
| Upstream timeout | Block upstream beyond cluster timeout | `504 UPSTREAM_TIMEOUT` |
| CORS preflight | OPTIONS with `Origin` matching `Cors:AllowedOrigins` and `Access-Control-Request-Credentials: true` | `200` with appropriate ACA-* headers |
| Health (ready) | Stop one non-critical upstream (e.g. Audit), `GET /health/ready` | degraded; status code still 200 (configurable) |
| Health (ready) | Stop Identity, `GET /health/ready` | `503` (Identity is critical) |
| Metrics | `GET /metrics` | `200`, Prometheus text incl. `nexus_bff_*` metrics |
| Keyring persistence | Stop BFF container, restart with same volume mounted, call `/api/v1/me` with the old cookie | `200` (cookie decrypts) |
| Keyring rotation | Without mounted volume, restart container, reuse old cookie | `401 SESSION_INVALID` (expected — keys lost) |

---

## 6. Postman collection

Optional `docs/postman/nexus-bff.postman_collection.json` mirroring §3–§4. Env vars: `baseUrl`, `username`, `password`. Use Postman's cookie jar.

---

## 7. Sign-off

| Tester | Date | Build SHA |
|---|---|---|
| | | |
