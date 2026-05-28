# Architecture Decision Records — Nexus HA

**Hospital Patient & Doctor Registry Platform**

> This document records the significant architecture decisions made while preparing the Nexus HA High-Level Technical Specification (HLD). Each record is **atomic** (one decision), **dated**, and carries a **status**. ADRs are immutable once accepted — a decision that changes is not edited but *superseded* by a new ADR.

| | |
|---|---|
| **Document Title** | Architecture Decision Records — Nexus HA |
| **Prepared By** | Solution Architect |
| **References** | HLD — Nexus HA (v0.1); BRD — Patient & Doctor Registry (v0.1) |
| **Status** | Draft — For Review |
| **Version** | 0.1 |
| **Date** | 27 May 2026 |

---

## Decision Log (Index)

| ADR | Decision | Status |
|---|---|---|
| [ADR-001](#adr-001--adopt-a-microservices-architecture) | Adopt a microservices architecture | Accepted |
| [ADR-002](#adr-002--use-net-10--aspnet-core-web-apis-with-react--react-native-clients) | .NET 10 / ASP.NET Core Web APIs with React + React Native clients | Accepted |
| [ADR-003](#adr-003--use-on-premises-sql-server-with-a-database-per-service-on-a-shared-host) | On-premises SQL Server, database-per-service on a shared host | Accepted |
| [ADR-004](#adr-004--package-services-as-docker-containers) | Package services as Docker containers | Accepted |
| [ADR-005](#adr-005--deploy-on-premises-with-docker-compose-first-then-migrate-to-azure-aks) | Deploy on-prem with Docker Compose first, then migrate to Azure AKS | Accepted |
| [ADR-006](#adr-006--self-hosted-token-based-identity-administrators-only) | Self-hosted, token-based identity (administrators only) | Accepted |
| [ADR-007](#adr-007--no-multi-factor-authentication-in-the-current-phase) | No multi-factor authentication in the current phase | Accepted |
| [ADR-008](#adr-008--defer-the-later-phase-azure-database-hosting-choice) | Defer the later-phase Azure database hosting choice | Proposed (Deferred) |
| [ADR-009](#adr-009--mandate-india-azure-region-and-processor-safeguards-for-cloud-hosted-personal-data) | Mandate India Azure region + processor safeguards for cloud data | Accepted |

**Status legend:** `Proposed` — under consideration · `Accepted` — decided and in force · `Deferred` — deliberately postponed · `Superseded` — replaced by a later ADR.

---

## ADR-001 — Adopt a microservices architecture

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder (Ramkumar), Solution Architect

### Context
The BRD describes a single-hospital, medium-scale registry (tens of thousands of records, two core entities, administrator-only access, no integrations). Phase 1 delivers only a subset of capabilities, with more capabilities planned in later phases. The architecture must accommodate this incremental growth.

### Decision
Build Nexus HA as a set of independently deployable **microservices** (e.g., Identity & Admin, Patients, Doctors, Branches, Audit), rather than a single application.

### Consequences
- **Positive:** New capabilities can be added and released as separate services with reduced risk to existing functionality; supports the phased roadmap.
- **Negative / accepted trade-offs:**
  - Operational overhead is incurred from day one (multiple deployable units to run, monitor, and integrate) before any feature ships — arguably disproportionate to the system's current scale.
  - Cross-cutting rules from the BRD — the *phone-number + role* uniqueness check and the *full audit trail* — are simpler in a single application and require more care when the system is split.
  - **Principal risk:** committing to service boundaries early, while the full future capability set is unknown; re-cutting boundaries across running services later is costly.
- **Mitigation:** Keep Phase-1 services deliberately coarse-grained; revisit and refine boundaries before they harden.

### Alternatives considered
- **Modular monolith** (one deployable, clean internal modules) — lower overhead, simpler cross-cutting logic, and "split later if needed." Consciously set aside in favour of phased-growth flexibility.
- **Plain monolith** — simplest, but offers the least room to grow capability-by-capability.

---

## ADR-002 — Use .NET 10 / ASP.NET Core Web APIs with React + React Native clients

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
The BRD mandates an API-first build, with web and mobile front-ends added later as consumers of those APIs. A capable, well-supported, and consistent technology family is required.

### Decision
Build the back-end services in **C# on .NET 10 (current LTS)** using **ASP.NET Core Web APIs** with **no server-side page rendering**. Build the **web client in React** and the **mobile client in React Native**, both as later-phase consumers of the APIs.

### Consequences
- **Positive:** Single language family for the back end; first-class SQL Server tooling; shared concepts between React and React Native; directly satisfies the API-first requirement.
- **Negative / accepted trade-offs:** None of significance — this is a mainstream, low-risk stack.

### Alternatives considered
- Java/Spring, Node.js, Python — all viable, but .NET pairs most naturally with the hospital's existing SQL Server investment and team direction.

---

## ADR-003 — Use on-premises SQL Server with a database-per-service on a shared host

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
The hospital already owns and is licensed for SQL Server on-premises. The microservices design (ADR-001) calls for data isolation between services. New database spend is to be avoided in the current phase.

### Decision
Store all data in **on-premises SQL Server**, reusing existing licenses, with a **database-per-service**. In the early phases, co-locate these databases on a **single SQL Server host**.

### Consequences
- **Positive:** No new licensing cost; each service's data is cleanly isolated, which is the correct pattern for microservices.
- **Negative / accepted trade-offs:**
  - The shared host is a **single point of failure** — its loss takes down every service's data simultaneously — and services can contend for the same resources ("noisy neighbour").
  - This partially re-couples, at the data tier, the services that ADR-001 spent complexity to decouple.
- **Mitigation / future path:** Acceptable at current scale; move to separate instances and/or high-availability configurations when scale or availability requires. Cloud hosting of the database is addressed in ADR-008 and ADR-009.

### Alternatives considered
- **NoSQL document store** — not justified for this relational, audit-heavy domain.
- **Separate database hosts per service now** — better isolation, but unnecessary cost and operational burden at current scale.

---

## ADR-004 — Package services as Docker containers

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
The platform must run on-premises initially and move to a cloud orchestrator later (ADR-005). A consistent, portable packaging unit is needed across both environments.

### Decision
Package each service as a **Docker container**, distributed as a container **image**.

### Consequences
- **Positive:** The same images run under Docker Compose (on-prem) and Azure Kubernetes Service (cloud), so the future migration is largely a configuration change rather than a rebuild — significantly de-risking that transition.
- **Negative / accepted trade-offs:** None of note; containerisation is the industry-standard approach.

### Alternatives considered
- Self-contained executables / Windows Services or IIS-hosted apps — would not carry forward cleanly to a Kubernetes-based cloud deployment.

---

## ADR-005 — Deploy on-premises with Docker Compose first, then migrate to Azure AKS

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
Early phases favour simplicity and avoiding cloud cost/complexity; later phases anticipate greater scale and operational maturity. Compute and data should remain co-located to avoid latency.

### Decision
In **early phases, run everything on-premises** — service containers and SQL Server together — orchestrated by **Docker Compose**. In a **later phase, move the platform fully into Azure**, with services on **Azure Kubernetes Service (AKS)** and the database also moved into Azure, keeping compute and data co-located.

### Consequences
- **Positive:** Simple, low-cost start; AKS later adds self-healing, rolling upgrades, and scaling. Moving compute and data to the cloud *together* avoids cross-boundary latency.
- **Negative / accepted trade-offs:**
  - The early posture is **not highly available**: Docker Compose on a single host has no automatic recovery, failover, or rolling upgrades — a single point of failure until AKS.
  - A cloud-compute/on-prem-database split is avoided as a steady state, but may exist briefly during the transition window.
- **Mitigation:** Treat the single-host phase as pilot/early-stage; the AKS move resolves availability. The database hosting choice for the Azure phase is deferred (ADR-008) under the residency mandate (ADR-009).

### Alternatives considered
- **On-prem Kubernetes from the start** — more capability, but heavy operational burden too early.
- **Cloud-first from day one** — premature cost and complexity; not aligned with the on-prem licensing position.

---

## ADR-006 — Self-hosted, token-based identity (administrators only)

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
Per the BRD, only administrators/staff use Nexus HA; patients and doctors never log in. The early deployment is on-premises, and full control over identity is preferred.

### Decision
Manage identity with a **self-hosted identity service inside the platform**, using **token-based authentication** (OpenID Connect / OAuth 2.0 with signed JWT access tokens). The BRD's single all-powerful admin role is carried as a *role claim* within the token.

### Consequences
- **Positive:** Full in-house control; works cleanly on-premises; stateless token validation suits microservices (each service verifies tokens independently); the role model can grow to finer-grained roles later without rework.
- **Negative / accepted trade-offs:** The hospital owns the operational responsibility for the identity service (user store, credential handling, key management).

### Alternatives considered
- **Azure Entra ID** — managed identity with built-in SSO/MFA, but introduces a cloud dependency at odds with the on-prem-first phase.
- **Self-hosted now, federate to Entra ID in the Azure phase** — remains a sensible future option (noted in the HLD roadmap), not adopted now.

---

## ADR-007 — No multi-factor authentication in the current phase

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
Administrator accounts hold privileged access to personal healthcare data. MFA strengthens account security but adds setup and user-experience overhead.

### Decision
**Do not require multi-factor authentication** for administrator logins in the current phase.

### Consequences
- **Negative / accepted risk:** Privileged access to personal data without a second factor increases the risk of account compromise via stolen or weak credentials.
- **Mitigation / recommendation:** Accepted for the current phase only; **introducing MFA is recommended before production scale-up**, and is straightforward to add given the self-hosted identity service (ADR-006).

### Alternatives considered
- **Mandatory MFA for all admins** — stronger security posture; deferred rather than adopted now.
- **Optional/configurable MFA** — middle ground; not selected for this phase.

---

## ADR-008 — Defer the later-phase Azure database hosting choice

- **Status:** Proposed (Deferred)
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
When the platform moves to Azure (ADR-005), the database moves with it. Two viable hosting options exist with materially different operational and cost profiles. The decision does not need to be made until the migration phase.

### Decision
**Defer** the choice between the two options below; document both with their trade-offs and decide at the Azure-migration phase.

| Option | Means | Trade-off |
|---|---|---|
| **Azure VM running SQL Server (BYOL)** | Lift-and-shift onto a self-managed cloud server. | Maximum control and straightforward license reuse, but the hospital retains responsibility for patching, backups, and HA. |
| **Azure SQL Managed Instance (Azure Hybrid Benefit)** | Managed database service, highly SQL Server-compatible. | Much lower operational burden, but a managed-service cost/feature model, and Hybrid Benefit license eligibility must be confirmed. |

### Consequences
- **Positive:** Avoids premature commitment; allows the decision to be made with better information at migration time.
- **Negative / accepted trade-offs:** A known open item remains in the architecture until resolved; the migration plan cannot be fully costed until then.

### Alternatives considered
- Committing now to either option — rejected; insufficient information at this stage.

---

## ADR-009 — Mandate India Azure region and processor safeguards for cloud-hosted personal data

- **Status:** Accepted
- **Date:** 27 May 2026
- **Deciders:** Business Stakeholder, Solution Architect

### Context
While data is on-premises, the hospital has maximum control and a clean data-residency position. Moving personal data (patient and doctor PII) to a cloud-hosted database (ADR-005, ADR-008) engages data-residency and data-sovereignty obligations under India's DPDP Act.

### Decision
When the database moves to the cloud, it **must reside in an India Azure region**, with appropriate **data-processor safeguards** and access controls in place. This is a firm requirement, not an option.

### Consequences
- **Positive:** Preserves DPDP compliance and data-sovereignty posture through the cloud migration.
- **Negative / accepted trade-offs:** Constrains Azure region selection and adds contractual/compliance steps (data-processor agreements, residency verification) to the migration.
- **Dependency:** Legal/compliance to confirm the specific processor terms and region before migration proceeds.

### Alternatives considered
- Treating residency as a later/optional concern — rejected; non-compliance risk is unacceptable for personal healthcare data.

---

*Confidential — Draft v0.1 — Nexus HA Architecture Decision Records*
