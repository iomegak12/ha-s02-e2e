# Nexus HA — Audit Service: Manual API Contract Testing

> Manual contract-testing playbook for `nexus-audit-service`.
> Spec under test: [`../../../specs/audit.openapi.json`](../../../specs/audit.openapi.json).

---

## 1. Prerequisites

```powershell
$baseUrl = "http://localhost:12000"
$identityUrl = "http://localhost:8000"
$access = ((curl -s -X POST "$identityUrl/api/v1/auth/token" -H "Content-Type: application/json" -d "{`"username`":`"admin`",`"password`":`"<pw>`"}") | ConvertFrom-Json).accessToken
$srcSvc = "nexus-patient"
$idem   = [guid]::NewGuid().ToString()
$payload = @{
  entityType = "Patient"
  entityId = [guid]::NewGuid().ToString()
  action = "Created"
  actorId = [guid]::NewGuid().ToString()
  actorUsername = "admin"
  occurredAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  summary = "Patient created via test"
} | ConvertTo-Json -Compress
```

---

## 2. Spec-vs-implementation checklist

- [ ] `/swagger/v1/swagger.json` lints clean.
- [ ] All 3 `operationId`s present: `appendAuditEntry`, `queryAuditEntries`, `getAuditEntryById`.
- [ ] `Idempotency-Key` is **required** on `appendAuditEntry`.
- [ ] `X-Source-Service` is **optional** on `appendAuditEntry`.
- [ ] 4xx → `application/problem+json` with `traceId`.

---

## 3. Operation walkthrough

### 3.1 `appendAuditEntry` — `POST /api/v1/audit`

**Happy path (201):**

```powershell
curl -i -X POST "$baseUrl/api/v1/audit" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" `
  -H "Idempotency-Key: $idem" -H "X-Source-Service: $srcSvc" `
  -d $payload
```

Expect `201` with stored `AuditEntry`. Capture `id`.

**Idempotent replay (200, same id):**

```powershell
curl -i -X POST "$baseUrl/api/v1/audit" `
  -H "Authorization: Bearer $access" -H "Content-Type: application/json" `
  -H "Idempotency-Key: $idem" -H "X-Source-Service: $srcSvc" `
  -d $payload
```

Expect `200` (or `201` per implementation choice) with the **same** entry body and `X-Idempotent-Replay: true`.

**Conflict (409 `IDEMPOTENCY_CONFLICT`):** same headers, different payload → `409`.

**Missing key (400 `IDEMPOTENCY_KEY_MISSING`):** omit `Idempotency-Key` → `400`.

**Validation (422 `AUDIT_VALIDATION`):** set `occurredAtUtc` to a future time well past skew → `422`.

### 3.2 `queryAuditEntries` — `GET /api/v1/audit`

```powershell
curl -i "$baseUrl/api/v1/audit?entityType=Patient&action=Created&page=1&size=20&sort=occurredAtUtc%20desc" `
  -H "Authorization: Bearer $access"
```

Expect `200`, `PagedAuditEntries`. Filter combinations:

- `actorId=<uuid>` → entries by actor
- `from=2026-01-01T00:00:00Z&to=2026-12-31T23:59:59Z` → date-range slice
- `sourceService=nexus-patient` → entries from that service only

### 3.3 `getAuditEntryById` — `GET /api/v1/audit/{id}`

```powershell
curl -i "$baseUrl/api/v1/audit/<id>" -H "Authorization: Bearer $access"
```

Expect `200`. Unknown id → `404 NOT_FOUND`.

---

## 4. Cross-cutting

| Concern | How | Expected |
|---|---|---|
| AuthN | omit bearer on append | `401 UNAUTHENTICATED` |
| AuthZ | non-Admin role token | `403 UNAUTHORIZED` |
| Rate limiting | enable + 600 appends/min from same `X-Source-Service` | `429 RATE_LIMITED` past limit |
| CORS | preflight OPTIONS | `200`, `Access-Control-Allow-Origin: *` |
| Health | `GET /health/ready` | `200`, JSON per dep |
| Metrics | `GET /metrics` | `200`, Prometheus text |
| Trimming | run TTL > `RetentionDays`, observe `IdempotencyRecord` count drop | older keys gone, entries untouched |

---

## 5. Postman collection

Optional `docs/postman/nexus-audit.postman_collection.json` mirroring §3. Env vars: `baseUrl`, `accessToken`, `entryId`, `idempotencyKey`, `sourceService`.

---

## 6. Sign-off

| Tester | Date | Build SHA |
|---|---|---|
| | | |
