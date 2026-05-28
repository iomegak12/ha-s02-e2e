# Contributing — Nexus HA Patient Service

## Ground rules

- **Independence.** No project references to sibling services. Infrastructure code is copy-pasted from the audit / identity templates.
- **Feature folders.** Behaviour under `Features/Patients/{Models,Validators,Repository,Service,Controller}`; cross-cutting under `Infrastructure/<Area>`; configuration POCOs under `Configuration/`.
- **BranchesClient.** The typed HttpClient built in Phase 5 is the canonical copy; the doctor service copies it verbatim in Phase 6.

## Local workflow

```powershell
dotnet tool restore
dotnet build Nexus.Patient.sln
dotnet test Nexus.Patient.sln --collect "XPlat Code Coverage"
dotnet run --project src/Nexus.Patient.Api
```

EF Core migrations (once Phase 1 lands):

```powershell
dotnet ef migrations add <Name> --project src/Nexus.Patient.Api --startup-project src/Nexus.Patient.Api
dotnet ef database update --project src/Nexus.Patient.Api --startup-project src/Nexus.Patient.Api
```

## Coding standards

Follow `docs/LLD_Nexus_HA.md` §16. XML doc comments on every public type/member. Coverage target ≥80% line.
