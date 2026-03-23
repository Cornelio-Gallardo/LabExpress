# Dx7 — LabExpress Lab Results Platform

> **DOCTRINE: Dx7 is a lens, not a ledger. It shows what the data allows and nothing more.**

Full-stack web application built with:
- **Backend**: .NET 8 / ASP.NET Core Web API
- **Frontend**: Vue.js 3 + Pinia + Vue Router
- **Database**: PostgreSQL 16

---

## Project Structure

```
dx7/
├── backend/
│   ├── Dx7Api/
│   │   ├── Controllers/        # Auth, Patients, Sessions, Results, Notes, Export,
│   │   │                       # Users, Clinics, Shifts, Hl7, Tenant, RefData, Roles
│   │   ├── Data/               # AppDbContext, DbSeeder
│   │   ├── DTOs/               # Request/Response DTOs
│   │   ├── Models/             # Tenant, Client, User, Patient, Session, Result,
│   │   │                       # MdNote, ShiftSchedule, Hl7Message, LabOrder,
│   │   │                       # ResultHeader, ResultValue, SxaTestCatalog, SxaAnalyte,
│   │   │                       # TenantTestMap, TenantAnalyteMap
│   │   ├── Services/           # JwtService, Hl7FileWatcherService, Hl7Parser,
│   │   │                       # Hl7Processor, Hl7Crypto
│   │   ├── appsettings.json    # Config (DB connection, JWT, CORS, HL7 inbox path)
│   │   ├── Program.cs          # DI, middleware, auto-migrate + seed, test/analyte mappings
│   │   └── Dockerfile
│   └── init.sql                # Raw SQL (alternative to EF migrations)
├── frontend/
│   ├── src/
│   │   ├── assets/main.css          # Global design system
│   │   ├── services/api.js          # Axios + all API calls
│   │   ├── store/auth.js            # Pinia auth store
│   │   ├── router/index.js          # Vue Router
│   │   ├── views/
│   │   │   ├── LoginView.vue              # Login
│   │   │   ├── DashboardView.vue          # Top 50 patients by latest result
│   │   │   ├── ShiftSelectView.vue        # Select shift + manage current roster
│   │   │   ├── ShiftManagementView.vue    # Create/manage shift schedules
│   │   │   ├── PatientRosterView.vue      # Search, add patients to shift
│   │   │   ├── SessionView.vue            # Results + MD Notes + Export
│   │   │   ├── PatientsView.vue           # Patient management (server-side paginated)
│   │   │   ├── UsersView.vue              # User management (server-side paginated)
│   │   │   ├── ClinicsView.vue            # Clinic management
│   │   │   ├── LongitudinalView.vue       # Longitudinal result trends
│   │   │   ├── Hl7InboxView.vue           # HL7 inbox status, quarantine, reprocess
│   │   │   ├── SettingsView.vue           # Tenant settings
│   │   │   └── ResetPasswordView.vue      # Password reset
│   │   ├── App.vue             # Layout: header + sidebar + router-view
│   │   └── main.js
│   ├── index.html
│   ├── vite.config.js
│   ├── Dockerfile
│   └── nginx.conf
└── docker-compose.yml
```

---

## Quick Start — Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org)
- [PostgreSQL 16](https://www.postgresql.org/download/) running locally

### Option A: Run manually (recommended for dev)

#### 1. Start PostgreSQL
Make sure PostgreSQL is running on `localhost:5432` with:
- Database: `dx7`
- Username: `postgres`
- Password: `postgres`

```bash
psql -U postgres -c "CREATE DATABASE dx7;"
```

#### 2. Start the .NET API
```bash
cd backend/Dx7Api

dotnet restore
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet ef database update

dotnet run
# → API running at http://localhost:5000
# → Swagger UI at http://localhost:5000/swagger
```

#### 3. Start the Vue frontend
```bash
cd frontend
npm install
npm run dev
# → App running at http://localhost:5173
```

### Option B: Docker Compose (all-in-one)

```bash
docker-compose up --build
# → App at http://localhost:5173
# → API at http://localhost:5000
# → Swagger at http://localhost:5000/swagger
```

---

## Demo Accounts (auto-seeded)

| Role | Email | Password | Can Do |
|------|-------|----------|--------|
| Charge Nurse | `charge@dx7.local` | `Nurse@1234` | Select patients, assign chairs, view results, export |
| Shift Nurse | `nurse@dx7.local` | `Nurse@1234` | View results, view MD notes (read-only) |
| Nephrologist | `md@dx7.local` | `Doctor@1234` | View results, write/edit session notes |
| Clinic Admin | `admin@dx7.local` | `Admin@1234` | User CRUD, patient management, shift management |
| PL Admin | `pladmin@dx7.local` | `Admin@1234` | All clinics under tenant, HL7 inbox, full admin |

---

## API Reference

### Auth
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/login` | Login with email + password → JWT token |
| POST | `/api/auth/external` | SSO login via Google or Facebook → JWT token |

### Patients
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/patients` | List patients (`search`, `status`, `sortBy`, `page`, `pageSize`) |
| GET | `/api/patients/summary` | Stat card counts (total, ready, stale, noData) |
| GET | `/api/patients/:id` | Single patient |
| POST | `/api/patients` | Create patient |
| DELETE | `/api/patients/:id` | Soft-delete patient |

### Sessions
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/sessions` | Sessions by `date`, `dateFrom`/`dateTo`, or `shift` |
| GET | `/api/sessions/last-date` | Most recent session date for a clinic |
| GET | `/api/sessions/:id` | Single session |
| POST | `/api/sessions` | Create session |
| POST | `/api/sessions/bulk` | Bulk create sessions |
| PATCH | `/api/sessions/:id` | Update chair assignment |
| DELETE | `/api/sessions/:id` | Delete session |

### Results
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/results/current/:patientId` | Latest value per analyte — for status indicators |
| GET | `/api/results/compare/:patientId` | Last N result dates as columns — for SessionView |
| GET | `/api/results/by-date/:patientId` | All results grouped by result date |
| GET | `/api/results/orders/:patientId` | Full CDM chain: Orders → Headers → Values |
| GET | `/api/results/longitudinal/:patientId` | 6-month trend per analyte (`?months=6`) |

### Notes & Export
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/notes?sessionId=` | MD notes for session |
| POST | `/api/notes` | Create note (MD only) |
| PATCH | `/api/notes/:id` | Edit note (MD, 24hr window) |
| POST | `/api/export` | Export session CSV or JSON |
| GET | `/api/export/session-pdf` | Session result PDF |
| POST | `/api/export/shift-pdf` | Shift summary PDF |
| GET | `/api/export/urr` | URR report |

### Users
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/users` | List users (`search`, `role`, `status`, `page`, `pageSize`) |
| GET | `/api/users/me` | Current user profile |
| GET | `/api/users/:id` | Single user |
| POST | `/api/users` | Create user |
| PATCH | `/api/users/:id` | Update user |
| PATCH | `/api/users/:id/activate` | Activate user |
| PATCH | `/api/users/:id/deactivate` | Deactivate user |
| PATCH | `/api/users/me/password` | Change own password |
| POST | `/api/users/:id/avatar` | Upload avatar |
| DELETE | `/api/users/:id/avatar` | Remove avatar |

### Clinics
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/clinics` | List clinics |
| POST | `/api/clinics` | Create clinic |
| PATCH | `/api/clinics/:id` | Update clinic |
| PATCH | `/api/clinics/:id/activate` | Activate clinic |
| PATCH | `/api/clinics/:id/deactivate` | Deactivate clinic |
| PATCH | `/api/clinics/:id/branding` | Update clinic branding |

### Shifts
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/shifts` | Shifts for a date |
| GET | `/api/shifts/week` | Shifts for a 7-day range |
| GET | `/api/shifts/history` | Shift history (30-day default) |
| POST | `/api/shifts` | Create shift schedule |
| POST | `/api/shifts/bulk` | Bulk create shifts for a date range |
| PATCH | `/api/shifts/:id` | Update shift |
| DELETE | `/api/shifts/:id` | Delete shift |
| POST | `/api/shifts/:id/nurses` | Assign nurse to shift |
| DELETE | `/api/shifts/:id/nurses/:assignmentId` | Remove nurse from shift |

### Tenant & Reference Data
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/tenant` | Current tenant info |
| PATCH | `/api/tenant/branding` | Update tenant branding |
| GET | `/api/tenant/sxa-tests` | SXA test catalog |
| GET | `/api/tenant/sxa-analytes` | SXA analyte catalog |
| GET | `/api/tenant/test-maps` | OBR-4 → SXA test mappings |
| POST | `/api/tenant/test-maps` | Add test mapping |
| DELETE | `/api/tenant/test-maps/:id` | Remove test mapping |
| GET | `/api/tenant/analyte-maps` | OBX-3 → analyte mappings |
| POST | `/api/tenant/analyte-maps` | Add analyte mapping |
| DELETE | `/api/tenant/analyte-maps/:id` | Remove analyte mapping |
| GET | `/api/roles` | Available roles (for dropdowns) |
| GET | `/api/refdata?category=` | Reference data by category |

### HL7 Ingestion
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/hl7/inbox/status` | Pending/processed/errored file counts |
| GET | `/api/hl7/log` | Recent processing log entries |
| DELETE | `/api/hl7/log` | Clear all log entries |
| DELETE | `/api/hl7/log/:idx` | Remove single log entry |
| GET | `/api/hl7/log/read` | Read raw HL7 file from log entry |
| POST | `/api/hl7/upload` | Upload one or more `.hl7` files |
| POST | `/api/hl7/message` | Submit raw HL7 message (text/plain) |
| GET | `/api/hl7/quarantine` | List quarantined files |
| GET | `/api/hl7/quarantine/read` | Read raw + parsed content + unmapped codes |
| POST | `/api/hl7/quarantine/reprocess` | Reprocess single quarantined file |
| POST | `/api/hl7/quarantine/reprocess-all` | Batch reprocess all quarantined files |
| DELETE | `/api/hl7/quarantine` | Permanently delete quarantined file |

Full interactive docs: **http://localhost:5000/swagger**

---

## HL7 Ingestion

Dx7 includes a production-grade HL7 v2 ingestion pipeline:

- **File Watcher**: `BackgroundService` monitors `HL7Inbox/{tenant}/` for `.hl7` files
- **Supported messages**: `ORM^O01` (orders), `ORU^R01` (results)
- **Segments parsed**: MSH, PID, OBR, OBX, NTE
- **Duplicate detection**: MSH-10 `MessageControlId` UNIQUE constraint — idempotent
- **Test mapping**: OBR-4 → `TenantTestMap` → `SxaTestCatalog`
- **Analyte mapping**: OBX-3 → `TenantAnalyteMap` → `SxaAnalyte`
- **Quarantine**: Unmapped test codes, non-numeric values, or missing patient identity → files moved to `error/` subfolder, accessible via UI for review and reprocess
- **Encryption**: Raw HL7 payload encrypted at rest with AES-256-GCM
- **Restart-safe**: Periodic 30s polling catches files missed during downtime

### HL7 Inbox path (production)
Set via environment variable:
```
Hl7__InboxPath=/hl7inbox
```
Drop files into `/hl7inbox/{TenantSlug}/` inside the container.

---

## Architecture Notes

- **Tenant isolation**: `tenant_id` enforced on every query server-side. Client never sets it.
- **JWT**: 8-hour tokens (shift-length). Role, tenant, client embedded in claims.
- **No interpretation**: Results are pass-through from source. No color coding, no risk labels.
- **BUNPRE / BUNPOST**: Always stored and displayed as separate test codes — never collapsed.
- **Stale data**: Results older than 30 days show ⚠ indicator with "X days ago" label.
- **CDM traceability**: Every `ResultValue` traces back to the raw HL7 message via `SchemaVersion = "DX7_CDM_1.0_A1"`.
- **Server-side pagination**: Patients and Users endpoints support `page`/`pageSize`/`search` — no full-table loads.

---

## Key Files to Know

| File | Purpose |
|------|---------|
| `backend/Dx7Api/Program.cs` | App bootstrap, DI, CORS, JWT, auto-migrate, test/analyte seed mappings |
| `backend/Dx7Api/Data/DbSeeder.cs` | Demo data — patients, users, results |
| `backend/Dx7Api/appsettings.json` | DB connection, JWT config, HL7 inbox path |
| `backend/Dx7Api/Services/Hl7FileWatcherService.cs` | HL7 background file watcher |
| `backend/Dx7Api/Services/Hl7/Hl7Processor.cs` | Core ingestion pipeline (parse → map → store) |
| `frontend/src/services/api.js` | All API calls in one place |
| `frontend/src/store/auth.js` | Login state, role helpers |
| `frontend/src/assets/main.css` | Full design system (CSS variables) |
