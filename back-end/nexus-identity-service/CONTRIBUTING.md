# Contributing — nexus-identity-service

## Branching

- `main` — protected, deployable.
- Feature branches: `feat/<short-slug>`, `fix/<short-slug>`, `chore/<short-slug>`.
- Rebase on `main` before opening a PR; squash-merge on landing.

## Commits

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(auth): add refresh-token rotation
fix(admins): correct ETag generation
chore(deps): bump xunit to 2.9.2
docs(readme): clarify port override
```

## Local dev loop

```powershell
dotnet build Nexus.Identity.sln
dotnet test  Nexus.Identity.sln
dotnet run   --project src/Nexus.Identity.Api
```

## Adding a feature folder

1. Create `src/Nexus.Identity.Api/Features/<Name>/` with `Models/`, `Validators/`, `Repository/`, `Service/`, `Controller/`.
2. Co-locate validators with their DTOs; register via `AddValidatorsFromAssemblyContaining<Program>()`.
3. Add tests under `tests/Nexus.Identity.Api.Tests/Features/<Name>/`.
4. Update `docs/CONTRACT_TESTING.md` with curl walkthroughs.
5. Bump `CHANGELOG.md` under `[Unreleased]`.

## Adding an EF Core migration

```powershell
dotnet ef migrations add <Name> `
  --project src/Nexus.Identity.Api `
  --output-dir Infrastructure/Persistence/Migrations
```

## Updating the OpenAPI surface

The source of truth is [../../specs/identity.openapi.json](../../specs/identity.openapi.json). Update it first, then mirror the change in controllers, DTOs, and validators. CI diffs the generated `/swagger/v1/swagger.json` against the source spec — divergence fails the build.

## Code style

- File-scoped namespaces.
- 4-space indent, CRLF, sorted usings (enforced by `.editorconfig`).
- XML doc comments on all public types and members in features and infrastructure.

## License

MIT. By contributing you agree your contribution is licensed under the project's MIT license.
