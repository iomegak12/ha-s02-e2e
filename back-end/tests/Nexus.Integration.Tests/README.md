# Nexus HA — Integration Test Suite

Black-box integration tests that hit a **running** Nexus HA stack via HTTP. No
mocks, no `WebApplicationFactory`, no test doubles — every request goes through
the real network, SQL Server, JWKS, and OpenTelemetry pipeline.

| | |
|---|---|
| Project | `Nexus.Integration.Tests` (this folder) |
| Solution | `back-end/tests/Nexus.IntegrationTests.sln` (one level up) |
| Target | net10.0 |
| Tests | 38 (Identity, Audit, cross-service) |
| Runtime | ~2–3 s once the stack is warm |

## Prerequisites

1. **Stack up.** From `back-end/`:
   ```powershell
   docker compose up -d
   ```
   Wait until `nexus-identity-service` and `nexus-audit-service` report
   `/health/ready → 200`. The Identity service's `AdminSeederWorker` will
   create the seed admin (`admin` / `Nexus_DevPass1!` by default) on first boot
   when the `NexusIdentity` database is fresh.

2. **Credentials match.** The suite logs in with the values from
   `appsettings.json` (or env-var overrides). The defaults align with
   `back-end/.env.example`:

   | Key | Default |
   |---|---|
   | `IdentityBaseUrl` | `http://localhost:8000` |
   | `AuditBaseUrl` | `http://localhost:12000` |
   | `AdminUsername` | `admin` |
   | `AdminPassword` | `Nexus_DevPass1!` |
   | `HealthWaitSeconds` | `60` |

   Override any of them with environment variables of the same name when
   running in a non-default environment.

## Run

```powershell
cd back-end/tests/Nexus.Integration.Tests
dotnet test
```

Targeted runs:

```powershell
# A single test class
dotnet test --filter "FullyQualifiedName~AppendAuditTests"

# Just the Identity tier
dotnet test --filter "FullyQualifiedName~Nexus.IntegrationTests.Identity"

# Just the cross-service end-to-end proofs
dotnet test --filter "FullyQualifiedName~CrossService"
```

## Layout

```
Nexus.Integration.Tests/
├── appsettings.json
├── GlobalUsings.cs
├── Infrastructure/
│   ├── TestSettings.cs       # config loader (JSON + env)
│   └── StackFixture.cs       # waits for /health/ready, acquires admin bearer
├── Identity/
│   ├── HealthTests.cs        # /health/live, /health/ready
│   ├── AuthTokenTests.cs     # /auth/token, /refresh, /revoke, /.well-known/jwks.json
│   └── AdminCrudTests.cs     # POST → GET → PATCH, list paging, 401/404/409
├── Audit/
│   ├── AuditRequestBuilder.cs
│   ├── AppendAuditTests.cs   # 201 / 200 + replay / 409 / 400 / 422 / 401
│   ├── QueryAuditTests.cs    # filters, paging, get-by-id 200/404
│   └── HealthAndDocsTests.cs # /health, /swagger.json, /metrics counters
└── CrossService/
    └── IdentityToAuditFlowTests.cs  # token → JWKS → audit append/get + tampered
```

## State handling

Per the design decision, **each test uses unique IDs** (random GUIDs, fresh
idempotency keys, generated usernames) — there is no database cleanup between
runs. Tables accumulate over time; tear down + `docker compose down -v` when
you want a clean slate.

## Adding a new test

- Inherit the shared fixture by adding `[Collection("stack")]` and accepting
  `StackFixture` in the constructor (`_fx.AdminAccessToken` gives you a valid
  admin bearer; `_fx.Settings` gives you base URLs).
- Use `_fx.AuthClient(baseUrl)` for an auth'd client or `_fx.Client(baseUrl)`
  for anonymous.
- Build audit payloads with `AuditRequestBuilder`.

## CI

Not wired today (Q7=A: local dev only). The suite assumes the stack is already
running. CI runnability — `docker compose up -d` inside the runner, then
`dotnet test` — is straightforward when needed.

## Companion suites

The per-service in-process suites (`Nexus.Identity.Api.Tests`,
`Nexus.Audit.Api.Tests`) are kept as the **fast inner-loop tier**. They use
`WebApplicationFactory<Program>` and shim JWT validation; they don't touch
SQL Server or the network. Run them as part of regular development:

```powershell
dotnet test back-end/nexus-identity-service/Nexus.Identity.sln
dotnet test back-end/nexus-audit-service/Nexus.Audit.sln
```

This project is the **real-stack tier** — slower, network-bound, but a true
black-box proof that both services interoperate correctly.
