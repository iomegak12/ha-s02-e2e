# Nexus HA — Clinical Admin Web Front-End

Blazor WebAssembly Hosted SPA for the Nexus HA Clinical Admin platform. Implements the full administrative UI with a server-side BFF (Backend-for-Frontend) embedded in the host, ensuring JWTs never reach the browser.

> Status: **Phase 1 — Foundation complete.** Auth shell, shell layout, login screen, and all stub pages are in place. Business feature pages begin in Phase 2.

## Tech stack

- .NET 10 (`net10.0`), Blazor WebAssembly Hosted model
- **Nexus.Web.Host** — ASP.NET Core 10 (BFF + static file host)
  - Cookie authentication (`HttpOnly`, `Secure`, `SameSite=Strict`)
  - In-memory JWT store, reverse-proxy middleware to 5 downstream services
  - Serilog structured logging
- **Nexus.Web.Client** — Blazor WASM SPA
  - Pure Tailwind CSS v3 (design tokens from DESIGN.md)
  - Google Material Symbols Outlined icons
  - Custom `NexusAuthStateProvider` (polls `/api/v1/me`)

## Prerequisites

- .NET 10 SDK
- Node.js ≥ 20 (for Tailwind CLI build in production; CDN used in development)
- Docker (optional, for container mode)

## Quick start

```powershell
cd front-end/Nexus.Web.Host
dotnet run
# Application starts on http://localhost:5000 (or https://localhost:5001)
# Navigate to http://localhost:5000 — you will be redirected to /login
```

> The Host also proxies all `/api/v1/*` traffic (except `/api/v1/session` and `/api/v1/me`) to the downstream microservices. Ensure those are running or configure `appsettings.json` accordingly.

## Running with Docker

```powershell
cd front-end/Nexus.Web.Host
docker build -t nexus-web-host .
docker run -p 8080:8080 nexus-web-host
```

## Running via docker-compose (full stack)

```powershell
cd back-end
docker-compose up
```

## Running tests

```powershell
# (Tests project to be added in Phase 6)
dotnet test front-end/Nexus.Web.sln
```

## Layout

```
front-end/
├── Nexus.Web.sln
├── Nexus.Web.Host/
│   ├── Nexus.Web.Host.csproj
│   ├── Program.cs                    Entry point, DI, middleware pipeline
│   ├── appsettings.json              Dev configuration (localhost service ports)
│   ├── appsettings.Production.json   Docker service name configuration
│   ├── Dockerfile                    Multi-stage build, non-root user, port 8080
│   ├── Auth/
│   │   ├── ITokenStore.cs            Interface for server-side JWT storage
│   │   ├── TokenEntry.cs             Immutable JWT record with expiry
│   │   └── InMemoryTokenStore.cs     IMemoryCache-backed implementation
│   ├── Controllers/
│   │   ├── SessionController.cs      POST /api/v1/session/{login,refresh,logout}
│   │   └── MeController.cs           GET /api/v1/me — decoded session info
│   └── Proxy/
│       ├── ServiceRouter.cs          Maps /api/v1/{prefix} → named HttpClient
│       └── ReverseProxyMiddleware.cs Injects Bearer JWT, streams response
└── Nexus.Web.Client/
    ├── Nexus.Web.Client.csproj
    ├── Program.cs                    WASM host builder, DI registrations
    ├── App.razor                     Router with AuthorizeRouteView
    ├── _Imports.razor                Global @using directives
    ├── Layout/
    │   ├── MainLayout.razor          240 px fixed sidebar + fluid main
    │   ├── NavMenu.razor             Sidebar nav, profile chip, logout
    │   └── EmptyLayout.razor         Bare layout for Login page
    ├── Pages/
    │   ├── Auth/Login.razor          Pixel-perfect login + 4-slide carousel
    │   ├── Dashboard.razor
    │   ├── Patients/PatientList.razor
    │   ├── Doctors/DoctorList.razor
    │   ├── Branches/BranchList.razor
    │   ├── Audit/AuditLog.razor
    │   ├── Settings.razor
    │   ├── Preferences.razor
    │   ├── UserProfile.razor
    │   ├── News.razor
    │   ├── Support.razor
    │   └── TermsOfService.razor
    ├── Components/Shared/
    │   ├── EmptyState.razor          Centered icon + title + message
    │   ├── LoadingSpinner.razor      Indigo animated spinner
    │   └── Toast.razor               RFC 9457 ProblemDetails-aware toast
    ├── Models/
    │   ├── Common/ProblemDetails.cs  RFC 9457
    │   ├── Common/ApiResponse.cs     Generic success/error wrapper
    │   ├── Common/PagedResult.cs     Standard paged envelope
    │   ├── Auth/LoginRequest.cs
    │   └── Auth/SessionInfo.cs
    ├── Services/Auth/
    │   ├── IAuthService.cs
    │   └── AuthService.cs            POST session/login|refresh|logout
    ├── State/
    │   ├── SessionState.cs           In-memory current session holder
    │   └── NexusAuthStateProvider.cs Custom AuthenticationStateProvider
    └── wwwroot/
        └── index.html               Tailwind CDN + Fonts + Blazor bootstrap
```

## BFF security model

The Host acts as a confidential BFF:

1. `POST /api/v1/session/login` → Host calls Identity service → stores JWT in `InMemoryTokenStore` keyed by opaque session ID → returns `HttpOnly` cookie to browser.
2. Every subsequent API call from the WASM client hits the Host on the same origin (no CORS).
3. `ReverseProxyMiddleware` reads the session cookie, looks up the JWT, injects `Authorization: Bearer …`, and proxies to the downstream service.
4. The browser **never** receives or touches a JWT.

## Documentation

- [docs/LLD_Nexus_HA.md](../docs/LLD_Nexus_HA.md) — Low-Level Design
- [docs/HLD_Nexus_HA.md](../docs/HLD_Nexus_HA.md) — High-Level Design
- [specs/](../specs/) — OpenAPI specifications for all downstream services

## License

MIT — see [LICENSE](../LICENSE).
