# Troubleshooting — Nexus HA Audit Service

Operational issues likely to be hit and how to recover. Expand as phases land.

## SQL Server unreachable

- Symptom: `/health/ready` returns 503, logs show `SqlException` on startup or first append.
- Check: `ConnectionStrings:Default`, SQL host/port reachable, firewall, `TrustServerCertificate`.
- Mitigation: in Dev, set `Persistence:AutoMigrate=true` so the database+schema are created on first boot. In Prod, `Persistence:FailFast=true` causes the pod to crash-loop until the dependency is restored.

## JWKS refresh failure

- Symptom: 401 with `WWW-Authenticate: Bearer error="invalid_token"`, logs show JWKS fetch error.
- Check: `Auth:Authority` reachable from the container, `<authority>/api/v1/auth/jwks` returns the key set, system clock within skew.
- Mitigation: bounce the audit service to re-pull JWKS, or roll the identity service if its signing keys are stuck.

## Idempotency conflicts

- Symptom: caller receives `409 IDEMPOTENCY_CONFLICT`.
- Meaning: the same `(SourceService, Idempotency-Key)` was reused with a **different** payload. This indicates a caller bug — they reused a key for a new event.
- Fix on caller: regenerate the `Idempotency-Key` per attempt-group (one key per business event, not per attempt of a different event).

## Clock skew rejections

- Symptom: `422 AUDIT_VALIDATION` mentioning `occurredAtUtc`.
- Meaning: `occurredAtUtc` is either more than `Idempotency:MaxClockSkewMinutes` ahead of the server, or older than 30 days behind it.
- Fix on caller: sync clocks (NTP), or backfill audit entries within the 30-day window.

## Append-only enforcement

- The API exposes no `PUT`/`PATCH`/`DELETE` on entries by design. If a record needs correction, append a follow-up entry; existing entries are immutable.

## Idempotency record trimming

- The `IdempotencyTrimWorker` removes idempotency records older than `Idempotency:RetentionDays` (default 7). Audit entries themselves are **never** deleted by this service.
- If retention is increased, expect the `IdempotencyRecords` table to grow proportionally.
