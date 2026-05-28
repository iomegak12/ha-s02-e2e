# Contributing — Nexus HA Audit Service

## Ground rules

- **Independence.** This service is self-contained: no project references to sibling services, no shared NuGet packages, no shared source files. If a piece of code is needed in two services, it is copied — not extracted.
- **Append-only.** The audit store is immutable after insert. Do not add `Update`/`Delete` paths to the repository or expose `PUT`/`PATCH`/`DELETE` over HTTP.
- **Feature folders.** New behaviour lives under `Features/<Area>/{Models,Validators,Repository,Service,Controller}`; cross-cutting concerns under `Infrastructure/<Area>`; configuration POCOs under `Configuration/`.
- **No outbound HTTP.** This service is the audit sink. Adding outbound HTTP calls is a design change and must be discussed first.

## Local workflow

```powershell
dotnet tool restore
dotnet build Nexus.Audit.sln
dotnet test Nexus.Audit.sln --collect "XPlat Code Coverage"
dotnet run --project src/Nexus.Audit.Api
```

EF Core migrations (once Phase 1 lands):

```powershell
dotnet ef migrations add <Name> --project src/Nexus.Audit.Api --startup-project src/Nexus.Audit.Api
dotnet ef database update --project src/Nexus.Audit.Api --startup-project src/Nexus.Audit.Api
```

## Coding standards

- Follow `docs/LLD_Nexus_HA.md` §16 (Microsoft C# coding conventions, file-scoped namespaces, nullable enabled, `LoggerMessage` source generators on hot paths, async-all-the-way, constructor injection only).
- XML doc comments on every public type/member of validators, repositories, services, controllers, options, middleware, and the idempotency filter (the `GenerateDocumentationFile` flag is on).
- Tests: xUnit + FluentAssertions + NSubstitute + Bogus; EF InMemory for repository tests. Target ≥80% line coverage.
- Never log PII.

## Pull request checklist

- [ ] Tests added/updated and green.
- [ ] OpenAPI shape unchanged or `specs/audit.openapi.json` updated to match.
- [ ] `CHANGELOG.md` updated under `[Unreleased]`.
- [ ] No new outbound HTTP dependency introduced.
- [ ] Manual contract walk-through (`docs/CONTRACT_TESTING.md`) still passes for any touched operation.
