# Troubleshooting — Nexus HA Branch Service

Operational issues likely to be hit and how to recover. Expand as phases land.

## SQL Server unreachable

- Symptom: `/health/ready` returns 503, logs show `SqlException` on startup or first call.
- Mitigation: in Dev, set `Persistence:AutoMigrate=true`. In Prod, `Persistence:FailFast=true` causes crash-loop until the dependency is restored.

## Identity (JWKS) refresh failure

- Symptom: 401 with `WWW-Authenticate: Bearer error="invalid_token"`.
- Check: `Auth:Authority` reachable from the container; `<authority>/.well-known/jwks.json` returns key set.

## Audit publisher outbox growing

- Symptom: `PENDING_AUDIT` table row count increasing; audit service unreachable.
- Mitigation: restore audit service; `PendingAuditWorker` will drain in the background.

## Duplicate branch code

- Symptom: `409 BRANCH_CODE_DUPLICATE`.
- Meaning: another active branch already uses this code. Deactivate the existing one first if you need to reuse the code.
