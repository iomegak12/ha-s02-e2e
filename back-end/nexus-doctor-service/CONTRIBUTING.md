# Contributing — Nexus HA Doctor Service

## Ground rules

- **Independence.** No project references to sibling services. Infrastructure code is copy-pasted from the audit / identity templates; `BranchesClient` is copy-pasted verbatim from the patient service (Phase 6 of the strategy).
- **Feature folders.** Behaviour under `Features/Doctors/{Models,Validators,Repository,Service,Controller}`; document subsystem under `Features/Doctors/Documents/*`; cross-cutting under `Infrastructure/<Area>`.
- **Document storage.** Always via the `IDocumentStorage` abstraction; never `File.OpenWrite` directly. Multipart bodies stream through `MultipartReader` — never bind to `IFormFile` (memory blow-up risk).
- **SHA-256 dedup.** The DB unique index on `(DoctorId, Sha256)` is the source of truth. App-level "does it exist?" checks are a hint, not a guarantee — catch `DbUpdateException` and surface the existing row.

## Local workflow

```powershell
dotnet tool restore
dotnet build Nexus.Doctor.sln
dotnet test Nexus.Doctor.sln --collect "XPlat Code Coverage"
dotnet run --project src/Nexus.Doctor.Api
```

EF Core migrations (once Phase 1 lands):

```powershell
dotnet ef migrations add <Name> --project src/Nexus.Doctor.Api --startup-project src/Nexus.Doctor.Api
dotnet ef database update --project src/Nexus.Doctor.Api --startup-project src/Nexus.Doctor.Api
```

## Coding standards

Follow `docs/LLD_Nexus_HA.md` §16. XML doc comments on every public type/member. Coverage target ≥80% line.
