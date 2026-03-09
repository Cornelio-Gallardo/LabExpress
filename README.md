# Dx7 — Dialysis Clinical Information System

> **Doctrine:** Dx7 presents lab data as-is from the laboratory source. No color-coded risk. No interpretation labels. No risk stratification. The nephrologist judges. Dx7 presents.

A multi-tenant Clinical Information System (CIS) built for Philippine dialysis centers. Dx7 manages the full shift workflow — from patient roster assignment to lab result review to MD documentation — with HL7 v2.x integration for automated result ingestion from LIS systems.

Author : Cornelio S. Gallardo
---

## Table of Contents

- [Overview](#overview)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Multi-Tenancy Model](#multi-tenancy-model)
- [Role Permissions Matrix](#role-permissions-matrix)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
  - [Docker (Recommended)](#docker-recommended)
  - [Manual Setup](#manual-setup)
- [Configuration](#configuration)
- [Database](#database)
  - [Schema Overview](#schema-overview)
  - [Seeded Test Data](#seeded-test-data)
- [API Reference](#api-reference)
- [Frontend Views](#frontend-views)
- [HL7 Integration](#hl7-integration)
- [Authentication & JWT](#authentication--jwt)
- [Shift Workflow](#shift-workflow)
- [Deployment](#deployment)

---

## Overview

Dx7 is designed around the daily dialysis shift cycle. A typical day looks like:

1. **Clinic admin / pl_admin** creates shift schedules for the day (Shifts 1–4)
2. **Charge nurse** selects a shift, searches/ticks patients from the roster, assigns chairs
3. **Shift nurses and MDs** view each patient's session, reviewing lab results
4. **MD (nephrologist)** writes session notes per patient
5. **Charge nurse / clinic admin** exports or prints session reports
6. Shifts are date-scoped — each new day starts fresh

Results are ingested automatically via HL7 file drop or REST API and displayed exactly as received from the source laboratory — no flags, colors, or risk labels are added by Dx7.

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | .NET 8 (ASP.NET Core Web API) |
| **Database** | PostgreSQL 16 via EF Core 8 + Npgsql |
| **Frontend** | Vue 3 + Vite 5 |
| **State** | Pinia |
| **HTTP Client** | Axios |
| **Auth** | JWT Bearer (HS256, 8hr expiry) |
| **Password Hashing** | BCrypt.Net-Next |
| **Export** | iText7 (PDF), CsvHelper (CSV) |
| **API Docs** | Swagger / OpenAPI via Swashbuckle |
| **Containerization** | Docker + Docker Compose |
| **Reverse Proxy** | Nginx (frontend container) |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Browser                              │
│              Vue 3 SPA  (port 5173 / nginx)                 │
└─────────────────────┬───────────────────────────────────────┘
                      │  HTTP/JSON (Axios)
                      ▼
┌─────────────────────────────────────────────────────────────┐
│              .NET 8 Web API  (port 5000)                    │
│                                                             │
│  ┌──────────────────┐   ┌────────────────────────────────┐  │
│  │  TenantMiddleware │   │  JWT Bearer Auth               │  │
│  │  (sets DbContext  │   │  (validates + extracts claims) │  │
│  │   tenant filter)  │   └────────────────────────────────┘  │
│  └──────────────────┘                                       │
│                                                             │
│  Controllers: Auth, Patients, Sessions, Results,           │
│               Shifts, Notes, Export, HL7, Users,           │
│               Clinics, Roles                               │
│                                                             │
│  Services: JwtService, EmailService,                       │
│            Hl7Parser, Hl7Processor,                        │
│            Hl7FileWatcherService (BackgroundService)       │
└─────────────────────┬───────────────────────────────────────┘
                      │  EF Core + Npgsql
                      ▼
┌─────────────────────────────────────────────────────────────┐
│              PostgreSQL 16  (port 5432)                     │
│              Volume: pgdata                                 │
└─────────────────────────────────────────────────────────────┘

          ┌──────────────────────────────────┐
          │  HL7 File Drop (local filesystem) │
          │  HL7Inbox/{tenantId}/*.hl7        │
          │  Watched by FileWatcherService    │
          └──────────────────────────────────┘
```

---

## Multi-Tenancy Model

Dx7 uses a **4-layer multi-tenancy model**:

```
Tenant (e.g. "Philippine Labs Corp")
  └── Client (e.g. "QC Dialysis Center", "Makati Branch")
        └── Users  (scoped to one client, or null for cross-clinic)
        └── Patients
        └── Sessions
        └── ShiftSchedules
```

**Tenant isolation** is enforced at two levels:
1. **EF Core Global Query Filters** — `TenantMiddleware` extracts `tenant_id` from the JWT and sets `db.CurrentTenantId`, which is applied as a WHERE clause on every query automatically.
2. **Controller-level** — Every query explicitly includes `.Where(x => x.TenantId == TenantId)` as defense-in-depth.

**Client (Clinic) isolation** is enforced at the controller level via the `ClientId` JWT claim. Users with `ClientId = null` (pl_admin, sysad) can access all clinics under their tenant and must explicitly pass a `clientId` parameter.

---

## Role Permissions Matrix

| Permission | `sysad` | `pl_admin` | `clinic_admin` | `charge_nurse` | `shift_nurse` | `md` |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| Select shift | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Add patients to shift | — | — | ✓ | ✓ | — | — |
| Assign / edit chairs | — | — | ✓ | ✓ | — | — |
| Remove patients from shift | — | — | ✓ | ✓ | — | — |
| View lab results | — | — | — | ✓ | ✓ | ✓ |
| Write MD notes | — | — | — | — | — | ✓ |
| View MD notes | — | — | — | ✓ | ✓ | ✓ |
| Export CSV / PDF | — | ✓ | ✓ | ✓ | — | — |
| Print session report | — | ✓ | ✓ | ✓ | — | ✓ |
| Manage shift schedules | — | ✓ | ✓ | ✓ | — | — |
| Manage users | — | ✓ | ✓ | — | — | — |
| Manage clinics | — | ✓ | — | — | — | — |
| View HL7 inbox | — | ✓ | ✓ | — | — | — |

> Role labels are DB-driven per tenant (see `RoleDefinitions` table). A tenant can rename `md` to `Nephrologist` or `charge_nurse` to `Head Nurse` without code changes.

---

## Project Structure

```
dx7/
├── docker-compose.yml
├── backend/
│   └── Dx7Api/
│       ├── Controllers/
│       │   ├── TenantBaseController.cs   # Base class: CurrentUserId, TenantId, ClientId, role helpers
│       │   ├── AuthController.cs         # POST /auth/login, forgot-password, reset-password
│       │   ├── PatientsController.cs     # CRUD patients, result status summary
│       │   ├── SessionsController.cs     # Create/view/delete shift sessions, update chairs
│       │   ├── ResultsController.cs      # GET current results, history, by-date grouped
│       │   ├── ShiftsController.cs       # Shift schedule CRUD, bulk create, nurse assignment
│       │   ├── NotesController.cs        # MD note create/edit per session
│       │   ├── ExportController.cs       # CSV + PDF export
│       │   ├── Hl7Controller.cs          # Manual HL7 message POST + inbox management
│       │   ├── UsersController.cs        # User CRUD, change password
│       │   ├── ClinicsController.cs      # Clinic (Client) CRUD
│       │   └── RolesController.cs        # Role label management
│       ├── Data/
│       │   ├── AppDbContext.cs           # EF Core context, global query filters, indexes
│       │   └── DbSeeder.cs              # Seeds tenant, clinic, users, patients, results on first run
│       ├── DTOs/
│       │   └── DTOs.cs                  # All request/response records
│       ├── Middleware/
│       │   └── TenantMiddleware.cs       # Extracts tenant_id from JWT → sets DbContext filter
│       ├── Models/
│       │   └── Models.cs                # All EF Core entity classes
│       ├── Services/
│       │   ├── JwtService.cs            # Token generation with tenant/client/role claims
│       │   ├── EmailService.cs          # SMTP email for password reset
│       │   ├── Hl7FileWatcherService.cs # BackgroundService: watches HL7Inbox folder
│       │   └── Hl7/
│       │       ├── Hl7Parser.cs         # Parses HL7 v2.x segments (MSH, PID, OBR, OBX)
│       │       └── Hl7Processor.cs      # Processes parsed messages → DB inserts/updates
│       ├── appsettings.json
│       ├── Dx7Api.csproj
│       └── Dockerfile
└── frontend/
    ├── src/
    │   ├── views/
    │   │   ├── LoginView.vue             # Login + forgot password trigger
    │   │   ├── ResetPasswordView.vue     # Password reset via email token
    │   │   ├── ShiftSelectView.vue       # Main landing: select shift, view/manage roster
    │   │   ├── PatientRosterView.vue     # Add patients to shift (search + tick)
    │   │   ├── SessionView.vue           # Patient results + MD notes for a session
    │   │   ├── PatientsView.vue          # Patient master list + result report modal
    │   │   ├── ShiftManagementView.vue   # Admin: create/manage shift schedules
    │   │   ├── UsersView.vue             # User management
    │   │   ├── ClinicsView.vue           # Clinic management (pl_admin)
    │   │   └── Hl7InboxView.vue          # HL7 message inbox and processing status
    │   ├── components/
    │   │   └── ResultReportModal.vue     # Date-tabbed result report popup with PDF export
    │   ├── services/
    │   │   └── api.js                   # Axios instance + all API service methods
    │   ├── store/
    │   │   └── auth.js                  # Pinia store: user, token, role getters, login/logout
    │   ├── router/
    │   │   └── index.js                 # Vue Router routes + auth guard
    │   ├── assets/
    │   │   └── main.css                 # Global CSS design system (SanteXA pattern)
    │   └── App.vue                      # Shell: sidebar navigation + router-view
    ├── nginx.conf
    ├── Dockerfile
    └── package.json
```

---

## Getting Started

### Docker (Recommended)

The fastest way to run the full stack:

```bash
git clone https://github.com/your-org/dx7.git
cd dx7
docker compose up --build
```

Services start on:
- **Frontend** → http://localhost:5173
- **API** → http://localhost:5000
- **Swagger** → http://localhost:5000/swagger
- **PostgreSQL** → localhost:5432

On first start, the API auto-migrates the database and seeds default data (see [Seeded Test Data](#seeded-test-data)).

### Manual Setup

**Prerequisites:** .NET 8 SDK, Node.js 18+, PostgreSQL 16

**Backend:**
```bash
cd backend/Dx7Api
# Update connection string in appsettings.json
dotnet run
# API starts on http://localhost:5000
# Database is auto-migrated and seeded on first run
```

**Frontend:**
```bash
cd frontend
npm install
npm run dev
# Dev server starts on http://localhost:5173
```

---

## Configuration

All configuration lives in `backend/Dx7Api/appsettings.json`. For Docker, environment variables override these via `docker-compose.yml`.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dx7;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Key": "CHANGE-THIS-IN-PRODUCTION-min-32-chars",
    "Issuer": "Dx7Api",
    "Audience": "Dx7Client",
    "ExpiryHours": 8
  },
  "Cors": {
    "Origins": ["http://localhost:5173"]
  },
  "Hl7": {
    "InboxPath": "HL7Inbox",
    "AutoCreatePatients": false,
    "WatchIntervalSeconds": 30
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "UseSsl": true,
    "FromAddress": "noreply@dx7.local",
    "FromName": "Dx7 Clinical System",
    "Username": "",
    "Password": ""
  },
  "AppUrl": "http://localhost:5173"
}
```

> ⚠️ **Production checklist:**
> - Change `Jwt:Key` to a strong random string (32+ chars). A changed key invalidates all active sessions.
> - Set real SMTP credentials for password reset emails.
> - Update `Cors:Origins` to your production frontend URL.
> - Use environment variables or a secrets manager — never commit real credentials.

---

## Database

### Schema Overview

Dx7 uses 11 tables. Full DBML schema (importable at [dbdiagram.io](https://dbdiagram.io)) is in `docs/dx7_schema.dbml`.

```
Tenants
  └── Clients
        ├── Users
        ├── Patients
        │     └── Results
        ├── Sessions
        │     ├── MdNotes
        │     └── ChairAudits
        ├── ShiftSchedules
        │     └── ShiftNurseAssignments
        └── RoleDefinitions
```

**Key constraints:**
- `Patients`: UNIQUE `(TenantId, ClientId, LisPatientId)` — prevents duplicate LIS patient IDs per clinic
- `Sessions`: UNIQUE `(PatientId, SessionDate, ShiftNumber)` — one patient per shift per day
- `Results`: INDEX `(TenantId, PatientId, TestCode, ResultDate)` — fast per-patient result queries
- All FK relationships with `AssignedBy`/`ChangedBy`/`MdUserId` use `ON DELETE RESTRICT` to preserve audit trails

### Seeded Test Data

On first run, `DbSeeder` creates:

| Type | Details |
|---|---|
| Tenant | LABExpress |
| Clinic | Metro Dialysis Center (MDC) |
| Users | See table below |
| Patients | 8 Filipino patients with LIS IDs and PhilHealth numbers |
| Lab Results | 272 seeded results across multiple dates and panels |
| Role Definitions | All 5 roles with Filipino clinical context labels |

**Default login credentials:**

| Email | Password | Role |
|---|---|---|
| `admin@dx7.local` | `Admin@1234` | clinic_admin |
| `charge@dx7.local` | `Nurse@1234` | charge_nurse |
| `nurse@dx7.local` | `Nurse@1234` | shift_nurse |
| `md@dx7.local` | `Doctor@1234` | md (Nephrologist) |
| `pladmin@dx7.local` | `Admin@1234` | pl_admin |

> The `pladmin` user has `ClientId = null` by design — this gives cross-clinic access. All other users are scoped to Metro Dialysis Center.

---

## API Reference

All endpoints require `Authorization: Bearer {token}` except `/api/auth/login` and the password reset endpoints.

Full interactive docs available at `/swagger` when running.

### Auth — `/api/auth`

| Method | Endpoint | Body | Description |
|---|---|---|---|
| POST | `/login` | `{ email, password }` | Returns JWT + user/tenant/client context |
| POST | `/forgot-password` | `{ email }` | Sends password reset email |
| POST | `/reset-password` | `{ email, token, newPassword }` | Resets password via token |

### Sessions — `/api/sessions`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/` | All clinical | List sessions. Params: `?date=&shift=&clientId=` |
| GET | `/{id}` | All clinical | Get single session by ID |
| POST | `/` | charge_nurse + | Create session (add patient to shift). Body: `{ patientId, sessionDate, shiftNumber, chair?, clientId? }` |
| POST | `/bulk` | charge_nurse + | Bulk add patients. Body: `{ patientIds[], sessionDate, shiftNumber, clientId? }` |
| PATCH | `/{id}` | charge_nurse + | Update chair assignment. Body: `{ chair }` |
| DELETE | `/{id}` | charge_nurse + | Remove patient from shift |

### Results — `/api/results`

| Method | Endpoint | Description |
|---|---|---|
| GET | `/current/{patientId}` | Latest result per test code (most recent date per test) |
| GET | `/history/{patientId}/{testCode}` | All historical results for a patient+test. Params: `?from=&to=` |
| GET | `/by-date/{patientId}` | All results grouped by result date (used by date-tab report modal) |
| POST | `/` | Manually ingest a result (pl_admin / sysad only) |

### Shifts — `/api/shifts`

| Method | Endpoint | Description |
|---|---|---|
| GET | `/` | Shift schedules for a date. Params: `?date=&clientId=` |
| GET | `/week` | Shifts for a 7-day range. Params: `?from=&clientId=` |
| GET | `/history` | Historical shifts. Params: `?from=&to=&clientId=` |
| POST | `/` | Create single shift schedule |
| POST | `/bulk` | Bulk create all 4 shifts for a date range |
| PATCH | `/{id}` | Update shift (label, times, capacity) |
| DELETE | `/{id}` | Delete shift schedule |
| POST | `/{id}/nurses` | Assign a nurse to a shift |
| DELETE | `/{id}/nurses/{assignmentId}` | Remove nurse from shift |

### Other Endpoints

| Resource | Base Path | Notes |
|---|---|---|
| Patients | `/api/patients` | CRUD + result status summary per patient |
| MD Notes | `/api/notes` | Notes per session. MD can edit within 24hr. |
| Export | `/api/export` | POST `{ patientIds[], fromDate, toDate, testCodes?, format }` — returns CSV blob or PDF |
| Users | `/api/users` | CRUD. clinic_admin manages their clinic; pl_admin manages all. |
| Clinics | `/api/clinics` | CRUD. pl_admin only. |
| Roles | `/api/roles` | GET/PATCH role labels per tenant. |
| HL7 | `/api/hl7` | GET inbox, POST raw message, DELETE processed message |

---

## Frontend Views

| Route | View | Access |
|---|---|---|
| `/shifts` | `ShiftSelectView` | All roles — main landing page |
| `/roster` | `PatientRosterView` | charge_nurse, clinic_admin, pl_admin |
| `/session/:id` | `SessionView` | All clinical roles |
| `/patients` | `PatientsView` | All roles |
| `/shift-management` | `ShiftManagementView` | charge_nurse, clinic_admin, pl_admin |
| `/users` | `UsersView` | clinic_admin, pl_admin |
| `/clinics` | `ClinicsView` | pl_admin only |
| `/hl7-inbox` | `Hl7InboxView` | clinic_admin, pl_admin |

### Key Components

**`ShiftSelectView`** — Main workflow hub. Shows today's shift cards (loaded from DB, not hardcoded). pl_admin sees a clinic picker. Clicking a shift loads the patient roster. Charge nurse can add patients, assign chairs, remove patients, and open sessions. Duplicate chair assignments are flagged with a red warning.

**`PatientRosterView`** — Add patients to a shift. Full patient list with live search + status filter. Tick checkboxes to select multiple patients, or use the "Add" button per row for quick single adds. Chair can be assigned per-patient at this stage. Patients already in the shift are shown greyed-out. Sticky confirm bar appears when selections are made.

**`SessionView`** — Per-patient session view. Shows Priority Labs panel (K, Phosphorus, HGB) at top, then full results table with all test codes. MD note editor on the right panel. Print/PDF export button. Results are loaded from `/results/current/{patientId}` — they are not FK'd to the session, just filtered by patient.

**`ResultReportModal`** — Date-tabbed popup for viewing all historical results for a patient. Opened from the Patients list. PDF download uses html2canvas + jsPDF with native Save As dialog support.

**`ShiftManagementView`** — Admin view for creating and managing shift schedules. Shows 4 shift cards per day. Supports bulk creation across a date range. Nurse assignment per shift. Week view and history view tabs. pl_admin sees a clinic picker at the top.

---

## HL7 Integration

Dx7 processes HL7 v2.x messages in two ways:

### 1. File Drop (Automated)

`Hl7FileWatcherService` is a .NET `BackgroundService` that watches:
```
HL7Inbox/
  └── {tenantId}/          ← one folder per tenant
        ├── message1.hl7
        └── message2.hl7
```

- Scans on startup, then watches via `FileSystemWatcher`
- Periodic rescan every `WatchIntervalSeconds` (default: 30s) as a safety net
- Processed files are moved to `processed/` subfolder
- Failed files are moved to `failed/` subfolder with error logged

### 2. REST API (Manual / Integration)

```
POST /api/hl7
Content-Type: text/plain
Authorization: Bearer {token}

MSH|^~\&|LIS|LabCorp|Dx7|MDC|20260309120000||ORU^R01|MSG001|P|2.5
PID|1||P001^InstaHMS||DELA CRUZ^Juan||19650312|M
OBR|1|||CBC
OBX|1|NM|HGB^Hemoglobin||8.5|g/dL|13.0-17.0|L|||F
```

### Supported Message Types

| Type | Trigger | Action |
|---|---|---|
| `ORU^R01` | Lab result available | Inserts/updates result values in `Results` table |
| `ORM^O01` | Lab order placed | Creates a `pending` result record (value = null) |

### HL7 Segment Mapping

| Segment | Field | Maps to |
|---|---|---|
| MSH-3 | Sending Application | `Result.SourceLab` |
| MSH-9 | Message Type | Route to order vs result handler |
| MSH-10 | Message Control ID | `Result.SourceMessageId` |
| PID-3 | Patient ID | `Patient.LisPatientId` (used for patient lookup) |
| PID-5 | Patient Name | `LASTNAME^FIRSTNAME` → `Patient.Name` |
| PID-7 | Date of Birth | `Patient.Birthdate` |
| PID-8 | Sex | `Patient.Gender` |
| OBR-7 | Observation Date/Time | `Result.ResultDate` |
| OBR-3 | Filler Order Number | `Result.AccessionId` |
| OBX-3 | Observation Identifier | `Result.TestCode`, `Result.TestName` |
| OBX-5 | Observation Value | `Result.ResultValue` |
| OBX-6 | Units | `Result.ResultUnit` |
| OBX-7 | Reference Range | `Result.ReferenceRange` |
| OBX-8 | Abnormal Flag | `Result.AbnormalFlag` (H/L/HH/LL/A) |
| OBX-11 | Observation Result Status | `Result.ResultStatus` (F=final, P=pending) |

> **Patient matching:** HL7 processor looks up patient by `LisPatientId` (PID-3) within the tenant. If `AutoCreatePatients = true` in config, unmatched patients are auto-created from PID data. Default is `false` — unmatched messages are logged as errors.

---

## Authentication & JWT

JWT tokens are issued on `/api/auth/login` and expire after `Jwt:ExpiryHours` (default: 8 hours).

**Token claims:**

| Claim | Value | Used for |
|---|---|---|
| `sub` (NameIdentifier) | User GUID | `CurrentUserId` in controllers |
| `email` | User email | Display |
| `name` | User full name | Display |
| `role` | Role enum string | Authorization checks |
| `tenant_id` | Tenant GUID | Global query filter isolation |
| `client_id` | Client GUID or empty string | Clinic-scoped data access |

**`client_id` behavior:**
- `clinic_admin`, `charge_nurse`, `shift_nurse`, `md` → have their clinic's GUID
- `pl_admin`, `sysad` → empty string (`""`) — they use the `clientId` query/body parameter to target a specific clinic

**ClientId resolution order** (in controllers that write data):
1. JWT `client_id` claim
2. Request body `clientId` field
3. DB lookup: `Users.ClientId` for the current user

---

## Shift Workflow

The complete nurse-side workflow:

```
Login
  └── /shifts (ShiftSelectView)
        ├── [pl_admin only] Select clinic from dropdown
        ├── View today's shift cards (loaded from ShiftSchedules table)
        ├── Click a shift card → loads patient roster for that shift
        └── [charge_nurse] Click "+ Add Patients"
              └── /roster (PatientRosterView)
                    ├── Search by name or LIS ID
                    ├── Tick patients → assign optional chair per patient
                    ├── "Confirm & Add to Shift" → creates Session records
                    └── Returns to /shifts
        └── [charge_nurse] Click chair cell → inline edit
        └── [charge_nurse] Click "Remove" → deletes Session
        └── Click "View Results"
              └── /session/:id (SessionView)
                    ├── Priority Labs panel (K, Phos, HGB)
                    ├── Full results table (all test codes)
                    └── [md] Write / edit session note
                    └── Print / PDF export
```

**"Fresh shift" behavior:** Sessions are scoped to `(ClientId, SessionDate, ShiftNumber)`. A new day creates an entirely new set of sessions. Previous days' data is preserved and accessible via the shift history.

---

## Deployment

### Docker Production

```bash
# Override secrets via environment variables
docker compose \
  -e "Jwt__Key=your-strong-secret-key-here-min-32-chars" \
  -e "ConnectionStrings__DefaultConnection=Host=your-db;..." \
  -e "Cors__Origins__0=https://your-frontend-domain.com" \
  up -d
```

### Reverse Proxy (Nginx / Caddy)

The frontend Nginx container serves the Vue SPA and proxies `/api` to the backend:

```nginx
# frontend/nginx.conf
location /api {
    proxy_pass http://api:8080;
}
```

For production behind an external reverse proxy, update `Cors:Origins` and set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` if behind a load balancer.

### Database Migrations

EF Core migrations run automatically on startup via `db.Database.MigrateAsync()`. To generate a new migration during development:

```bash
cd backend/Dx7Api
dotnet ef migrations add YourMigrationName
dotnet ef database update
```

---

## Contributing

1. Branch from `main`
2. Follow the existing controller pattern — all controllers extend `TenantBaseController`
3. All new tables must include `TenantId` and be added to the global query filter in `AppDbContext.OnModelCreating`
4. DTOs live in `DTOs/DTOs.cs` — use `record` types
5. Frontend API calls go through `src/services/api.js`
6. Role-based UI gates use Pinia getters from `src/store/auth.js` (e.g. `auth.canManageSession`)

---

## License

Proprietary — All rights reserved.

---

*Dx7 CIS — Built for Philippine dialysis centers*
