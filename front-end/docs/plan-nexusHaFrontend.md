# Nexus HA — Frontend Implementation Plan

## Architecture Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Framework | .NET 10 Blazor WebAssembly Hosted | Full SPA in browser; ASP.NET Core host absorbs BFF responsibilities |
| Language | C# | Consistent with back-end microservices |
| UI Strategy | Pure Tailwind CSS + Material Symbols Outlined | 100% fidelity to HTML mockups — no component library abstraction layer |
| Modularization | Two-project solution: `Nexus.Web.Client` + `Nexus.Web.Host` | Clean separation of WASM client from server-side BFF/proxy host |
| API Clients | Hand-written HttpClient service classes per domain | No code generation; explicit control over request/response handling |
| BFF Pattern | Embedded in `Nexus.Web.Host` | Replaces the unbuilt `nexus-bff-service`; session cookies server-side, JWTs never reach browser |
| Auth Storage | HttpOnly/Secure/SameSite=Strict cookie (server-side session) | Zero XSS token exposure; `Nexus.Web.Host` holds JWT, browser holds opaque cookie |
| CORS | Non-issue — browser only talks to `Nexus.Web.Host` (single origin) | Host proxies all `/api/v1/*` to downstream services server-to-server |
| Containerization | Dockerfile + docker-compose entry in `back-end/docker-compose.yml` | Consistent with all other Nexus HA microservices |
| Phasing | Foundation-first | Auth → Shell → Branches → Patients → Doctors → Audit → Settings/Static |

---

## Solution Structure

```
front-end/
  Nexus.Web.sln
  Nexus.Web.Client/               # Blazor WASM project
    Nexus.Web.Client.csproj
    wwwroot/
      index.html
      css/
        app.css                   # Tailwind build output + custom tokens
      fonts/                      # Self-hosted Roboto + JetBrains Mono (optional)
    App.razor
    Routes.razor
    _Imports.razor
    Layout/
      MainLayout.razor             # Fixed sidebar + fluid content shell
      NavMenu.razor                # 240px sidebar with all nav items
      TopBar.razor                 # Optional top bar (user chip, breadcrumb)
    Pages/
      Auth/
        Login.razor
      Dashboard/
        Dashboard.razor
      Patients/
        PatientList.razor
        PatientAdd.razor           # 3-step wizard host
        Steps/
          Step1PersonalInfo.razor
          Step2AddressContact.razor
          Step3BranchAssociation.razor
      Doctors/
        DoctorList.razor
        DoctorAdd.razor            # 3-step wizard host
        Steps/
          Step1Profile.razor
          Step2Credentials.razor
          Step3BranchSetup.razor
      Branches/
        BranchList.razor
      Audit/
        AuditLog.razor
      Settings/
        Settings.razor
        Preferences.razor
      Profile/
        UserProfile.razor
      Static/
        News.razor
        Support.razor
        TermsOfService.razor
    Components/
      Shared/
        StatusBadge.razor          # Pill badge: Active/Pending/Verified/Error/Archived
        DataTable.razor            # Reusable paginated table wrapper
        PageHeader.razor           # Title + breadcrumb + action button slot
        ConfirmModal.razor         # Generic confirmation dialog
        Toast.razor                # Problem Details toast (RFC 9457)
        LoadingSpinner.razor
        EmptyState.razor
        WizardStepper.razor        # Step indicator for multi-step forms
      Forms/
        TextField.razor
        SelectField.razor
        DateField.razor
        FileUpload.razor           # Doctor credential document upload
    Services/
      Auth/
        IAuthService.cs
        AuthService.cs             # POST /api/v1/session/login|logout|refresh
      Identity/
        IIdentityService.cs
        IdentityService.cs         # Admins CRUD → proxied via Host
      Patients/
        IPatientService.cs
        PatientService.cs          # Patient CRUD + lifecycle + branch links
      Doctors/
        IDoctorService.cs
        DoctorService.cs           # Doctor CRUD + lifecycle + documents + branch links
      Branches/
        IBranchService.cs
        BranchService.cs           # Branches CRUD
      Audit/
        IAuditService.cs
        AuditService.cs            # Audit query (read-only)
    Models/
      Auth/
        LoginRequest.cs
        SessionInfo.cs
      Patients/
        Patient.cs
        CreatePatientRequest.cs
        PatchPatientRequest.cs
        PatientBranchLink.cs
      Doctors/
        Doctor.cs
        CreateDoctorRequest.cs
        PatchDoctorRequest.cs
        DoctorDocument.cs
        ReviewDocumentRequest.cs
        DoctorBranchLink.cs
      Branches/
        Branch.cs
        CreateBranchRequest.cs
        PatchBranchRequest.cs
      Audit/
        AuditEntry.cs
        AuditQuery.cs
      Common/
        PagedResult.cs
        ProblemDetails.cs          # RFC 9457
        ApiResponse.cs             # Wrapper: T Data | ProblemDetails Error
    State/
      AuthStateProvider.cs         # Extends AuthenticationStateProvider
      SessionState.cs              # In-memory: current admin info, session expiry

  Nexus.Web.Host/                 # ASP.NET Core host (BFF embedded)
    Nexus.Web.Host.csproj
    Program.cs
    appsettings.json
    appsettings.Production.json
    Controllers/
      SessionController.cs         # POST /api/v1/session/login|logout|refresh
      MeController.cs              # GET /api/v1/me
    Proxy/
      ReverseProxyMiddleware.cs    # Forwards /api/v1/* to correct downstream service
      ServiceRouter.cs             # Route table: path prefix → service base URL
    Auth/
      SessionCookieHandler.cs      # Injects Bearer token from server-side session store
      TokenStore.cs                # IMemoryCache / IDistributedCache backed token store
    Dockerfile
    .dockerignore
```

---

## Technology Stack

| Layer | Package | Purpose |
|---|---|---|
| Blazor WASM | `Microsoft.AspNetCore.Components.WebAssembly` 10.x | Client SPA runtime |
| Host | `Microsoft.AspNetCore.App` 10.x | Server; cookie auth; reverse proxy |
| Tailwind CSS | Tailwind CSS v3 (via npm + build step or CDN in dev) | All styling — matches mockup token-for-token |
| Icons | Google Material Symbols Outlined (CDN) | Matches mockup icon set exactly |
| Fonts | Google Fonts: Roboto + JetBrains Mono | Matches mockup typography |
| Auth | `Microsoft.AspNetCore.Authentication.Cookies` | HttpOnly session cookie on Host |
| HTTP | `System.Net.Http.Json` | JSON HttpClient extensions in WASM |
| Caching | `Microsoft.Extensions.Caching.Memory` | Server-side token store in Host |
| Container | Docker + multi-stage build | nginx serves WASM static files; Kestrel runs Host |

---

## Tailwind CSS Token Mapping

All DESIGN.md tokens mapped to `tailwind.config.js` (mirrors every mockup's inline config):

```js
colors: {
  primary: '#3525cd',
  'primary-container': '#4f46e5',
  secondary: '#006591',
  surface: '#f9f9ff',
  'surface-card': '#FFFFFF',
  'surface-bg': '#F3F4F6',
  'on-surface': '#151c27',
  'on-surface-variant': '#464555',
  outline: '#777587',
  'outline-variant': '#c7c4d8',
  'status-active': '#10B981',
  'status-pending': '#F59E0B',
  'status-error': '#EF4444',
  'status-verified': '#6366F1',
  'status-archived': '#374151',
  error: '#ba1a1a',
  // ... full token list from DESIGN.md
},
fontFamily: {
  sans: ['Roboto', 'sans-serif'],
  mono: ['JetBrains Mono', 'monospace'],
},
borderRadius: {
  sm: '0.25rem',
  DEFAULT: '0.5rem',
  md: '0.75rem',
  lg: '1rem',
  xl: '1.5rem',
  full: '9999px',
},
```

---

## BFF Proxy — Route Table (embedded in Nexus.Web.Host)

| Incoming path prefix | Downstream service | Port |
|---|---|---|
| `/api/v1/session/*` | Handled locally by SessionController | — |
| `/api/v1/me` | Handled locally by MeController | — |
| `/api/v1/admins/*` | Identity Service | 5001 |
| `/api/v1/patients/*` | Patient Service | 5002 |
| `/api/v1/doctors/*` | Doctor Service | 5003 |
| `/api/v1/branches/*` | Branch Service | 5004 |
| `/api/v1/audit/*` | Audit Service | 5005 |

The `ReverseProxyMiddleware` extracts the JWT from the server-side `TokenStore` (keyed by session cookie), injects it as `Authorization: Bearer <token>` on the outbound request to the downstream service, and streams the response back to the WASM client. The browser never sees a raw JWT.

---

## Phased Implementation Strategy

### Phase 1 — Foundation: Project Scaffold + Auth + Shell Layout

**Goal:** A running, deployable app with login, logout, session management, and the full sidebar shell. All other pages render as stubs.

**Deliverables:**
- `Nexus.Web.sln` with both projects wired up
- Tailwind CSS build pipeline configured (`tailwind.config.js`, `package.json` build script)
- All DESIGN.md color/font/spacing tokens in `tailwind.config.js`
- `appsettings.json` / `appsettings.Production.json` with downstream service URLs
- `Dockerfile` + `.dockerignore` for `Nexus.Web.Host`
- `README.md` (following microservice README conventions)
- `MainLayout.razor` — fixed 240px sidebar + fluid content area
- `NavMenu.razor` — all nav items with Material Symbols icons
- `Login.razor` — pixel-perfect match to `login_screen_nexus_ha/screen.png`
- `SessionController` — `POST /api/v1/session/login`, `POST /api/v1/session/logout`, `POST /api/v1/session/refresh`
- `MeController` — `GET /api/v1/me`
- `AuthStateProvider` — wires Blazor auth state to session cookie
- `TokenStore` — server-side JWT storage keyed by session ID
- Route guards: unauthenticated users redirected to `/login`
- All non-auth pages render `<EmptyState>` stub with page title

**Screens covered:** `login_screen_nexus_ha`
**API endpoints used:** `POST /api/v1/auth/token` (Identity), `POST /api/v1/auth/refresh`, `POST /api/v1/auth/revoke`

---

### Phase 2 — Branches Module

**Goal:** Full CRUD for branches — the simplest domain, no lifecycle, used as validation reference by later phases.

**Deliverables:**
- `BranchService.cs` with `GET /api/v1/branches`, `POST`, `GET /{id}`, `PATCH /{id}`, `DELETE /{id}`
- `BranchList.razor` — paginated table with search, active/inactive filter, Add button
- `CreateBranchModal.razor` / `EditBranchModal.razor` — inline modal forms
- `ConfirmModal.razor` (shared, built here, reused by all future phases)
- `StatusBadge.razor` (shared, built here — Active/Inactive)
- `DataTable.razor` (shared, built here — reusable paginated table)
- `Toast.razor` — RFC 9457 ProblemDetails error display
- ETag optimistic concurrency on PATCH
- `ReverseProxyMiddleware` wired for `/api/v1/branches/*`

**Screens covered:** `branches_nexus_ha`
**API endpoints used:** All 5 Branch Service endpoints

---

### Phase 3 — Patients Module

**Goal:** Full patient lifecycle — 3-step add wizard, list with filters, activate/archive actions, branch association.

**Deliverables:**
- `PatientService.cs` — all 9 patient endpoints
- `PatientList.razor` — filterable table (status: Draft/Active/Archived, branch filter, search)
- `PatientAdd.razor` — 3-step wizard host with `WizardStepper.razor`
  - `Step1PersonalInfo.razor` — matches `add_patient_personal_info_nexus_ha`
  - `Step2AddressContact.razor` — matches `add_patient_address_contact_nexus_ha`
  - `Step3BranchAssociation.razor` — matches `add_patient_branch_association_nexus_ha`
- Lifecycle action buttons: Activate, Archive (with `ConfirmModal`)
- Branch association panel within patient detail view
- `ReverseProxyMiddleware` wired for `/api/v1/patients/*`

**Screens covered:** `patients_nexus_ha`, `add_patient_personal_info_nexus_ha`, `add_patient_address_contact_nexus_ha`, `add_patient_branch_association_nexus_ha`
**API endpoints used:** All 9 Patient Service endpoints

---

### Phase 4 — Doctors Module

**Goal:** Full doctor lifecycle — 3-step add wizard, 5-step credential verification workflow, document upload, branch association.

**Deliverables:**
- `DoctorService.cs` — all 16 doctor endpoints
- `DoctorList.razor` — filterable table (status: Pending/Verified/Approved/Active/Deactivated, specialty, branch, search)
- `DoctorAdd.razor` — 3-step wizard host
  - `Step1Profile.razor` — matches `add_doctor_profile_nexus_ha`
  - `Step2Credentials.razor` — matches `add_doctor_credentials_nexus_ha` (document upload: PDF/PNG/JPEG, max 10MB)
  - `Step3BranchSetup.razor` — matches `add_doctor_branch_setup_nexus_ha`
- Lifecycle action buttons: Verify, Approve, Activate, Deactivate
- Document review panel: approve/reject individual documents
- `FileUpload.razor` shared component (built here)
- `ReverseProxyMiddleware` wired for `/api/v1/doctors/*`

**Screens covered:** `doctors_nexus_ha`, `add_doctor_profile_nexus_ha`, `add_doctor_credentials_nexus_ha`, `add_doctor_branch_setup_nexus_ha`
**API endpoints used:** All 16 Doctor Service endpoints

---

### Phase 5 — Audit Logs Module

**Goal:** Read-only audit ledger with monospaced before/after JSON viewer and full filter set.

**Deliverables:**
- `AuditService.cs` — `GET /api/v1/audit`, `GET /api/v1/audit/{id}`
- `AuditLog.razor` — filterable audit table (entityType, action, actorId, date range)
- `AuditEntryDetail.razor` — expanded drawer/modal with monospaced before/after JSON diff
- `ReverseProxyMiddleware` wired for `/api/v1/audit/*`

**Screens covered:** `audit_logs_nexus_ha`
**API endpoints used:** 2 Audit Service read endpoints (proxied via Host)

---

### Phase 6 — Settings, Profile & Static Pages

**Goal:** Complete all remaining screens; full application is feature-complete.

**Deliverables:**
- `UserProfile.razor` — matches `user_profile_nexus_ha` (view/edit current admin profile)
- `Settings.razor` — matches `settings_nexus_ha`
- `Preferences.razor` — matches `settings_preferences_nexus_ha`
- `News.razor` — matches `news_announcements_nexus_ha`
- `Support.razor` — matches `support_nexus_ha`
- `TermsOfService.razor` — matches `terms_of_service_nexus_ha`
- `Dashboard.razor` — matches `dashboard_nexus_ha` (summary stats cards wiring to live data)
- `IdentityService.cs` + Admins management (list/create/edit/deactivate admins)
- Final Docker image verification + `docker-compose.yml` entry for the front-end service

**Screens covered:** `dashboard_nexus_ha`, `user_profile_nexus_ha`, `settings_nexus_ha`, `settings_preferences_nexus_ha`, `news_announcements_nexus_ha`, `support_nexus_ha`, `terms_of_service_nexus_ha`

---

## Configuration Files

### `appsettings.json` (Nexus.Web.Host)
```json
{
  "Services": {
    "Identity": "http://localhost:5001",
    "Patients": "http://localhost:5002",
    "Doctors":  "http://localhost:5003",
    "Branches": "http://localhost:5004",
    "Audit":    "http://localhost:5005"
  },
  "Session": {
    "CookieName": "nexus.sid",
    "ExpiryMinutes": 60,
    "SlidingExpiration": true
  },
  "Logging": {
    "LogLevel": { "Default": "Information" }
  }
}
```

### `appsettings.Production.json` (Nexus.Web.Host)
```json
{
  "Services": {
    "Identity": "http://nexus-identity-service:5001",
    "Patients": "http://nexus-patient-service:5002",
    "Doctors":  "http://nexus-doctor-service:5003",
    "Branches": "http://nexus-branch-service:5004",
    "Audit":    "http://nexus-audit-service:5005"
  },
  "Session": {
    "ExpiryMinutes": 480
  }
}
```

---

## Dockerfile (Nexus.Web.Host — multi-stage)

```dockerfile
# Stage 1: Build WASM client
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS client-build
WORKDIR /src
COPY Nexus.Web.Client/ ./Nexus.Web.Client/
COPY Nexus.Web.Host/ ./Nexus.Web.Host/
COPY Nexus.Web.sln ./
RUN dotnet restore Nexus.Web.Host/Nexus.Web.Host.csproj
RUN dotnet publish Nexus.Web.Host/Nexus.Web.Host.csproj -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=client-build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nexus.Web.Host.dll"]
```

---

## Shared Component Design Principles

- **StatusBadge:** `rounded-full px-2 py-0.5 text-[11px] font-bold uppercase` — color pair per status using semantic tokens
- **DataTable:** slot-based columns, built-in pagination (`page`/`size`), sort header click, empty state slot
- **WizardStepper:** step number + label, active/completed/upcoming visual states using primary/outline-variant tokens
- **Toast:** RFC 9457 `ProblemDetails` aware — shows `title` + `detail` + custom `code`; auto-dismiss after 5s
- **PageHeader:** `display-lg` title (30px/700), optional subtitle, right-aligned action button slot
- **ConfirmModal:** accessible dialog with primary + ghost button pair

---

## Screen → Phase Traceability Matrix

| Screen folder | Phase |
|---|---|
| `login_screen_nexus_ha` | Phase 1 |
| `dashboard_nexus_ha` | Phase 6 (live data wiring) |
| `branches_nexus_ha` | Phase 2 |
| `patients_nexus_ha` | Phase 3 |
| `add_patient_personal_info_nexus_ha` | Phase 3 |
| `add_patient_address_contact_nexus_ha` | Phase 3 |
| `add_patient_branch_association_nexus_ha` | Phase 3 |
| `doctors_nexus_ha` | Phase 4 |
| `add_doctor_profile_nexus_ha` | Phase 4 |
| `add_doctor_credentials_nexus_ha` | Phase 4 |
| `add_doctor_branch_setup_nexus_ha` | Phase 4 |
| `audit_logs_nexus_ha` | Phase 5 |
| `user_profile_nexus_ha` | Phase 6 |
| `settings_nexus_ha` | Phase 6 |
| `settings_preferences_nexus_ha` | Phase 6 |
| `news_announcements_nexus_ha` | Phase 6 |
| `support_nexus_ha` | Phase 6 |
| `terms_of_service_nexus_ha` | Phase 6 |
