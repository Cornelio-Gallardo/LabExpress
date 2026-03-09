-- Dx7 PostgreSQL Database Initialization
-- Run this only if NOT using EF Core migrations (dotnet ef database update)
-- EF Core migrations are the recommended approach.

-- Create database (run as superuser)
-- CREATE DATABASE dx7;
-- \c dx7

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Tenants (Partner Labs)
CREATE TABLE IF NOT EXISTS "Tenants" (
    "Id"           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "Name"         VARCHAR(100) NOT NULL,
    "Code"         VARCHAR(20),
    "LogoUrl"      TEXT,
    "FooterText"   TEXT,
    "PrimaryColor" VARCHAR(20) DEFAULT '0D7377',
    "IsActive"     BOOLEAN DEFAULT TRUE,
    "CreatedAt"    TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt"    TIMESTAMPTZ DEFAULT NOW()
);

-- Clients (Dialysis Clinics)
CREATE TABLE IF NOT EXISTS "Clients" (
    "Id"        UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "TenantId"  UUID NOT NULL REFERENCES "Tenants"("Id"),
    "Name"      VARCHAR(100) NOT NULL,
    "Code"      VARCHAR(20),
    "LogoUrl"   TEXT,
    "Address"   TEXT,
    "IsActive"  BOOLEAN DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ DEFAULT NOW()
);

-- Users
CREATE TABLE IF NOT EXISTS "Users" (
    "Id"           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "TenantId"     UUID NOT NULL REFERENCES "Tenants"("Id"),
    "ClientId"     UUID REFERENCES "Clients"("Id"),
    "Email"        VARCHAR(200) NOT NULL UNIQUE,
    "PasswordHash" TEXT NOT NULL,
    "Name"         VARCHAR(100) NOT NULL,
    "Role"         VARCHAR(20) NOT NULL DEFAULT 'shift_nurse'
                   CHECK ("Role" IN ('sysad','pl_admin','clinic_admin','charge_nurse','shift_nurse','md')),
    "IsActive"     BOOLEAN DEFAULT TRUE,
    "CreatedAt"    TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt"    TIMESTAMPTZ DEFAULT NOW()
);

-- Patients
CREATE TABLE IF NOT EXISTS "Patients" (
    "Id"            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "TenantId"      UUID NOT NULL REFERENCES "Tenants"("Id"),
    "ClientId"      UUID NOT NULL REFERENCES "Clients"("Id"),
    "LisPatientId"  VARCHAR(100),
    "Name"          VARCHAR(200) NOT NULL,
    "Birthdate"     DATE,
    "Gender"        VARCHAR(10),
    "ContactNumber" VARCHAR(30),
    "IsActive"      BOOLEAN DEFAULT TRUE,
    "CreatedAt"     TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt"     TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE ("TenantId", "ClientId", "LisPatientId")
);
CREATE INDEX IF NOT EXISTS idx_patients_tenant_client ON "Patients"("TenantId", "ClientId");

-- Sessions
CREATE TABLE IF NOT EXISTS "Sessions" (
    "Id"          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "TenantId"    UUID NOT NULL REFERENCES "Tenants"("Id"),
    "ClientId"    UUID NOT NULL REFERENCES "Clients"("Id"),
    "PatientId"   UUID NOT NULL REFERENCES "Patients"("Id"),
    "SessionDate" DATE NOT NULL,
    "ShiftNumber" INT NOT NULL CHECK ("ShiftNumber" BETWEEN 1 AND 4),
    "Chair"       VARCHAR(20),
    "AssignedBy"  UUID NOT NULL REFERENCES "Users"("Id"),
    "AssignedAt"  TIMESTAMPTZ DEFAULT NOW(),
    "CreatedAt"   TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt"   TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE ("PatientId", "SessionDate", "ShiftNumber")
);

-- Results (Operational Canonical Cache)
CREATE TABLE IF NOT EXISTS "Results" (
    "Id"              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "TenantId"        UUID NOT NULL REFERENCES "Tenants"("Id"),
    "PatientId"       UUID NOT NULL REFERENCES "Patients"("Id"),
    "AccessionId"     VARCHAR(100),
    "TestCode"        VARCHAR(50) NOT NULL,
    "TestName"        VARCHAR(200) NOT NULL,
    "ResultValue"     TEXT,
    "ResultUnit"      VARCHAR(50),
    "ReferenceRange"  TEXT,
    "AbnormalFlag"    VARCHAR(5),
    "ResultDate"      DATE NOT NULL,
    "ResultTime"      TIME,
    "SourceMessageId" VARCHAR(200),
    "SourceLab"       VARCHAR(100),
    "CreatedAt"       TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt"       TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_results_patient_code_date ON "Results"("TenantId", "PatientId", "TestCode", "ResultDate");

-- MD Notes
CREATE TABLE IF NOT EXISTS "MdNotes" (
    "Id"        UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "TenantId"  UUID NOT NULL REFERENCES "Tenants"("Id"),
    "SessionId" UUID NOT NULL REFERENCES "Sessions"("Id"),
    "MdUserId"  UUID NOT NULL REFERENCES "Users"("Id"),
    "NoteText"  TEXT NOT NULL,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ DEFAULT NOW()
);

-- Chair Audit
CREATE TABLE IF NOT EXISTS "ChairAudits" (
    "Id"        UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "SessionId" UUID NOT NULL REFERENCES "Sessions"("Id"),
    "ChairOld"  VARCHAR(20),
    "ChairNew"  VARCHAR(20),
    "ChangedBy" UUID NOT NULL REFERENCES "Users"("Id"),
    "ChangedAt" TIMESTAMPTZ DEFAULT NOW()
);
