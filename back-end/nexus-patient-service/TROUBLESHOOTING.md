# Troubleshooting — Nexus HA Patient Service

## SQL Server unreachable

`/health/ready` 503 with `SqlException`. In Dev, ensure `Persistence:AutoMigrate=true`. In Prod, `Persistence:FailFast=true` crash-loops until restored.

## Branches service unavailable

`linkPatientToBranch` returns `503 DEPENDENCY_UNAVAILABLE` after the circuit opens (≥30% failure ratio over 10 s, 30 s break). Restore branches; circuit half-opens, normal flow resumes.

## Audit publisher outbox growing

`PENDING_AUDIT` row count climbing → audit service unreachable. Restore audit; `PendingAuditWorker` drains the backlog.

## Duplicate patient

`409 DUPLICATE_IDENTITY` on create. A patient with the same `phone + dateOfBirth` (or `email + dateOfBirth`) is already on file.

## Illegal state transition

`409 LIFECYCLE_INVALID_TRANSITION` on activate/archive. Inspect the patient's current `status`; the allowed transitions are `Draft → Active → Archived` (no re-activation).

## Purge worker

Stuck rows: check the worker logs for the last completed scan; `Purge:Enabled=false` disables it entirely.
