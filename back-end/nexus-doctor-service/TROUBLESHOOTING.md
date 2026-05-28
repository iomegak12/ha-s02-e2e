# Troubleshooting — Nexus HA Doctor Service

## SQL Server / Identity / Audit dependencies

Same patterns as Branches and Patients — see those services' troubleshooting docs.

## Branches service unavailable

`linkDoctorToBranch` returns `503 DEPENDENCY_UNAVAILABLE` after the circuit opens.

## Document upload failures

- `413 DOCUMENT_TOO_LARGE` — file exceeds `Documents:MaxFileBytes` (default 10 MB).
- `415 DOCUMENT_UNSUPPORTED_TYPE` — content type not in `Documents:AllowedContentTypes` (default: PDF, PNG, JPEG).
- `409 DOCUMENT_DUPLICATE` — another active document for this doctor with the same SHA-256. Fetch it via `GET /documents/{docId}` instead.
- `409 DOCUMENT_LOCKED` — the doctor has reached `Verified`; further uploads and deletes are blocked. Review-only from this point.

## Storage permissions

Container fails to start with "permission denied" on `/var/data/nexus/doctor-docs/` → the host volume isn't owned by uid/gid matching the `nexus` user inside the image. Fix on the host: `chown -R nexus:nexus /var/lib/docker/volumes/.../doctor-docs/`.

## Lifecycle blocked

`verifyDoctor` returns `409 LIFECYCLE_BLOCKED` → not all `IsRequired=true` documents are in `Verified` state. Review documents first, then re-attempt verification.

## License conflict

`createDoctor` returns `409 DUPLICATE_LICENSE` → an active doctor already holds this license number. Deactivate the existing record (rule: license filtered-unique excludes Deactivated rows).
