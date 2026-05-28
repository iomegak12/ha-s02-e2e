# Nexus HA — BFF Service: Implementation Plan

> **Audience:** the developer building `nexus-bff-service` independently.
> **Authoritative references:** [`../../../specs/bff.openapi.json`](../../../specs/bff.openapi.json), [`../../../docs/LLD_Nexus_HA.md`](../../../docs/LLD_Nexus_HA.md), [`../../../docs/HLD_Nexus_HA.md`](../../../docs/HLD_Nexus_HA.md), [`../../../docs/ADR_Nexus_HA.md`](../../../docs/ADR_Nexus_HA.md).
> **Independence rule:** self-contained, no project references, no shared NuGet, no shared source.

---

## 1. Scope

The BFF is the **single browser-facing edge** for the Clinical Admin SPA. It owns session cookies, mints access tokens against Identity per request, and proxies every other call to the appropriate upstream via YARP. It also exposes a small first-party surface (`/api/v1/session/**`, `/api/v1/me`).

| OpenAPI tag | Operations (first-party) | Feature folder |
|---|---|---|
| `Session` | `sessionLogin`, `sessionRefresh`, `sessionLogout` | `Features/Session` |
| `Me` | `getCurrentUser` | `Features/Me` |
| `Proxy:Identity` | `proxyListAdmins`, `proxyCreateAdmin`, `proxyGetAdmin`, `proxyPatchAdmin`, `proxyDeleteAdmin` | YARP |
| `Proxy:Branches` | `proxyListBranches`, `proxyCreateBranch`, `proxyGetBranch`, `proxyPatchBranch`, `proxyDeleteBranch` | YARP |
| `Proxy:Patients` | `proxyListPatients`, `proxyCreatePatient`, `proxyGetPatient`, `proxyPatchPatient`, `proxyActivatePatient`, `proxyArchivePatient`, `proxyListPatientBranches`, `proxyLinkPatientToBranch`, `proxyUnlinkPatientFromBranch` | YARP |
| `Proxy:Doctors` | `proxyListDoctors`, `proxyCreateDoctor`, `proxyGetDoctor`, `proxyPatchDoctor`, `proxyVerifyDoctor`, `proxyApproveDoctor`, `proxyActivateDoctor`, `proxyDeactivateDoctor`, `proxyListDoctorDocuments`, `proxyUploadDoctorDocument`, `proxyGetDoctorDocument`, `proxyReviewDoctorDocument`, `proxyDeleteDoctorDocument`, `proxyListDoctorBranches`, `proxyLinkDoctorToBranch`, `proxyUnlinkDoctorFromBranch` | YARP |
| `Proxy:Audit` | `proxyQueryAuditEntries`, `proxyGetAuditEntry` | YARP |

(Refer to [`../../../specs/bff.openapi.json`](../../../specs/bff.openapi.json) for the canonical list.)

Listening port: **13000**.

**No SQL Server, no EF Core, no database.** State lives in two places: (a) the ASP.NET Data Protection keyring on disk, (b) the encrypted `nexus.sid` cookie itself.

---

## 2. Solution & project layout

```
nexus-bff-service/
├── Nexus.Bff.sln
├── src/Nexus.Bff.Api/Nexus.Bff.Api.csproj      (net10.0, Web SDK)
├── tests/Nexus.Bff.Api.Tests/Nexus.Bff.Api.Tests.csproj
├── docs/, README.md, CHANGELOG.md, TROUBLESHOOTING.md, CONTRIBUTING.md, LICENSE
├── .gitignore, .dockerignore, .editorconfig
└── Dockerfile
```

csproj baseline same as siblings: `net10.0`, Nullable on, ImplicitUsings on, `GenerateDocumentationFile=true`.

---

## 3. Feature-folder structure

```
Program.cs
Features/
├── Session/
│   ├── Models/        (SessionLoginRequest, SessionEnvelope, SessionState)
│   ├── Validators/    (SessionLoginRequestValidator)
│   ├── Service/       (ISessionService, SessionService, ISessionCookieCodec, DataProtectionSessionCookieCodec)
│   └── Controller/    (SessionController)
└── Me/
    ├── Models/        (CurrentUser)
    └── Controller/    (MeController)

Infrastructure/
├── Auth/              (SessionAuthenticationHandler, AdminRolePolicy, CurrentUser)
├── Identity/          (IIdentityClient, IdentityClient — typed HttpClient against Identity service)
├── Proxy/             (YarpConfiguration, ClusterFactory, TransformExtensions, AccessTokenInjectorTransform)
├── Resilience/        (ResilienceOptions binding)
├── Observability/     (SerilogBootstrap, OtelBootstrap, HealthCheckRegistration)
├── Errors/            (ProblemDetailsMiddleware, DomainException, ErrorCodes, ProxyErrorTranslator)
└── Startup/           (BannerWriter, StartupSummaryWriter)

Configuration/
├── SessionCookieOptions.cs   (Name=nexus.sid, Path=/, SameSite=Lax, Secure=true, HttpOnly=true, IdleTimeoutMinutes, AbsoluteTimeoutHours)
├── DataProtectionOptions.cs  (KeyRingPath, ApplicationName=nexus-bff)
├── RateLimitingOptions.cs
├── CorsOptions.cs
├── ResilienceOptions.cs      (per-upstream blocks)
├── UpstreamOptions.cs        (Identity, Branches, Patients, Doctors, Audit)
├── ObservabilityOptions.cs
└── YarpOptions.cs

appsettings.json, appsettings.Development.json, appsettings.Production.json
```

---

## 4. Persistence

**None.** No DbContext, no Migrations. The keyring folder is the only durable state.

---

## 5. Validation (FluentValidation)

- `SessionLoginRequestValidator` — `username`, `password` required.
- Auto-invocation via `ValidationFilter<T>` endpoint filter on first-party endpoints. Proxied endpoints carry validation downstream — the BFF only forwards.

---

## 6. Error model (RFC 9457 ProblemDetails)

First-party endpoints produce ProblemDetails directly. Proxy endpoints **pass through** upstream ProblemDetails verbatim, but ensure these BFF-level overrides:

| HTTP | code | When |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed JSON on first-party endpoint |
| 401 | `SESSION_INVALID` | No/expired `nexus.sid` cookie |
| 401 | `SESSION_REFRESH_FAILED` | Refresh token revoked or unknown |
| 403 | `UNAUTHORIZED` | Authenticated but not `Admin` |
| 422 | `SESSION_VALIDATION` | FluentValidation on `sessionLogin` |
| 429 | `RATE_LIMITED` | BFF-level rate limit |
| 502 | `UPSTREAM_BAD_GATEWAY` | Upstream returned 5xx after retries |
| 503 | `UPSTREAM_UNAVAILABLE` | Circuit-open or upstream unreachable |
| 504 | `UPSTREAM_TIMEOUT` | Total request timeout exceeded |

`ProxyErrorTranslator` converts YARP transport failures into these envelopes.

---

## 7. AuthN/Z

**Two authentication schemes registered:**

1. **`Session` (custom)** — `SessionAuthenticationHandler` reads the `nexus.sid` cookie, decrypts via `DataProtectionSessionCookieCodec`, validates `IdleTimeout` and `AbsoluteTimeout`, populates `ClaimsPrincipal` with `sub`, `preferred_username`, `name`, `role=Admin`. This is the **only** scheme exposed to the browser.
2. **`SessionToAccessToken` (internal)** — applied by YARP transform `AccessTokenInjectorTransform`. For each proxied request:
   - Resolve the current `SessionState` from the cookie (already done by the handler).
   - Call `IdentityClient.MintAccessTokenAsync(refreshToken)` to obtain a short-lived access token (cache it for the lifetime of the inbound request only).
   - Inject `Authorization: Bearer <access>` into the outbound request.
   - Strip the `Cookie` header from the outbound request (upstreams never see browser cookies).

**The browser never sees a JWT.** The cookie carries only an encrypted blob containing the refresh token and session metadata. The session blob shape:

```json
{ "sub":"<adminId>", "username":"admin", "name":"Admin User", "role":"Admin",
  "refreshToken":"<opaque>", "issuedAtUtc":"...", "lastActivityUtc":"...", "absoluteExpiryUtc":"..." }
```

**Cookie**: `Name=nexus.sid`, `Path=/`, `HttpOnly`, `Secure`, `SameSite=Lax`. Idle timeout default 30 min, absolute 8 h. On every authenticated request, `lastActivityUtc` is updated and the cookie is re-issued.

**Data Protection**: `services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(opts.KeyRingPath)).SetApplicationName("nexus-bff")`. KeyRingPath default `/var/data/nexus/bff-keys` (volume-mounted in production).

Policy `AdminOnly` = `RequireAuthenticatedUser().RequireRole("Admin")`. Applied to `/api/v1/me`, all `/proxy/**` routes via YARP authorization policy.

`sessionLogin`, `sessionRefresh`, `sessionLogout` are **anonymous** (logout is idempotent and safe with no cookie).

---

## 8. Resilience (Microsoft.Extensions.Http.Resilience)

Two contexts:

- **`IdentityClient`** (typed HttpClient) → `AddStandardResilienceHandler()` bound to `Resilience:Identity`. Used by:
  - `sessionLogin` (POST `/api/v1/auth/token`)
  - `sessionRefresh` (POST `/api/v1/auth/refresh`)
  - `sessionLogout` (POST `/api/v1/auth/revoke`)
  - `AccessTokenInjectorTransform` (POST `/api/v1/auth/refresh` on every proxied request) — see open item #1 about caching.
- **YARP upstream HttpClient factories** — for each cluster (`identity`, `branches`, `patients`, `doctors`, `audit`), configure the cluster's `HttpClient` with `AddStandardResilienceHandler()` bound to `Resilience:<UpstreamName>`.

```json
"Resilience": {
  "Identity": { "TotalRequestTimeoutSeconds": 10, "AttemptTimeoutSeconds": 3,
    "Retry": { "MaxRetryAttempts": 2, "Delay": "00:00:00.500", "BackoffType": "Exponential", "UseJitter": true },
    "CircuitBreaker": { "FailureRatio": 0.5, "SamplingDurationSeconds": 30, "MinimumThroughput": 8, "BreakDurationSeconds": 30 } },
  "Branches": { /* same shape */ },
  "Patients": { /* same shape */ },
  "Doctors":  { "TotalRequestTimeoutSeconds": 30, "AttemptTimeoutSeconds": 25, /* generous for multipart */
    "Retry": { "MaxRetryAttempts": 0 },
    "CircuitBreaker": { /* ... */ } },
  "Audit":    { /* same shape */ }
}
```

Multipart upload route to Doctors has **0 retries** and a longer attempt timeout — never replay a half-streamed multipart body.

---

## 9. Audit publishing

**Not applicable.** Audit fan-out is the responsibility of each upstream service (which receives the user's bearer via YARP). The BFF itself does not publish audit entries.

---

## 10. Observability

Serilog (console JSON Prod / coloured Dev) + rolling file `/var/log/nexus/bff/bff-.log` 7-day retention. Sensitive headers redacted: `Cookie`, `Authorization`, `Set-Cookie`.

OpenTelemetry .NET OTLP gRPC. Instrumentation: `AspNetCore`, `HttpClient`, `YarpReverseProxy` (built-in `Yarp.Telemetry`), `Runtime`, `Process`. Resource `service.name=nexus-bff`.

Prometheus `/metrics`. Custom metrics:
- `nexus_bff_sessions_active` (gauge — approximate via cookie issuance rate)
- `nexus_bff_token_mints_total{result}`
- `nexus_bff_proxy_requests_total{cluster,statusClass}`
- `nexus_bff_proxy_failures_total{cluster,reason}`

Health: `/health/live`, `/health/ready` — readiness pings **all 5 upstreams** (`GET /health/live` on each) and the local keyring path writability. Mark degraded (not failed) if any single non-critical upstream is down; mark **failed** only if Identity is down (no logins possible).

---

## 11. Startup banner & summary

`Spectre.Console` Figlet "Nexus BFF". Panels: Configuration (port 13000, env, cookie name + flags + timeouts, keyring path + writability, allowed CORS origins), Dependencies (Identity, Branches, Patients, Doctors, Audit reachability + circuit-breaker state stub, OTLP endpoint), Toggles (rate limiting, Swagger/ReDoc/metrics URLs, multipart upload max size).

---

## 12. Rate limiting

Fixed-window per-IP **and** per-session partition (whichever hits first):

```csharp
options.AddPolicy("ip-or-session", ctx => RateLimitPartition.GetFixedWindowLimiter(
    GetPartitionKey(ctx),
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromSeconds(60) }));
```

`GetPartitionKey` prefers `sub` from cookie if present, falls back to IP. Off by default (`RateLimiting:Enabled=false`). Login endpoint has a stricter sub-policy (10/min/IP) when enabled.

---

## 13. CORS

Default `AllowedOrigins=["*"]` but **`AllowCredentials=false`** by default. When deployed:
- Set `AllowedOrigins` to the SPA origin (e.g. `https://admin.nexusha.local`).
- Set `AllowCredentials=true`.
- Startup validator: `AllowCredentials=true` ⇒ `AllowedOrigins` must not contain `*`.

The cookie is `SameSite=Lax`, so cross-site form posts won't carry it; same-origin SPA usage is the supported model.

---

## 14. Configuration files

`appsettings.json`:

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://+:13000" } } },
  "SessionCookie": {
    "Name": "nexus.sid", "Path": "/", "SameSite": "Lax",
    "Secure": true, "HttpOnly": true,
    "IdleTimeoutMinutes": 30, "AbsoluteTimeoutHours": 8
  },
  "DataProtection": {
    "KeyRingPath": "/var/data/nexus/bff-keys",
    "ApplicationName": "nexus-bff"
  },
  "Upstreams": {
    "Identity": "http://nexus-identity-service:8000",
    "Branches": "http://nexus-branch-service:11000",
    "Patients": "http://nexus-patient-service:9000",
    "Doctors":  "http://nexus-doctor-service:10000",
    "Audit":    "http://nexus-audit-service:12000"
  },
  "ReverseProxy": {
    "Routes": { /* generated by YarpConfiguration from Upstreams */ },
    "Clusters": { /* generated by YarpConfiguration */ }
  },
  "Resilience": { "Identity": { "...": "" }, "Branches": { "...": "" }, "Patients": { "...": "" },
                   "Doctors":  { "...": "" }, "Audit":    { "...": "" } },
  "RateLimiting": { "Enabled": false, "PermitLimit": 200, "WindowSeconds": 60,
                    "LoginPermitLimit": 10, "LoginWindowSeconds": 60 },
  "Cors": { "AllowedOrigins": ["*"], "AllowedHeaders": ["*"], "AllowedMethods": ["*"], "AllowCredentials": false },
  "Observability": { "Otlp": { "Endpoint": "", "Protocol": "Grpc" }, "Prometheus": { "Enabled": true } },
  "Logging": { "File": { "Path": "logs/bff-.log", "RetainedFileCountLimit": 7 } }
}
```

`appsettings.Development.json`: `SessionCookie:Secure=false` (for localhost http), `Cors:AllowedOrigins=["http://localhost:5173"]`, `Cors:AllowCredentials=true`.
`appsettings.Production.json`: `Observability:Otlp:Endpoint=http://otel-collector:4317`, `Cors:AllowCredentials=true`, `Cors:AllowedOrigins=["https://admin.nexusha.local"]`.

All options bound with `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

## 15. OpenAPI surface

Swashbuckle `/swagger/v1/swagger.json`, Swagger UI `/swagger`, ReDoc `/redoc`. **Proxy operationIds are documented in the spec but resolved by YARP at runtime** — they do NOT have controller actions. Use `Swashbuckle.AspNetCore`'s `IDocumentFilter` to inject the proxy operations into the generated document from the source spec, or write a small `ProxyDocumentFilter` that emits each upstream operation as a passthrough description.

Acceptance criterion: the diff between the source spec and the generated spec is empty modulo `servers` and example data.

---

## 16. Static documentation (Redocly CLI)

`docs/redocly.yaml`:

```yaml
extends: [recommended]
apis:
  bff@v1:
    root: ../../../specs/bff.openapi.json
```

`docs/site/` gitignored.

---

## 17. Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Nexus.Bff.sln", "./"]
COPY ["src/Nexus.Bff.Api/Nexus.Bff.Api.csproj", "src/Nexus.Bff.Api/"]
COPY ["tests/Nexus.Bff.Api.Tests/Nexus.Bff.Api.Tests.csproj", "tests/Nexus.Bff.Api.Tests/"]
RUN dotnet restore "Nexus.Bff.sln"
COPY . .
RUN dotnet publish "src/Nexus.Bff.Api/Nexus.Bff.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S -G nexus nexus
WORKDIR /app
COPY --from=build --chown=nexus:nexus /app/publish ./
USER nexus
ENV ASPNETCORE_URLS=http://+:13000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 13000
ENTRYPOINT ["dotnet", "Nexus.Bff.Api.dll"]
```

`.dockerignore`: `**/bin`, `**/obj`, `**/.vs`, `docs/site`, `logs`, `*.user`, `.git`, `tests`.

Production deployment **must** mount a persistent volume at `DataProtection:KeyRingPath`. Without it, every container restart invalidates all sessions.

---

## 18. Test project

```
tests/Nexus.Bff.Api.Tests/
├── Features/
│   ├── Session/
│   │   ├── Validators/   (SessionLoginRequestValidatorTests)
│   │   ├── Service/      (SessionServiceTests, DataProtectionSessionCookieCodecTests)
│   │   └── Controller/   (SessionControllerTests — login/refresh/logout including idle+absolute expiry)
│   └── Me/
│       └── Controller/   (MeControllerTests)
├── Infrastructure/
│   ├── Auth/             (SessionAuthenticationHandlerTests — missing/expired/tampered cookies)
│   ├── Identity/         (IdentityClientTests via NSubstitute HttpMessageHandler)
│   ├── Proxy/            (AccessTokenInjectorTransformTests, ProxyErrorTranslatorTests)
│   ├── Errors/           (ProblemDetailsMiddlewareTests)
│   └── Observability/    (HealthCheck readiness behaviour for upstream-down scenarios)
├── Fakers/               (SessionStateFaker, ClaimsPrincipalFaker)
└── Nexus.Bff.Api.Tests.csproj
```

Packages: `xunit`, `FluentAssertions`, `NSubstitute`, `Bogus`, `coverlet.collector`, `Microsoft.AspNetCore.Mvc.Testing` (for `WebApplicationFactory<Program>`). Coverage target ≥80% line. Integration tests via `WebApplicationFactory` are encouraged here since there's no DB — easy to wire up.

---

## 19. Documentation comments

XML doc comments on every public type/member of validators, services, controllers, options, middleware, YARP transforms, session codec, error translator. `GenerateDocumentationFile=true`.

---

## 20. Top-level service artifacts

`README.md` (overview, architecture diagram pointer, cookie & session model, "no DB", quick start, config, API doc links, endpoint summary), `CHANGELOG.md`, `TROUBLESHOOTING.md` (cookie not set, keyring permissions, upstream down behaviour, SameSite issues in development, CORS misconfig), `CONTRIBUTING.md`, `LICENSE` (MIT 2026 Nexus HA Engineering), `.gitignore`, `.editorconfig`.

---

## 21. BFF-specific business rules (developer must enforce)

- **Session cookie is the only client credential.** Reject any inbound `Authorization` header from the browser (strip silently or 400 — plan: strip + warn-log).
- **Every proxied request mints a fresh access token** from Identity. Cache only **within the single inbound request lifetime** (open item #1: should we cache across requests for the same session for a short window?).
- **YARP route table** is generated programmatically from `Upstreams` config. Pattern: each operation in [`../../../specs/bff.openapi.json`](../../../specs/bff.openapi.json) tagged `Proxy:<Cluster>` maps to a YARP route with:
  - Match: the exact path + method.
  - Cluster: `<Cluster>` (e.g. `identity`, `branches`, `patients`, `doctors`, `audit`).
  - Transforms: `PathPattern: "/api/v1/{**catch-all}"` (or equivalent rewrite), `AccessTokenInjectorTransform`, `CookieHeaderRemoverTransform`, `IdempotencyKeyPassthroughTransform`, `IfMatchPassthroughTransform`, `XForwardedFor` adder.
- **Multipart upload to Doctors** (`uploadDoctorDocument`) MUST be streamed end-to-end. Configure Kestrel `Limits.MaxRequestBodySize=10485760`, YARP `MaxRequestBodySize` accordingly. Disable any buffering middleware.
- **Logout** clears the cookie (`Set-Cookie: nexus.sid=; Max-Age=0`) and best-effort calls Identity `revokeToken` with the refresh token. Failure to revoke is logged but returns 204 to the browser.
- **Refresh** rotates the refresh token in the cookie; old cookie blob is unusable after first refresh (replay → `401 SESSION_REFRESH_FAILED`).
- **Idle / absolute timeouts** evaluated on every authenticated request. Expiry → `401 SESSION_INVALID`, browser must call `sessionLogin` again.

---

## 22. Verification

```powershell
dotnet build Nexus.Bff.sln
dotnet test  Nexus.Bff.sln --collect "XPlat Code Coverage"
docker build -t nexus-bff-service:dev .
docker run --rm -p 13000:13000 -v ${PWD}/_keys:/var/data/nexus/bff-keys nexus-bff-service:dev
curl -i http://localhost:13000/health/ready
```

Plus in `docs/`:

```powershell
npx @redocly/cli lint ../../specs/bff.openapi.json
npm run docs:build
```

Acceptance: tests green; `/swagger/v1/swagger.json` exposes every `operationId` in [`../../../specs/bff.openapi.json`](../../../specs/bff.openapi.json); container boots with banner; readiness reports all 5 upstreams; manual contract tests pass per [`CONTRACT_TESTING.md`](CONTRACT_TESTING.md); restarting the container with the same keyring volume preserves an active session cookie.

---

## Open items for the dev to confirm

1. Cache the minted access token per-session for ~60 s to reduce Identity QPS? — Plan assumes **no** (mint per request) for simplicity; revisit if Identity becomes a bottleneck.
2. Should `/api/v1/me` return upstream-derived data (e.g. fetch current admin from Identity) or only cookie-derived claims? — Plan: cookie-derived only (zero upstream calls on `/me`).
3. CSRF strategy: SameSite=Lax cookies block cross-origin POST automatically; do we also need a double-submit token? — Plan: no double-submit token in Phase 1; revisit if browser surface grows beyond same-origin SPA.
