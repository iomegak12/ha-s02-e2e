# Phased build strategy for `nexus-identity-service`

Sequenced the 22-section IMPLEMENTATION_PLAN.md into 10 phases organized as **two vertical slices wrapped in scaffolding, infrastructure, and finishing work**. The order is chosen so each phase is independently verifiable and so JwtBearer validation in Phase 4 can target a JWKS issued by Phase 3 (avoiding mock keys).

## Steps

1. **Phase 0 — Repository scaffolding.** Solution, `src/` + `tests/` csprojs (net10.0), top-level docs/`.gitignore`/`.editorconfig`/`LICENSE`, minimal `Program.cs` exposing `/health/live` on port 8000. *(§2, §20)*
2. **Phase 1 — Cross-cutting infrastructure.** Configuration options, `ProblemDetailsMiddleware`, pagination primitives, Serilog + OpenTelemetry + Prometheus, Spectre.Console startup banner, Swashbuckle + ReDoc, CORS + rate limiting (toggleable), health endpoints. Build once, reuse everywhere. *(§6, §10–§15)*
3. **Phase 2 — Persistence.** `IdentityDbContext`, entities (`Admin`, `RefreshToken`, `SigningKey`), `RowVersionInterceptor`, `InitialIdentitySchema` migration, SQL probe added to `/health/ready`. *(§4)*
4. **Phase 3 — Auth vertical slice (issuer).** `SqlSigningKeyStore` (bootstrap first RSA-2048), `JwtIssuer`, `JwksProvider`, refresh-token repo with SHA-256 hash + rotation, `BCryptPasswordHasher`, validators, `AuthController` with `/auth/token|refresh|revoke|jwks`. Verified via CONTRACT_TESTING §3.1–3.4. *(§7)*
5. **Phase 4 — Admins vertical slice (consumer).** JwtBearer wired to loopback JWKS, `AdminOnly` policy, `AdminRepository` + `AdminService`, full CRUD controller, `If-Match`/ETag handling, `Idempotency-Key` on create. Verified via CONTRACT_TESTING §3.5–3.10. *(§5, §6, §7)*
6. **Phase 5 — Audit publishing.** `HttpAuditPublisher` with `AddStandardResilienceHandler`, hooked into admin/auth events; failures logged-and-swallowed. *(§8, §9) — parallel with Phase 6.*
7. **Phase 6 — Dev seeding.** Environment-+-flag-gated seeder that creates first admin and WARN-logs a random password once. *(§21) — parallel with Phase 5.*
8. **Phase 7 — Test suite.** xUnit/FluentAssertions/NSubstitute/Bogus/coverlet; per-feature validator/service/repository/controller tests + infrastructure tests. Developed incrementally per slice, gated here. *(§18)*
9. **Phase 8 — Containerization & static docs.** Multi-stage Alpine `Dockerfile` (non-root), `.dockerignore`, Redocly site, populated `README.md`. *(§16, §17, §20)*
10. **Phase 9 — Spec parity & sign-off.** Diff generated swagger vs `specs/identity.openapi.json`, Redocly lint zero errors, complete CONTRACT_TESTING §2 checklist, resolve the 3 open architect items, tag `v0.1.0`. *(§22 + open items)*

## Relevant files (to be created)

- [back-end/nexus-identity-service/Nexus.Identity.sln](back-end/nexus-identity-service/Nexus.Identity.sln)
- [back-end/nexus-identity-service/src/Nexus.Identity.Api/Program.cs](back-end/nexus-identity-service/src/Nexus.Identity.Api/Program.cs)
- `src/Nexus.Identity.Api/Features/Auth/**` (Models, Validators, Repository, Service, Controller)
- `src/Nexus.Identity.Api/Features/Admins/**` (same shape)
- `src/Nexus.Identity.Api/Infrastructure/**` (Auth, Persistence, Errors, Pagination, Observability, Audit, Startup)
- `src/Nexus.Identity.Api/Configuration/**` (option classes)
- `tests/Nexus.Identity.Api.Tests/**`
- [back-end/nexus-identity-service/Dockerfile](back-end/nexus-identity-service/Dockerfile)

## Verification (per phase, cumulative)

1. Phase 0: `dotnet build` + `curl /health/live` → 200.
2. Phase 1: banner renders; `/swagger`, `/redoc`, `/metrics`, `/health/live` all respond.
3. Phase 2: fresh-DB migration succeeds; `/health/ready` → 200.
4. Phase 3: manual run of CONTRACT_TESTING.md §3.1–3.4; JWKS validates a sample JWT on `jwt.io`.
5. Phase 4: CONTRACT_TESTING.md §3.5–3.10 walkthrough, including 409 `ETAG_MISMATCH`, 409 `ADMIN_USERNAME_DUPLICATE`, idempotency replay.
6. Phase 5: with audit stubbed to 503, admin endpoints still succeed; circuit breaker observed in logs.
7. Phase 6: container boot in `Development` against empty DB logs seeded password exactly once.
8. Phase 7: `dotnet test --collect "XPlat Code Coverage"` all green, coverage ≥80% reported.
9. Phase 8: `docker build` + `docker run -p 8000:8000` succeeds; `npm run docs:build` produces `docs/site/index.html`.
10. Phase 9: Redocly lint zero errors; every `operationId` from `specs/identity.openapi.json` is present in generated `/swagger/v1/swagger.json`.

## Decisions

- **Build order = 0→1→2→3→4→{5,6}→7→8→9.** Phases 5 and 6 are parallelizable. Phase 7 runs continuously alongside features but is gated for completeness here.
- Auth issuer (Phase 3) is built **before** Admins consumer (Phase 4) so JwtBearer validates against a real JWKS — no mock keys, no rework.
- Tests evolve alongside each feature (TDD-friendly), but Phase 7 is the formal sign-off gate.
- **In scope:** all operations in §1 table; self-contained codebase per Independence Rule.
- **Out of scope for Phase 1:** integration-test project, audit outbox pattern, OIDC discovery doc beyond JWKS, refresh-token replay-detection beyond rotation.

## Further considerations (open items from the plan, surface to architect)

1. `SigningKey.PrivateKeyPemEncrypted` — Option A: encrypt via ASP.NET Data Protection (assumed). Option B: rely on column-level SQL TDE only. *Recommendation: A.*
2. Refresh-token policy — Option A: rotate mandatorily on every refresh (assumed). Option B: sliding-extend without rotation. *Recommendation: A (defense-in-depth).*
3. JWKS freshness — `Cache-Control: max-age=300` gives a 5-minute staleness window for downstream services. Confirm acceptable for the rotation cadence (`RotationDays=30`).
