using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dx7Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCdmSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────────
            // CDM §2.1 / §4 / §5 / §6 — create tables that are in the EF model
            // but were never added to any migration (existed in production via
            // EnsureCreated).  All statements use IF NOT EXISTS so this migration
            // is safe to run against both fresh installs and existing databases.
            // ─────────────────────────────────────────────────────────────────────

            // ── §5.1  SxaTestCatalog ─────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "SxaTestCatalog" (
                    "SxaTestId"      character varying(50)  NOT NULL,
                    "CanonicalName"  character varying(200) NOT NULL,
                    "Category"       character varying(50)  NOT NULL DEFAULT '',
                    "ResultType"     text                   NOT NULL DEFAULT 'SINGLE',
                    "ActiveFlag"     boolean                NOT NULL DEFAULT true,
                    CONSTRAINT "PK_SxaTestCatalog" PRIMARY KEY ("SxaTestId")
                );
                """);

            // ── §5.2  SxaAnalytes ────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "SxaAnalytes" (
                    "AnalyteCode"   character varying(50)  NOT NULL,
                    "DisplayName"   character varying(200) NOT NULL,
                    "DefaultUnit"   character varying(20),
                    "ResultType"    text                   NOT NULL DEFAULT 'NUMERIC',
                    CONSTRAINT "PK_SxaAnalytes" PRIMARY KEY ("AnalyteCode")
                );
                """);

            // ── RefData ──────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "RefData" (
                    "Id"          uuid                   NOT NULL DEFAULT gen_random_uuid(),
                    "Category"    character varying(50)  NOT NULL,
                    "Code"        character varying(50)  NOT NULL,
                    "Label"       character varying(100) NOT NULL,
                    "Description" text,
                    "SortOrder"   integer                NOT NULL DEFAULT 0,
                    "IsActive"    boolean                NOT NULL DEFAULT true,
                    CONSTRAINT "PK_RefData" PRIMARY KEY ("Id")
                );
                """);

            // ── §2.1  Hl7Messages ────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Hl7Messages" (
                    "Id"                uuid                    NOT NULL DEFAULT gen_random_uuid(),
                    "TenantId"          uuid                    NOT NULL,
                    "MessageControlId"  character varying(100)  NOT NULL,
                    "RawPayload"        text                    NOT NULL DEFAULT '',
                    "ReceivedAt"        timestamp with time zone NOT NULL DEFAULT now(),
                    "ProcessedFlag"     boolean                 NOT NULL DEFAULT false,
                    "QuarantineFlag"    boolean                 NOT NULL DEFAULT false,
                    "QuarantineReason"  character varying(500),
                    CONSTRAINT "PK_Hl7Messages" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_Hl7Messages_Tenants_TenantId"
                        FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
                );
                """);

            // UNIQUE: prevent HL7 retransmission duplicates (CDM §2.1)
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Hl7Messages_TenantId_MessageControlId"
                    ON "Hl7Messages" ("TenantId", "MessageControlId");
                """);

            // ── §4.1  LabOrders ──────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "LabOrders" (
                    "Id"                  uuid                    NOT NULL DEFAULT gen_random_uuid(),
                    "TenantId"            uuid                    NOT NULL,
                    "ClientId"            uuid                    NOT NULL,
                    "PatientId"           uuid                    NOT NULL,
                    "AccessionNumber"     character varying(100)  NOT NULL,
                    "SourceHl7MessageId"  uuid                    NOT NULL,
                    "ReleasedAt"          timestamp with time zone,
                    "CreatedAt"           timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_LabOrders" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_LabOrders_Tenants_TenantId"
                        FOREIGN KEY ("TenantId")   REFERENCES "Tenants"     ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_LabOrders_Clients_ClientId"
                        FOREIGN KEY ("ClientId")   REFERENCES "Clients"     ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_LabOrders_Patients_PatientId"
                        FOREIGN KEY ("PatientId")  REFERENCES "Patients"    ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_LabOrders_Hl7Messages_SourceHl7MessageId"
                        FOREIGN KEY ("SourceHl7MessageId") REFERENCES "Hl7Messages" ("Id") ON DELETE RESTRICT
                );
                """);

            // ── §4.2  ResultHeaders ──────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ResultHeaders" (
                    "Id"                  uuid                    NOT NULL DEFAULT gen_random_uuid(),
                    "OrderId"             uuid                    NOT NULL,
                    "TenantId"            uuid                    NOT NULL,
                    "SourceHl7MessageId"  uuid                    NOT NULL,
                    "SxaTestId"           character varying(50),
                    "SpecimenType"        character varying(200),
                    "CollectionDatetime"  timestamp with time zone,
                    "ResultDatetime"      timestamp with time zone,
                    CONSTRAINT "PK_ResultHeaders" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ResultHeaders_LabOrders_OrderId"
                        FOREIGN KEY ("OrderId")   REFERENCES "LabOrders"      ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ResultHeaders_Tenants_TenantId"
                        FOREIGN KEY ("TenantId")  REFERENCES "Tenants"        ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ResultHeaders_Hl7Messages_SourceHl7MessageId"
                        FOREIGN KEY ("SourceHl7MessageId") REFERENCES "Hl7Messages" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_ResultHeaders_SxaTestCatalog_SxaTestId"
                        FOREIGN KEY ("SxaTestId") REFERENCES "SxaTestCatalog" ("SxaTestId") ON DELETE SET NULL
                );
                """);

            // ── §4.3  ResultValues ───────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ResultValues" (
                    "Id"                  uuid                    NOT NULL DEFAULT gen_random_uuid(),
                    "ResultHeaderId"      uuid                    NOT NULL,
                    "TenantId"            uuid                    NOT NULL,
                    "AnalyteCode"         character varying(50),
                    "DisplayValue"        text                    NOT NULL DEFAULT '',
                    "ValueNumeric"        numeric,
                    "Unit"                character varying(50),
                    "ReferenceRangeLow"   numeric,
                    "ReferenceRangeHigh"  numeric,
                    "ReferenceRangeRaw"   text,
                    "AbnormalFlag"        character varying(5),
                    "RawHl7Segment"       text,
                    "NoSpecimen"          boolean                 NOT NULL DEFAULT false,
                    "NotCalculated"       boolean                 NOT NULL DEFAULT false,
                    "Superseded"          boolean                 NOT NULL DEFAULT false,
                    "SchemaVersion"       character varying(30)   NOT NULL DEFAULT 'DX7_CDM_1.0_A1',
                    CONSTRAINT "PK_ResultValues" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ResultValues_ResultHeaders_ResultHeaderId"
                        FOREIGN KEY ("ResultHeaderId") REFERENCES "ResultHeaders" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ResultValues_Tenants_TenantId"
                        FOREIGN KEY ("TenantId")       REFERENCES "Tenants"      ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ResultValues_SxaAnalytes_AnalyteCode"
                        FOREIGN KEY ("AnalyteCode")    REFERENCES "SxaAnalytes"  ("AnalyteCode") ON DELETE SET NULL
                );
                """);

            // ── §4.3 amendment columns on ResultValues (existing DBs) ─────────────
            // If ResultValues already existed (EnsureCreated DB), add the amendment
            // columns that were not present at original creation time.
            migrationBuilder.Sql("""
                ALTER TABLE "ResultValues" ADD COLUMN IF NOT EXISTS "NoSpecimen"    boolean              NOT NULL DEFAULT false;
                ALTER TABLE "ResultValues" ADD COLUMN IF NOT EXISTS "NotCalculated" boolean              NOT NULL DEFAULT false;
                ALTER TABLE "ResultValues" ADD COLUMN IF NOT EXISTS "Superseded"    boolean              NOT NULL DEFAULT false;
                ALTER TABLE "ResultValues" ADD COLUMN IF NOT EXISTS "SchemaVersion" character varying(30) NOT NULL DEFAULT 'DX7_CDM_1.0_A1';
                """);

            // ── §6.1  TenantTestMaps ─────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "TenantTestMaps" (
                    "Id"              uuid                   NOT NULL DEFAULT gen_random_uuid(),
                    "TenantId"        uuid                   NOT NULL,
                    "TenantTestCode"  character varying(50)  NOT NULL,
                    "SxaTestId"       character varying(50)  NOT NULL,
                    "IsActive"        boolean                NOT NULL DEFAULT true,
                    CONSTRAINT "PK_TenantTestMaps" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_TenantTestMaps_Tenants_TenantId"
                        FOREIGN KEY ("TenantId")  REFERENCES "Tenants"       ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_TenantTestMaps_SxaTestCatalog_SxaTestId"
                        FOREIGN KEY ("SxaTestId") REFERENCES "SxaTestCatalog" ("SxaTestId") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantTestMaps_TenantId_TenantTestCode"
                    ON "TenantTestMaps" ("TenantId", "TenantTestCode");
                """);

            // ── §6.2  TenantAnalyteMaps ──────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "TenantAnalyteMaps" (
                    "Id"                uuid                   NOT NULL DEFAULT gen_random_uuid(),
                    "TenantId"          uuid                   NOT NULL,
                    "TenantAnalyteCode" character varying(50)  NOT NULL,
                    "AnalyteCode"       character varying(50)  NOT NULL,
                    "IsActive"          boolean                NOT NULL DEFAULT true,
                    CONSTRAINT "PK_TenantAnalyteMaps" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_TenantAnalyteMaps_Tenants_TenantId"
                        FOREIGN KEY ("TenantId")    REFERENCES "Tenants"    ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_TenantAnalyteMaps_SxaAnalytes_AnalyteCode"
                        FOREIGN KEY ("AnalyteCode") REFERENCES "SxaAnalytes" ("AnalyteCode") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantAnalyteMaps_TenantId_TenantAnalyteCode"
                    ON "TenantAnalyteMaps" ("TenantId", "TenantAnalyteCode");
                """);

            // ── AuditLogs ────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "AuditLogs" (
                    "Id"        uuid                    NOT NULL DEFAULT gen_random_uuid(),
                    "TenantId"  uuid                    NOT NULL,
                    "UserId"    uuid,
                    "Action"    character varying(50)   NOT NULL,
                    "Entity"    character varying(100)  NOT NULL,
                    "EntityId"  uuid,
                    "Before"    text,
                    "After"     text,
                    "Notes"     character varying(500),
                    "Timestamp" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_AuditLogs_Tenants_TenantId"
                        FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_AuditLogs_TenantId_Entity_EntityId"
                    ON "AuditLogs" ("TenantId", "Entity", "EntityId");
                """);

            // ── LabNotes ─────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "LabNotes" (
                    "Id"             uuid    NOT NULL DEFAULT gen_random_uuid(),
                    "TenantId"       uuid    NOT NULL,
                    "ResultHeaderId" uuid    NOT NULL,
                    "NoteText"       text    NOT NULL,
                    "SortOrder"      integer NOT NULL DEFAULT 0,
                    CONSTRAINT "PK_LabNotes" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_LabNotes_Tenants_TenantId"
                        FOREIGN KEY ("TenantId")       REFERENCES "Tenants"       ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_LabNotes_ResultHeaders_ResultHeaderId"
                        FOREIGN KEY ("ResultHeaderId") REFERENCES "ResultHeaders" ("Id") ON DELETE CASCADE
                );
                """);

            // ── Missing columns on pre-existing tables ───────────────────────────
            // User — SSO fields and avatar added after InitialCreate
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "AvatarUrl"            text;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ExternalProvider"     character varying(50);
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ExternalProviderId"   character varying(200);
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordResetToken"   character varying(200);
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordResetExpiry"  timestamp with time zone;
                """);

            // Session — ShiftLabel added after ShiftManagement migration
            migrationBuilder.Sql("""
                ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "ShiftLabel" character varying(50) NOT NULL DEFAULT '';
                """);

            // Result — CDM linkage columns added after InitialCreate
            migrationBuilder.Sql("""
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "Hl7MessageId"       uuid;
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "SxaTestId"          character varying(50);
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "AnalyteCode"        character varying(50);
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "DisplayValue"       text;
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "ValueNumeric"       numeric;
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "ReferenceRangeLow"  numeric;
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "ReferenceRangeHigh" numeric;
                ALTER TABLE "Results" ADD COLUMN IF NOT EXISTS "RawHl7Segment"      text;
                """);

            // Result → Hl7Messages FK (SET NULL so deleting a message doesn't cascade to flat Results)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'FK_Results_Hl7Messages_Hl7MessageId'
                    ) THEN
                        ALTER TABLE "Results"
                            ADD CONSTRAINT "FK_Results_Hl7Messages_Hl7MessageId"
                            FOREIGN KEY ("Hl7MessageId") REFERENCES "Hl7Messages" ("Id") ON DELETE SET NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "LabNotes";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "AuditLogs";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "TenantAnalyteMaps";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "TenantTestMaps";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ResultValues";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ResultHeaders";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "LabOrders";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "Hl7Messages";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "RefData";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "SxaAnalytes";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "SxaTestCatalog";""");
        }
    }
}
