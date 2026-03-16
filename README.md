
Author : Cornelio S. Gallardo

# Dx7 — Dialysis Clinical Information System

> A multi-tenant Clinical Information System (CIS) for Philippine dialysis centers.
> Dx7 manages the full shift workflow — from patient roster assignment to lab result review to MD documentation — with integrated HL7 v2.x lab result ingestion from LIS / Sysmex instruments.

**Philosophy**: *"Dx7 presents lab data as-is from the laboratory source. No color-coded risk. No interpretation labels. No risk stratification. The nephrologist judges. Dx7 presents."*

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | .NET 8 — ASP.NET Core Web API |
| **Database** | PostgreSQL 16 + Entity Framework Core 8 (Npgsql) |
| **Frontend** | Vue 3.4 + Vite 5 + Pinia 2 |
| **Auth** | JWT Bearer (HS256, 8-hour expiry) + BCrypt password hashing |
| **Export** | iText7 (PDF), CsvHelper (CSV) |
| **API Docs** | Swagger / OpenAPI (Swashbuckle) |
| **Containerization** | Docker + Docker Compose + Nginx |

---

## Features

| Module | Description |
|---|---|
| **Dashboard** | Overview of daily activity and result stats |
| **Shift Management** | Create shift schedules, assign nurses to chairs |
| **Patient Roster** | Add/remove patients from shifts, assign chairs |
| **Sessions** | Per-patient session view with lab results and MD notes |
| **Lab Results** | Date-tabbed result reports with H/L/N flags, PDF/CSV export |
| **Longitudinal View** | Patient lab history across multiple sessions |
| **HL7 Inbox** | File-drop inbox, processing log, quarantine review and reprocessing |
| **Users** | User CRUD with role-based access control |
| **Clinics** | Clinic/client management (pl_admin) |
| **Settings** | HL7 code mappings — OBR-4 test maps and OBX-3 analyte maps per tenant |

---

## Project Structure

```
dx7/
├── docker-compose.yml
├── backend/
│   └── Dx7Api/
│       ├── Controllers/             # 12 REST API controllers
│       ├── Data/
│       │   ├── AppDbContext.cs      # EF Core + global tenant filters + auto-audit
│       │   └── DbSeeder.cs          # Seeds tenant, users, patients, code mappings
│       ├── DTOs/                    # Request/response record types
│       ├── Middleware/
│       │   └── TenantMiddleware.cs  # Extracts tenant_id from JWT → DbContext
│       ├── Models/                  # EF Core entity models
│       ├── Services/
│       │   ├── Hl7FileWatcherService.cs  # BackgroundService — watches HL7 inbox
│       │   ├── AuditService.cs           # Append-only audit logging
│       │   ├── Hl7Crypto.cs              # AES-256-GCM encryption for HL7 payloads
│       │   └── HL7/
│       │       ├── Hl7Parser.cs          # HL7 v2.x segment parser (MSH/PID/OBR/OBX)
│       │       └── Hl7Processor.cs       # CDM traceability chain processor
│       ├── HL7Inbox/
│       │   └── {TenantFolder}/
│       │       ├── processed/       # Successfully ingested files
│       │       ├── error/           # Quarantined files
│       │       └── dx7_hl7.log      # Single-line processing log
│       ├── Program.cs               # Startup — migrate, seed, idempotent patches
│       └── appsettings.json         # DB, JWT, Email, HL7, encryption config
└── frontend/
    └── src/
        ├── views/                   # 14 Vue SPA views
        ├── components/              # Reusable components (ResultReportModal, AppDialog)
        ├── composables/             # Shared Vue composables
        ├── router/index.js          # Vue Router with auth guards
        ├── store/auth.js            # Pinia auth store (token, user, role, tenant)
        ├── services/api.js          # Axios client with JWT interceptors
        └── assets/main.css          # Global design system (CSS variables + utilities)
```

---

## Getting Started

### Docker (Recommended)

```bash
git clone https://github.com/your-org/dx7.git
cd dx7
docker compose up --build
```

| Service | URL |
|---|---|
| Frontend | http://localhost:5173 |
| API | http://localhost:5000 |
| Swagger | http://localhost:5000/swagger |
| PostgreSQL | localhost:5432 |

---

### Manual Setup

**Backend**

```bash
cd backend/Dx7Api
# Edit appsettings.json — update DB connection string
dotnet run
# API starts on http://localhost:5000
```

**Frontend**

```bash
cd frontend
npm install
npm run dev
# Dev server on http://localhost:5173
```

---

## Configuration

`backend/Dx7Api/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dx7;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Key": "Dx7-SuperSecretKey-ChangeInProduction-MinLength32Chars!",
    "Issuer": "Dx7Api",
    "Audience": "Dx7Client",
    "ExpiryHours": 8
  },
  "Hl7": {
    "InboxPath": "HL7Inbox",
    "WatchIntervalSeconds": 30
  },
  "Hl7Encryption": {
    "Key": "<Base64-encoded 32-byte AES-256 key>"
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "UseSsl": true,
    "FromAddress": "noreply@dx7.local"
  },
  "AppUrl": "http://localhost:5173"
}
```

> **Production checklist**: Change `Jwt:Key` to a strong random string (32+ chars), configure real SMTP credentials, update `Cors:Origins` to your domain. Never commit production secrets.

---

## Database

- **Engine**: PostgreSQL 16
- **Schema**: Created automatically on first run via `EnsureCreatedAsync()`
- **Seeder**: Inserts default tenant, clinic, users, role definitions, HL7 code mappings on a fresh database
- **Startup patches**: Idempotent SQL in `Program.cs` ensures SXA catalog and tenant maps are always current (`WHERE NOT EXISTS` / `ON CONFLICT DO NOTHING`)

### Key Tables

| Table | Purpose |
|---|---|
| `Tenants` | Top-level organization |
| `Clients` | Dialysis clinics under a tenant |
| `Users` | Staff accounts with roles |
| `Patients` | Patient records (LIS IDs, PhilHealth numbers) |
| `Sessions` | Per-patient shift sessions |
| `LabOrders` | Orders linked to accession numbers |
| `ResultHeaders` | One per OBR — links order to test panel |
| `ResultValues` | One per OBX — individual analyte results |
| `Hl7Messages` | Raw HL7 archive (AES-256-GCM encrypted) |
| `SxaTestCatalog` | Canonical test definitions (CBC, Chemistry, Lipid, etc.) |
| `SxaAnalytes` | Canonical analyte definitions (HGB, WBC, PLT, etc.) |
| `TenantTestMaps` | OBR-4 code → SXA test, per tenant |
| `TenantAnalyteMaps` | OBX-3 code → SXA analyte, per tenant |
| `AuditLogs` | Append-only change log |
| `ShiftSchedules` | Recurring shift definitions |
| `ShiftNurseAssignments` | Nurse-to-chair assignments per shift |

---

## HL7 Integration

Dx7 ingests HL7 v2.x `ORU^R01` result messages via a file-drop inbox monitored by a .NET `BackgroundService`.

### Processing Pipeline (CDM §9)

```
HL7 file dropped → HL7Inbox/{Tenant}/
  ↓
Hl7FileWatcherService detects file (FileSystemWatcher + 30s periodic scan)
  ↓
Hl7Parser  →  MSH / PID / OBR / OBX segments
  ↓
Hl7Processor:
  1. Archive raw payload → Hl7Messages  (AES-256-GCM encrypted)
  2. Duplicate check on MSH-10          (UNIQUE constraint — no reprocessing)
  3. Resolve / create Patient from PID
  4. Create LabOrder from OBR
  5. Map OBR-4 → SxaTest               (quarantine whole message if unmapped)
  6. Create ResultHeader
  7. Map OBX-3 → SxaAnalyte            (quarantine individual OBX if unmapped)
  8. Create ResultValues
  ↓
File moved to processed/ or error/ (quarantine)
Entry written to dx7_hl7.log (single line, pipe-delimited)
```

### Fixing Quarantined Files

1. Open **HL7 Inbox → Quarantine**
2. Click a file to see the quarantine reason and unmapped code chips
3. Go to **Settings → HL7 Code Mappings** and add the missing mappings
4. Click **Reprocess** on the quarantined file

---

## Multi-Tenancy

```
Tenant  (e.g., "Philippine Labs Corp")
  └── Client / Clinic  (e.g., "QC Dialysis Center")
      ├── Users     (scoped to clinic; pl_admin/sysad sees all)
      ├── Patients
      ├── Sessions
      └── Shifts
```

Tenant isolation is enforced at two levels:
1. **EF Core Global Query Filters** — `TenantMiddleware` sets `CurrentTenantId` on `AppDbContext` from the JWT; all queries automatically apply `WHERE TenantId = @id`.
2. **Controller-level defense** — Every query also explicitly filters by `TenantId`.

---

## Roles & Permissions

| Permission | pl_admin | clinic_admin | charge_nurse | shift_nurse | md |
|---|:---:|:---:|:---:|:---:|:---:|
| View shift roster | ✓ | ✓ | ✓ | ✓ | ✓ |
| Manage shift schedules | ✓ | ✓ | ✓ | — | — |
| Add / remove patients from shift | ✓ | ✓ | ✓ | — | — |
| Assign chairs | ✓ | ✓ | ✓ | — | — |
| View lab results | ✓ | ✓ | ✓ | ✓ | ✓ |
| Write MD notes | — | — | — | — | ✓ |
| Export CSV / PDF | ✓ | ✓ | ✓ | — | — |
| Manage users | ✓ | ✓ | — | — | — |
| Manage clinics | ✓ | — | — | — | — |
| HL7 Inbox access | ✓ | ✓ | — | — | — |
| HL7 code mappings (Settings) | ✓ | ✓ | — | — | — |

---

## Default Seed Accounts

> Created only on a fresh database. **Change all passwords before any production deployment.**

| Email | Password | Role |
|---|---|---|
| admin@dx7.local | Admin@1234 | clinic_admin |
| charge@dx7.local | Nurse@1234 | charge_nurse |
| nurse@dx7.local | Nurse@1234 | shift_nurse |
| md@dx7.local | Doctor@1234 | md (Nephrologist) |
| pladmin@dx7.local | Admin@1234 | pl_admin |

---

## Architecture Notes

### Audit Logging
All writes to core entities (`Patient`, `Session`, `ResultValue`, `User`, etc.) are automatically captured in `AuditLogs` via EF Core `ChangeTracker` inside `AppDbContext.SaveChangesAsync()`. Each entry records entity type, operation, before/after JSON state, user ID, and UTC timestamp.

### HL7 Payload Encryption
Raw HL7 content stored in `Hl7Messages.RawPayload` is encrypted with AES-256-GCM via `Hl7Crypto`. Configure the key in `appsettings.json` under `Hl7Encryption:Key` as a Base64-encoded 32-byte value.

### Idempotent Startup Patches
`Program.cs` runs SQL patches on every startup to ensure the SXA test catalog, SXA analyte catalog, and all default tenant code mappings are always present — even on existing databases where the seeder guard (`if (await db.Tenants.AnyAsync()) return`) would otherwise skip them.

### Concurrent HL7 Processing
`Hl7FileWatcherService` uses a `SemaphoreSlim(1,1)` to serialize all file processing. When multiple files arrive simultaneously, they are queued and processed one at a time — preventing race conditions on patient creation and log file contention.

---

## License

Private — LABExpress / SanteXA. All rights reserved.
