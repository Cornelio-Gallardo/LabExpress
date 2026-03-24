# Dx7 — Clinical Lab Results Platform

> **"Dx7 is a lens, not a ledger. It shows what the data allows and nothing more."**

A multi-tenant SaaS platform for dialysis clinics to receive, view, and track laboratory results.
Dx7 ingests HL7 v2 messages from the lab information system, normalises them through a canonical data model, and surfaces them to clinical staff through a role-gated web interface.

---

## Table of Contents

- [Features](#features)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
  - [Docker Compose](#docker-compose-recommended)
  - [Manual Setup](#manual-setup)
- [Demo Accounts](#demo-accounts)
- [Configuration](#configuration)
- [Architecture](#architecture)
  - [Multi-Tenancy](#multi-tenancy)
  - [Authentication](#authentication)
  - [HL7 Ingestion Pipeline](#hl7-ingestion-pipeline)
  - [CDM Data Model](#cdm-data-model)
- [API Reference](#api-reference)
- [Frontend Pages](#frontend-pages)
- [Database Migrations](#database-migrations)
- [Running Tests](#running-tests)
- [Deployment](#deployment)

---

## Features

| Area | Capability |
|------|-----------|
| **Multi-tenant** | Tenant-per-lab, client-per-clinic; all data isolated by PostgreSQL RLS + EF Core query filters |
| **HL7 Ingestion** | File-watcher monitors `HL7Inbox/`; parses `ORM^O01` and `ORU^R01`; quarantines unmapped codes for review |
| **Results Dashboard** | Current-shift results grouped by test panel (CBC, Chemistry, Lipid…) with freshness indicators |
| **Longitudinal Trends** | 6-month per-analyte history charts per patient |
| **Session Management** | Daily dialysis sessions, 4 shifts, up to 20 chairs; chair reassignments tracked with a full audit trail |
| **MD Notes** | Physician notes attached to sessions with a 24-hour edit window |
| **Role-Based Access** | 6 roles: `sysad`, `pl_admin`, `clinic_admin`, `charge_nurse`, `shift_nurse`, `md` |
| **Export** | CSV, JSON, and PDF (session summaries, URR/Kt·V adequacy reports) |
| **SSO** | Google and Facebook OAuth 2.0 |
| **Security** | JWT stored in `httpOnly` cookies — never exposed to JavaScript; CSRF-resistant via `SameSite=Lax` |

---

## Tech Stack

**Backend**
- [.NET 8](https://dotnet.microsoft.com/) — ASP.NET Core Web API
- [Entity Framework Core 8](https://learn.microsoft.com/en-us/ef/core/) + Npgsql
- [PostgreSQL 16](https://www.postgresql.org/) with Row-Level Security (RLS)
- JWT Bearer authentication (httpOnly cookie transport)
- Swashbuckle / OpenAPI (Swagger UI at `/swagger`)
- iText7 (PDF), CsvHelper (CSV), BCrypt.Net-Next

**Frontend**
- [Vue 3](https://vuejs.org/) — Composition API
- [Vue Router 4](https://router.vuejs.org/) + [Pinia](https://pinia.vuejs.org/) (auth store)
- [Axios](https://axios-http.com/) with credential interceptors
- [Vite 5](https://vitejs.dev/)

**Infrastructure**
- Docker + Docker Compose (3-service stack: `db`, `api`, `frontend`)
- Nginx (static serving + API reverse-proxy in production)

---

## Project Structure

```
dx7/
├── backend/
│   ├── Dx7Api/                         # .NET 8 Web API
│   │   ├── Controllers/                # 13 API controllers
│   │   ├── Data/                       # AppDbContext, DbSeeder, TenantRlsInterceptor
│   │   │   └── TenantAmbient.cs        # AsyncLocal tenant context for background services
│   │   ├── DTOs/                       # Request / response records
│   │   ├── Middleware/                 # TenantMiddleware (JWT → tenant context)
│   │   ├── Migrations/                 # 11 EF Core migrations
│   │   ├── Models/                     # Domain entities
│   │   ├── Services/
│   │   │   ├── Hl7FileWatcherService.cs  # Background HL7 file watcher
│   │   │   └── Hl7/                    # Parser, Processor, Crypto
│   │   ├── appsettings.json
│   │   ├── Program.cs                  # DI, middleware, auto-migrate + seed
│   │   └── Dockerfile
│   └── Dx7Api.Tests/                   # xUnit test project (in-memory DB)
├── frontend/
│   ├── src/
│   │   ├── assets/main.css             # Design system (CSS variables)
│   │   ├── components/                 # Shared UI components
│   │   ├── composables/                # useDialog, etc.
│   │   ├── router/index.js             # Route definitions + auth guards
│   │   ├── services/api.js             # Centralised Axios client + all API calls
│   │   ├── store/auth.js               # Pinia auth store (login, roles, SSO)
│   │   └── views/                      # 13 page components
│   ├── Dockerfile
│   ├── nginx.conf
│   └── vite.config.js
├── docker-compose.yml
└── dx7.sln
```

---

## Quick Start

### Docker Compose (recommended)

```bash
git clone <repo-url> dx7
cd dx7
docker-compose up --build
```

| Service | URL |
|---------|-----|
| App (Frontend) | http://localhost:5173 |
| API | http://localhost:5000 |
| Swagger | http://localhost:5000/swagger |
| PostgreSQL | localhost:5432 |

The API **auto-creates the database, runs all migrations, and seeds demo data** on first boot — no manual setup required.

---

### Manual Setup

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download), [Node 20](https://nodejs.org), [PostgreSQL 16](https://www.postgresql.org/download/)

```bash
# 1 — Create the database
psql -U postgres -c "CREATE DATABASE dx7;"

# 2 — Start the API  (auto-migrates and seeds on startup)
cd backend/Dx7Api
dotnet restore
dotnet run
# API:     http://localhost:5000
# Swagger: http://localhost:5000/swagger

# 3 — Start the frontend  (separate terminal)
cd frontend
npm install
npm run dev
# App: http://localhost:5173
```

> **No need to run `dotnet ef database update` manually.** `Program.cs` applies all pending migrations and seeds demo data automatically at startup.

---

## Demo Accounts

Seeded automatically on first startup.

| Role | Email | Password | Access |
|------|-------|----------|--------|
| PL Admin | `pladmin@dx7.local` | `Admin@1234` | All tenants, full admin |
| Clinic Admin | `admin@dx7.local` | `Admin@1234` | User/patient/shift management |
| Charge Nurse | `charge@dx7.local` | `Nurse@1234` | Patient assignment, chair management, export |
| Shift Nurse | `nurse@dx7.local` | `Nurse@1234` | View results and notes |
| Nephrologist | `md@dx7.local` | `Doctor@1234` | View results, write/edit MD notes |

---

## Configuration

All configuration lives in `backend/Dx7Api/appsettings.json`.
In production, override these values via environment variables (e.g. `Jwt__Key=...`).

| Key | Default | Notes |
|-----|---------|-------|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5432;Database=dx7;Username=postgres;Password=postgres` | PostgreSQL connection string |
| `Jwt:Key` | `Dx7-SuperSecretKey-ChangeInProduction-MinLength32Chars!` | **Change in production** — minimum 32 characters |
| `Jwt:Issuer` | `Dx7Api` | |
| `Jwt:Audience` | `Dx7Client` | |
| `Jwt:ExpiryHours` | `8` | Token lifetime (one clinical shift) |
| `Cors:Origins` | `["http://localhost:5173"]` | Allowed frontend origin(s) |
| `Hl7:InboxPath` | `HL7Inbox` | Absolute or relative path to HL7 watch folder |
| `Hl7:AutoCreatePatients` | `false` | Auto-register patients from HL7 PID segment |
| `Hl7Encryption:Key` | *(base64 AES-256-GCM key)* | **Change in production** |
| `Email:SmtpHost` | `smtp.gmail.com` | SMTP host for password-reset emails |
| `Email:Username` | *(empty)* | Set via environment variable / secrets manager |
| `Email:Password` | *(empty)* | Set via environment variable / secrets manager |
| `OAuth:Google:ClientId` | *(provided)* | Google SSO client ID |
| `AppUrl` | `http://localhost:5173` | Used in password-reset email links |

---

## Architecture

### Multi-Tenancy

Every row in every tenant-scoped table carries a `TenantId` UUID.
Isolation is enforced at **two independent layers** — both must pass for any row to be visible:

1. **EF Core query filters** — applied on every LINQ query at the application layer.
   `CurrentTenantId == null` returns **zero rows** (fail-closed by design).

2. **PostgreSQL Row-Level Security (RLS)** — `RESTRICTIVE FOR SELECT` policy on every table.
   A database-level backstop that is independent of application code.

The active tenant ID is extracted from the JWT `tenant_id` claim by `TenantMiddleware` and placed on both the `AppDbContext` instance and `HttpContext.Items`. Background services (e.g. the HL7 file watcher) propagate the tenant ID through an `AsyncLocal<Guid>` (`TenantAmbient`) so the RLS interceptor works correctly without an HTTP context.

### Authentication

JWT tokens are issued at login and stored **exclusively in an `httpOnly`, `SameSite=Lax` cookie** named `dx7_token`.

- The token is **never accessible to JavaScript** — immune to XSS.
- The browser automatically attaches the cookie to every credentialed request.
- The frontend sets `withCredentials: true` on all Axios requests.
- The `Secure` flag is set to `Always` in production and `SameAsRequest` in development.

### HL7 Ingestion Pipeline

```
LIS / HCLab ──► HL7Inbox/{tenant}/*.hl7
                         │
               Hl7FileWatcherService
               (FileSystemWatcher + 30 s safety poll)
                         │
                   Hl7Parser.Parse()
                   (MSH / PID / OBR / OBX / NTE)
                         │
               TenantTestMap lookup    (OBR-4 code)
               TenantAnalyteMap lookup (OBX-3 code)
                         │
           ┌─── Unmapped? ──► error/ quarantine
           │
           └─── Mapped ──► Hl7Processor.ProcessAsync()
                                   │
                     LabOrder → ResultHeader → ResultValue
                     (CDM traceability chain, AES-256-GCM encrypted at rest)
                                   │
                            processed/ + dx7_hl7.log
```

Key properties:
- **Idempotent** — `MessageControlId` (MSH-10) unique constraint prevents duplicate imports
- **Restart-safe** — 30-second poll catches files missed during downtime
- **Quarantine UI** — unmapped files reviewed and reprocessed from `/hl7-inbox` without restarting
- **Tenant-scoped** — each file processed under the correct tenant's RLS context

### CDM Data Model

Every result value is fully traceable back to the raw HL7 source:

```
Hl7Message  (raw AES-256-GCM encrypted payload, SchemaVersion = "DX7_CDM_1.0_A1")
    └── LabOrder        (one per OBR segment — test panel)
            └── ResultHeader    (result group)
                    └── ResultValue    (one per OBX — analyte + value + unit + range)
                                 ↕
                          SxaAnalyte   (canonical analyte definition from SXA catalog)
```

---

## API Reference

All endpoints are prefixed with `/api/`.
Full interactive documentation: **http://localhost:5000/swagger**

### Auth
| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/auth/login` | Email + password login → sets `dx7_token` cookie |
| `POST` | `/api/auth/external` | Google / Facebook SSO → sets `dx7_token` cookie |
| `POST` | `/api/auth/logout` | Clears `dx7_token` cookie |
| `POST` | `/api/auth/forgot-password` | Send password-reset email |
| `POST` | `/api/auth/reset-password` | Submit new password with reset token |

### Patients
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/patients` | Paginated list (`search`, `status`, `sortBy`, `page`, `pageSize`) |
| `GET` | `/api/patients/summary` | Stat card counts (total, ready, stale, no data) |
| `GET` | `/api/patients/:id` | Single patient |
| `POST` | `/api/patients` | Create patient |
| `DELETE` | `/api/patients/:id` | Soft-delete patient |

### Sessions
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/sessions` | Sessions by `date`, `dateFrom`/`dateTo`, or `shift` |
| `GET` | `/api/sessions/last-date` | Most recent session date for a clinic |
| `GET` | `/api/sessions/:id` | Single session |
| `POST` | `/api/sessions` | Create session |
| `POST` | `/api/sessions/bulk` | Bulk-create sessions |
| `PATCH` | `/api/sessions/:id` | Update chair assignment |
| `DELETE` | `/api/sessions/:id` | Delete session |

### Results
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/results/current/:patientId` | Latest value per analyte — dashboard status indicators |
| `GET` | `/api/results/compare/:patientId` | Last N result dates as columns — session view comparison |
| `GET` | `/api/results/by-date/:patientId` | All results grouped by result date |
| `GET` | `/api/results/orders/:patientId` | Full CDM chain: Orders → Headers → Values |
| `GET` | `/api/results/longitudinal/:patientId` | 6-month analyte trend (`?months=6`) |

### Notes
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/notes?sessionId=` | MD notes for a session |
| `POST` | `/api/notes` | Create note (MD role only) |
| `PATCH` | `/api/notes/:id` | Edit note (MD, within 24 h of creation) |

### Export
| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/export` | Export session results as CSV or JSON |
| `GET` | `/api/export/session-pdf` | Session result PDF |
| `POST` | `/api/export/shift-pdf` | Shift summary PDF |
| `GET` | `/api/export/urr` | URR/Kt·V adequacy report |

### Users
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/users` | Paginated list (`search`, `role`, `status`, `page`, `pageSize`) |
| `GET` | `/api/users/me` | Current user profile |
| `POST` | `/api/users` | Create user |
| `PATCH` | `/api/users/:id` | Update user |
| `PATCH` | `/api/users/:id/activate` | Activate user |
| `PATCH` | `/api/users/:id/deactivate` | Deactivate user |
| `PATCH` | `/api/users/me/password` | Change own password |
| `POST` | `/api/users/:id/avatar` | Upload avatar |
| `DELETE` | `/api/users/:id/avatar` | Remove avatar |

### Clinics
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/clinics` | List clinics |
| `POST` | `/api/clinics` | Create clinic |
| `PATCH` | `/api/clinics/:id` | Update clinic details |
| `PATCH` | `/api/clinics/:id/activate` | Activate clinic |
| `PATCH` | `/api/clinics/:id/deactivate` | Deactivate clinic |
| `PATCH` | `/api/clinics/:id/branding` | Update logo, colours, footer text |

### Shifts
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/shifts` | Shifts for a date |
| `GET` | `/api/shifts/week` | Shifts for a 7-day range |
| `GET` | `/api/shifts/history` | Shift history (30-day default) |
| `POST` | `/api/shifts` | Create shift schedule |
| `POST` | `/api/shifts/bulk` | Bulk-create shifts for a date range |
| `PATCH` | `/api/shifts/:id` | Update shift |
| `DELETE` | `/api/shifts/:id` | Delete shift |
| `POST` | `/api/shifts/:id/nurses` | Assign nurse to shift |
| `DELETE` | `/api/shifts/:id/nurses/:assignmentId` | Remove nurse from shift |

### Tenant & Reference Data
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/tenant` | Current tenant info and branding |
| `PATCH` | `/api/tenant/branding` | Update tenant branding |
| `GET` | `/api/tenant/sxa-tests` | SXA test catalog |
| `GET` | `/api/tenant/sxa-analytes` | SXA analyte catalog |
| `GET` | `/api/tenant/test-maps` | OBR-4 → SXA test code mappings |
| `POST` | `/api/tenant/test-maps` | Add test mapping |
| `DELETE` | `/api/tenant/test-maps/:id` | Remove test mapping |
| `GET` | `/api/tenant/analyte-maps` | OBX-3 → analyte code mappings |
| `POST` | `/api/tenant/analyte-maps` | Add analyte mapping |
| `DELETE` | `/api/tenant/analyte-maps/:id` | Remove analyte mapping |
| `GET` | `/api/roles` | Available roles (dropdown data) |
| `GET` | `/api/refdata?category=` | Reference data by category |

### HL7 Inbox
| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/hl7/inbox/status` | Pending / processed / errored file counts |
| `GET` | `/api/hl7/log` | Recent processing log entries |
| `DELETE` | `/api/hl7/log` | Clear all log entries |
| `DELETE` | `/api/hl7/log/:index` | Remove single log entry |
| `GET` | `/api/hl7/log/read` | Read raw HL7 file from a log entry |
| `POST` | `/api/hl7/upload` | Upload one or more `.hl7` files via multipart |
| `POST` | `/api/hl7/message` | Submit raw HL7 message as `text/plain` |
| `GET` | `/api/hl7/quarantine` | List quarantined files |
| `GET` | `/api/hl7/quarantine/read` | Read raw + parsed content + unmapped codes |
| `POST` | `/api/hl7/quarantine/reprocess` | Reprocess single quarantined file |
| `POST` | `/api/hl7/quarantine/reprocess-all` | Batch reprocess all quarantined files |
| `DELETE` | `/api/hl7/quarantine` | Permanently delete a quarantined file |

---

## Frontend Pages

| Route | Page | Minimum Role |
|-------|------|-------------|
| `/login` | Login (email + SSO) | Public |
| `/forgot-password` | Password reset request | Public |
| `/dashboard` | Top-50 patients — today's results | Any |
| `/roster` | Search & assign patients to current shift | Charge Nurse |
| `/session/:id` | Session detail — results, notes, export | Any |
| `/patients` | Patient management (server-side paginated) | Clinic Admin |
| `/users` | User management (server-side paginated) | Clinic Admin |
| `/clinics` | Clinic management & white-label branding | PL Admin |
| `/hl7-inbox` | HL7 inbox status, quarantine, reprocess | Clinic Admin |
| `/longitudinal/:patientId` | 6-month analyte trend charts | Any |
| `/settings` | Tenant settings, HL7 code mappings | Clinic Admin |

---

## Database Migrations

Migrations run automatically at startup. To apply manually:

```bash
cd backend/Dx7Api
dotnet tool install --global dotnet-ef  # one-time
dotnet ef database update
```

| Migration | Description |
|-----------|-------------|
| `InitialCreate` | Core schema — tenants, clients, users, patients, sessions, results |
| `ShiftManagement` | Shift schedules and nurse assignments |
| `MultiTenantHardening` | `TenantId` on all tables, unique indexes |
| `RoleDefinitions` | `RoleDefinitions` table |
| `AddPhilhealthNo` | Philhealth number field on patients |
| `AddPhilhealthAndRoles` | Additional role seed entries |
| `AddResultStatus` | Result status field |
| `AddCdmSchema` | HL7 archive table, CDM order/header/value layer |
| `EnableRls` | PostgreSQL Row-Level Security on all tenant-scoped tables |
| `PatchF12Mappings` | Add missing HCLab CREA (OBR-4) and BUN (OBX-3) code mappings |
| `AddChairAuditTenantId` | `TenantId` column + RLS policies on `ChairAudits` table |

---

## Running Tests

Tests use an **EF Core in-memory database** — no PostgreSQL required.

```bash
# API stopped
dotnet test backend/Dx7Api.Tests

# API running (avoids the locked .exe in Debug output)
dotnet test backend/Dx7Api.Tests --configuration Release
```

### Test Coverage

| Test | Validates |
|------|-----------|
| `UpdateChair_Saves_ChairAudit_With_Correct_TenantId` | Chair audit record carries the authenticated tenant ID (not `Guid.Empty`) |
| `UpdateChair_Updates_Session_Chair_Value` | `Session.Chair` is updated in the same transaction |
| `ChairAudit_QueryFilter_Returns_Only_CurrentTenant_Records` | EF query filter isolates `ChairAudit` rows by tenant |
| `ChairAudit_QueryFilter_Returns_Empty_When_No_Tenant_FailClosed` | No tenant context → zero rows (fail-closed) |
| `UpdateChair_Returns_NotFound_When_Session_Belongs_To_Different_Tenant` | Cross-tenant session access returns 404, no audit record created |
| `UpdateChair_Returns_Forbid_For_Insufficient_Role` | Roles below `charge_nurse` receive 403 |

---

## Deployment

### Production Environment Variables

Override these at runtime — **do not commit secrets to source control**.

```bash
ConnectionStrings__DefaultConnection="Host=db;Database=dx7;Username=postgres;Password=<secret>"
Jwt__Key="<random-string-minimum-32-characters>"
Hl7Encryption__Key="<base64-encoded-aes256-key>"
Hl7__InboxPath="/hl7inbox"
Cors__Origins__0="https://your-domain.com"
AppUrl="https://your-domain.com"
Email__Username="your-smtp-user"
Email__Password="your-smtp-password"
```

### Pre-Deploy Checklist

- [ ] Replace all default secrets (`Jwt:Key`, `Hl7Encryption:Key`, database password)
- [ ] Set `Cors:Origins` to your production frontend domain
- [ ] Configure SMTP credentials for password-reset emails
- [ ] Mount a persistent Docker volume for `HL7Inbox/` (defined as `hl7inbox` in `docker-compose.yml`)
- [ ] Mount a persistent Docker volume for PostgreSQL data (defined as `pgdata`)
- [ ] Serve over HTTPS — the cookie `Secure` flag is automatically enforced outside Development

### Cookie Security Behaviour

| Environment | `Secure` flag | `SameSite` |
|-------------|---------------|------------|
| `Development` | `SameAsRequest` (allows HTTP) | `Lax` |
| `Production` | `Always` (HTTPS required) | `Lax` |

---

## Key Files

| File | Purpose |
|------|---------|
| `backend/Dx7Api/Program.cs` | App bootstrap, DI, CORS, JWT, auto-migrate, SXA catalog seed |
| `backend/Dx7Api/Data/AppDbContext.cs` | EF entity mappings, RLS query filters, auto-audit `SaveChangesAsync` |
| `backend/Dx7Api/Data/TenantRlsInterceptor.cs` | Sets `app.current_tenant_id` before every DB command |
| `backend/Dx7Api/Data/TenantAmbient.cs` | `AsyncLocal` tenant propagation for background services |
| `backend/Dx7Api/Services/Hl7FileWatcherService.cs` | Background HL7 file monitoring + archival |
| `backend/Dx7Api/Services/Hl7/Hl7Processor.cs` | Core ingestion pipeline: parse → map → store |
| `backend/Dx7Api/appsettings.json` | All configuration keys |
| `frontend/src/services/api.js` | Centralised Axios client — all API calls |
| `frontend/src/store/auth.js` | Login state, role permission getters, SSO flow |
| `frontend/src/router/index.js` | Route definitions and auth guards |
| `frontend/src/assets/main.css` | Full design system (CSS custom properties) |
| `docker-compose.yml` | Local development orchestration (db, api, frontend) |

---

*LabExpress Dx7 — Private. All rights reserved.*
