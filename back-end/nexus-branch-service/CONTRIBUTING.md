# Contributing — Nexus HA Branch Service

## Ground rules

- **Independence.** No project references to sibling services, no shared NuGet packages, no shared source files. Infrastructure code is copy-pasted from the audit / identity templates.
- **Master data.** This service is the source of truth for branch identifiers. Patient and Doctor services validate against `GET /api/v1/branches/{id}` before linking.
- **Feature folders.** New behaviour lives under `Features/Branches/{Models,Validators,Repository,Service,Controller}`; cross-cutting concerns under `Infrastructure/<Area>`; configuration POCOs under `Configuration/`.

## Local workflow

```powershell
dotnet tool restore
dotnet build Nexus.Branch.sln
dotnet test Nexus.Branch.sln --collect "XPlat Code Coverage"
dotnet run --project src/Nexus.Branch.Api
```

EF Core migrations (once Phase 1 lands):

```powershell
dotnet ef migrations add <Name> --project src/Nexus.Branch.Api --startup-project src/Nexus.Branch.Api
dotnet ef database update --project src/Nexus.Branch.Api --startup-project src/Nexus.Branch.Api
```

## Coding standards

Follow `docs/LLD_Nexus_HA.md` §16 (Microsoft C# coding conventions, file-scoped namespaces, nullable enabled, async-all-the-way, constructor injection only). XML doc comments on every public type/member. Tests: xUnit + FluentAssertions + NSubstitute + Bogus. Coverage target ≥80% line.
