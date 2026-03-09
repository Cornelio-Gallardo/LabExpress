# Dx7 — Dialysis Exchange Dashboard

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
│   │   ├── Controllers/        # AuthController, PatientsController, SessionsController,
│   │   │                       # ResultsController, NotesController, ExportController
│   │   ├── Data/               # AppDbContext, DbSeeder
│   │   ├── DTOs/               # Request/Response DTOs
│   │   ├── Models/             # Tenant, Client, User, Patient, Session, Result, MdNote, ChairAudit
│   │   ├── Services/           # JwtService
│   │   ├── appsettings.json    # Config (DB connection, JWT, CORS)
│   │   ├── Program.cs          # DI, middleware, auto-migrate + seed
│   │   └── Dockerfile
│   └── init.sql                # Raw SQL (alternative to EF migrations)
├── frontend/
│   ├── src/
│   │   ├── assets/main.css     # Global design system
│   │   ├── services/api.js     # Axios + all API calls
│   │   ├── store/auth.js       # Pinia auth store
│   │   ├── router/index.js     # Vue Router
│   │   ├── views/
│   │   │   ├── LoginView.vue          # Login with demo account shortcuts
│   │   │   ├── ShiftSelectView.vue    # Select shift + manage current roster
│   │   │   ├── PatientRosterView.vue  # Search, tick, add patients to shift
│   │   │   ├── SessionView.vue        # Results + MD Notes + Export
│   │   │   └── PatientsView.vue       # Patient management
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
# Create database
psql -U postgres -c "CREATE DATABASE dx7;"
```

#### 2. Start the .NET API
```bash
cd backend/Dx7Api

# Restore packages
dotnet restore

# Run EF Core migrations (creates all tables automatically)
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet ef database update

# Start API (auto-seeds demo data on first run)
dotnet run
# → API running at http://localhost:5000
# → Swagger UI at http://localhost:5000/swagger
```

#### 3. Start the Vue frontend
```bash
cd frontend

# Install dependencies
npm install

# Start dev server (proxies /api to localhost:5000)
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
| Clinic Admin | `admin@dx7.local` | `Admin@1234` | User CRUD, patient management |
| PL Admin | `pladmin@dx7.local` | `Admin@1234` | All clinics under tenant |

---

## API Reference

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/login` | Login → JWT token |
| GET | `/api/patients` | List patients (search, status filter) |
| GET | `/api/patients/:id` | Single patient |
| POST | `/api/patients` | Create patient |
| DELETE | `/api/patients/:id` | Soft-delete patient |
| GET | `/api/sessions` | Sessions by date+shift |
| POST | `/api/sessions` | Create session |
| POST | `/api/sessions/bulk` | Bulk create sessions |
| PATCH | `/api/sessions/:id` | Update chair |
| GET | `/api/results/current/:patientId` | Latest result per test |
| GET | `/api/results/history/:patientId/:testCode` | Result history |
| POST | `/api/results` | Create result (HL7 simulation) |
| GET | `/api/notes?sessionId=` | MD notes for session |
| POST | `/api/notes` | Create note (MD only) |
| PATCH | `/api/notes/:id` | Edit note (MD, 24hr window) |
| POST | `/api/export` | Export CSV or JSON |

Full interactive docs: **http://localhost:5000/swagger**

---

## Architecture Notes

- **Tenant isolation**: `tenant_id` enforced on every query server-side. Client never sets it.
- **JWT**: 8-hour tokens (shift-length). Role, tenant, client embedded in claims.
- **No interpretation**: Results are pass-through from source. No color coding, no risk labels.
- **BUNPRE / BUNPOST**: Always stored and displayed as separate test codes — never collapsed.
- **Stale data**: Results older than 30 days show ⚠ indicator with "X days ago" label.
- **Transition-ready**: API shape mirrors canonical platform pattern. Backend swap = config change only.

---

## Key Files to Know

| File | Purpose |
|------|---------|
| `backend/Dx7Api/Program.cs` | App bootstrap, DI, CORS, JWT, auto-migrate |
| `backend/Dx7Api/Data/DbSeeder.cs` | Demo data — patients, users, results |
| `backend/Dx7Api/appsettings.json` | DB connection string, JWT config |
| `frontend/src/services/api.js` | All API calls in one place |
| `frontend/src/store/auth.js` | Login state, role helpers |
| `frontend/src/assets/main.css` | Full design system (CSS variables) |
