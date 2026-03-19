using Dx7Api.Services.Hl7;
using Dx7Api.Services;
using Dx7Api.Middleware;
using System.Text;
using Dx7Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// CDM §2.1 — initialize HL7 raw_payload AES-256-GCM encryption
// Key must be a base64-encoded 32-byte value. Generate for production:
//   Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
var hl7EncKey = builder.Configuration["Hl7Encryption:Key"];
if (!string.IsNullOrEmpty(hl7EncKey))
    Hl7Crypto.Initialize(hl7EncKey);

// Database
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Auth
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpClient(); // used by AuthController for Google/Facebook token verification

// HL7 Listener
builder.Services.AddScoped<Hl7Processor>();
builder.Services.AddHostedService<Hl7FileWatcherService>();

// CORS for local Vue dev
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Dx7 API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

// Auto-migrate and seed
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // EnsureCreated creates schema if DB is empty; skips if tables exist
        var created = await db.Database.EnsureCreatedAsync();
        logger.LogInformation("DB EnsureCreated: {Created}", created ? "schema created" : "already exists");

        // Add any tables that were introduced after initial schema creation.
        // Safe to run repeatedly — uses CREATE TABLE IF NOT EXISTS.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id"        uuid                        NOT NULL,
                "TenantId"  uuid                        NOT NULL,
                "UserId"    uuid                        NULL,
                "Action"    character varying(50)       NOT NULL DEFAULT '',
                "Entity"    character varying(100)      NOT NULL DEFAULT '',
                "EntityId"  uuid                        NULL,
                "Before"    text                        NULL,
                "After"     text                        NULL,
                "Notes"     character varying(500)      NULL,
                "Timestamp" timestamp with time zone    NOT NULL DEFAULT now(),
                CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_AuditLogs_TenantId_Entity_EntityId"
                ON "AuditLogs" ("TenantId", "Entity", "EntityId");

            CREATE TABLE IF NOT EXISTS "LabNotes" (
                "Id"              uuid                        NOT NULL,
                "TenantId"        uuid                        NOT NULL,
                "ResultHeaderId"  uuid                        NOT NULL,
                "NoteText"        text                        NOT NULL DEFAULT '',
                "SortOrder"       integer                     NOT NULL DEFAULT 0,
                "CreatedAt"       timestamp with time zone    NOT NULL DEFAULT now(),
                "UpdatedAt"       timestamp with time zone    NOT NULL DEFAULT now(),
                CONSTRAINT "PK_LabNotes" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_LabNotes_ResultHeaders_ResultHeaderId"
                    FOREIGN KEY ("ResultHeaderId") REFERENCES "ResultHeaders" ("Id") ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS "RefData" (
                "Id"          uuid                        NOT NULL,
                "Category"    character varying(50)       NOT NULL DEFAULT '',
                "Code"        character varying(50)       NOT NULL DEFAULT '',
                "Label"       character varying(100)      NOT NULL DEFAULT '',
                "Description" text                        NULL,
                "SortOrder"   integer                     NOT NULL DEFAULT 0,
                "IsActive"    boolean                     NOT NULL DEFAULT true,
                CONSTRAINT "PK_RefData" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefData_Category_Code"
                ON "RefData" ("Category", "Code");
        """);
        // CDM Amendment 1 §10.4 — add no_specimen, not_calculated flags to ResultValues
        // CDM Amendment 1 §11.2 — add schema_version to ResultValues
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ResultValues"
                ADD COLUMN IF NOT EXISTS "NoSpecimen"    boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS "NotCalculated" boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS "SchemaVersion" character varying(30) NOT NULL DEFAULT 'DX7_CDM_1.0_A1';
        """);

        logger.LogInformation("DB schema patches applied");

        // Fix patient names that have a leading ", " from when PatientLastName was empty.
        // Converts ", ANTONIO" → "ANTONIO", leaves "LATAY, ANTONIO" unchanged.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Patients"
            SET "Name" = REGEXP_REPLACE("Name", '^[,\s]+', '')
            WHERE "Name" ~ '^[,\s]';
        """);
        logger.LogInformation("Patient name cleanup applied");

        // CDM Amendment 1 §9 — rename stale analyte codes to CDM-canonical IDs.
        // SXA_A_CREAT → SXA_A_CREA,  SXA_A_GLU → SXA_A_FBS
        // Also corrects ALB unit from g/dL to g/L per §9.3.
        // Safe to run repeatedly — WHERE EXISTS guards each statement.
        await db.Database.ExecuteSqlRawAsync("""
            -- Step 1: Insert new canonical SxaAnalyte rows first (FK target must exist before referencing rows are updated)
            INSERT INTO "SxaAnalytes" ("AnalyteCode", "DisplayName", "DefaultUnit", "ResultType")
            SELECT 'SXA_A_CREA', "DisplayName", 'µmol/L', "ResultType"
            FROM "SxaAnalytes" WHERE "AnalyteCode" = 'SXA_A_CREAT'
            ON CONFLICT ("AnalyteCode") DO NOTHING;

            INSERT INTO "SxaAnalytes" ("AnalyteCode", "DisplayName", "DefaultUnit", "ResultType")
            SELECT 'SXA_A_FBS', 'Fasting Blood Sugar', 'mmol/L', "ResultType"
            FROM "SxaAnalytes" WHERE "AnalyteCode" = 'SXA_A_GLU'
            ON CONFLICT ("AnalyteCode") DO NOTHING;

            -- Step 2: Update all FK references to point to new codes
            UPDATE "ResultValues" SET "AnalyteCode" = 'SXA_A_CREA' WHERE "AnalyteCode" = 'SXA_A_CREAT';
            UPDATE "ResultValues" SET "AnalyteCode" = 'SXA_A_FBS'  WHERE "AnalyteCode" = 'SXA_A_GLU';

            UPDATE "TenantAnalyteMaps" SET "AnalyteCode" = 'SXA_A_CREA' WHERE "AnalyteCode" = 'SXA_A_CREAT';
            UPDATE "TenantAnalyteMaps" SET "AnalyteCode" = 'SXA_A_FBS'  WHERE "AnalyteCode" = 'SXA_A_GLU';

            -- Step 3: Delete old rows now that nothing references them
            DELETE FROM "SxaAnalytes" WHERE "AnalyteCode" = 'SXA_A_CREAT';
            DELETE FROM "SxaAnalytes" WHERE "AnalyteCode" = 'SXA_A_GLU';

            -- Correct ALB unit per CDM §9.3
            UPDATE "SxaAnalytes" SET "DefaultUnit" = 'g/L'
            WHERE "AnalyteCode" = 'SXA_A_ALB' AND "DefaultUnit" = 'g/dL';
        """);
        logger.LogInformation("CDM Amendment 1 analyte code rename patches applied");

        // Ensure the full SXA catalog is present — safe to run repeatedly via ON CONFLICT DO NOTHING.
        // This covers both fresh DBs (before seeder runs) and existing DBs when new entries are added.
        // Table name is "SxaTestCatalog" (explicit ToTable mapping in AppDbContext).
        // ResultType stored as string per HasConversion<string>() in OnModelCreating.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "SxaTestCatalog" ("SxaTestId", "CanonicalName", "Category", "ResultType", "ActiveFlag")
            VALUES
                ('SXA_TEST_CBC',      'Complete Blood Count',                'Hematology', 'PANEL',  true),
                ('SXA_TEST_CHEM',     'Chemistry Panel',                     'Chemistry',  'PANEL',  true),
                ('SXA_TEST_FBS',      'Fasting Blood Sugar',                 'Chemistry',  'SINGLE', true),
                ('SXA_TEST_BUN_PRE',  'Blood Urea Nitrogen (Pre-Dialysis)',  'Chemistry',  'SINGLE', true),
                ('SXA_TEST_BUN_POST', 'Blood Urea Nitrogen (Post-Dialysis)', 'Chemistry',  'SINGLE', true),
                ('SXA_TEST_LIPID',    'Lipid Panel',                         'Chemistry',  'PANEL',  true),
                ('SXA_TEST_K',        'Potassium',                           'Chemistry',  'SINGLE', true),
                ('SXA_TEST_MULTI',    'Multi-Panel (Fallback)',               'Chemistry',  'PANEL',  true),
                ('SXA_TEST_CREA',     'Creatinine',                          'Chemistry',  'SINGLE', true),
                ('SXA_TEST_DDIMER',   'D-Dimer',                             'Hematology', 'SINGLE', true),
                ('SXA_TEST_BILI',     'Bilirubin',                           'Chemistry',  'PANEL',  true),
                ('SXA_TEST_FER',      'Ferritin',                            'Chemistry',  'SINGLE', true),
                ('SXA_TEST_TFT',      'Thyroid Function Test',               'Chemistry',  'PANEL',  true),
                ('SXA_TEST_URINE',    'Urinalysis',                          'Urinalysis', 'PANEL',  true)
            ON CONFLICT ("SxaTestId") DO NOTHING;

            INSERT INTO "SxaAnalytes" ("AnalyteCode", "DisplayName", "DefaultUnit", "ResultType")
            VALUES
                -- Hematology — CBC
                ('SXA_A_HGB',      'Hemoglobin',                  'g/dL',      'NUMERIC'),
                ('SXA_A_HCT',      'Hematocrit',                  '%',         'NUMERIC'),
                ('SXA_A_RBC',      'RBC',                         'x10¹²/L',   'NUMERIC'),
                ('SXA_A_WBC',      'WBC',                         'x10⁹/L',    'NUMERIC'),
                ('SXA_A_PLT',      'Platelets',                   'x10⁹/L',    'NUMERIC'),
                ('SXA_A_MCV',      'MCV',                         'fL',        'NUMERIC'),
                ('SXA_A_MCH',      'MCH',                         'pg',        'NUMERIC'),
                ('SXA_A_MCHC',     'MCHC',                        'g/dL',      'NUMERIC'),
                ('SXA_A_NEUT',     'Neutrophils',                 '',          'NUMERIC'),
                ('SXA_A_LYMPH',    'Lymphocytes',                 '',          'NUMERIC'),
                ('SXA_A_MONO',     'Monocytes',                   '',          'NUMERIC'),
                ('SXA_A_EO',       'Eosinophils',                 '',          'NUMERIC'),
                ('SXA_A_BASO',     'Basophils',                   '',          'NUMERIC'),
                -- Chemistry — Renal / Dialysis
                ('SXA_A_BUN_PRE',  'BUN Pre-Dialysis',            'mg/dL',     'NUMERIC'),
                ('SXA_A_BUN_POST', 'BUN Post-Dialysis',           'mg/dL',     'NUMERIC'),
                ('SXA_A_URR',      'URR',                         '%',         'NUMERIC'),
                ('SXA_A_KTV',      'Kt/V',                        '',          'NUMERIC'),
                ('SXA_A_CREA',     'Creatinine',                  'µmol/L',    'NUMERIC'),
                ('SXA_A_ALB',      'Albumin',                     'g/L',       'NUMERIC'),
                ('SXA_A_ALKP',     'Alkaline Phosphatase',        'U/L',       'NUMERIC'),
                ('SXA_A_UA',       'Uric Acid',                   'µmol/L',    'NUMERIC'),
                ('SXA_A_PHOS',     'Inorganic Phosphorus',        'mg/dL',     'NUMERIC'),
                ('SXA_A_NA',       'Sodium',                      'mEq/L',     'NUMERIC'),
                ('SXA_A_K',        'Potassium',                   'mEq/L',     'NUMERIC'),
                ('SXA_A_CA',       'Calcium',                     'mg/dL',     'NUMERIC'),
                ('SXA_A_ICAL',     'Ionized Calcium',             'mmol/L',    'NUMERIC'),
                ('SXA_A_FBS',      'Fasting Blood Sugar',         'mmol/L',    'NUMERIC'),
                ('SXA_A_ALT',      'SGPT (ALT)',                  'U/L',       'NUMERIC'),
                ('SXA_A_HBA1C',    'HbA1c (Glycated Hgb)',        '%',         'NUMERIC'),
                -- Lipid panel
                ('SXA_A_CHOL',     'Cholesterol',                 'mmol/L',    'NUMERIC'),
                ('SXA_A_TRIG',     'Triglycerides',               'mmol/L',    'NUMERIC'),
                ('SXA_A_HDL',      'HDL Cholesterol',             'mmol/L',    'NUMERIC'),
                ('SXA_A_LDL',      'LDL Cholesterol',             'mmol/L',    'NUMERIC'),
                ('SXA_A_VLDL',     'VLDL Cholesterol',            'mmol/L',    'NUMERIC')
            ON CONFLICT ("AnalyteCode") DO NOTHING;
        """);
        logger.LogInformation("SXA catalog ensured");

        // Ensure all OBR-4 test code maps exist for all tenants.
        // Safe to run repeatedly — WHERE NOT EXISTS prevents duplicates.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "TenantTestMaps" ("Id", "TenantId", "TenantTestCode", "SxaTestId", "IsActive")
            SELECT gen_random_uuid(), t."Id", codes."Code", codes."SxaId", true
            FROM "Tenants" t
            CROSS JOIN (VALUES
                ('BUNPRE',  'SXA_TEST_BUN_PRE'),
                ('BUNPOST', 'SXA_TEST_BUN_POST'),
                ('BUNPOS',  'SXA_TEST_BUN_POST'),
                ('DGC0074', 'SXA_TEST_CBC'),
                ('CBC',     'SXA_TEST_CBC'),
                ('DGC0035', 'SXA_TEST_FBS'),
                ('FBS',     'SXA_TEST_FBS'),
                ('K',       'SXA_TEST_K'),
                ('LIPID',   'SXA_TEST_LIPID'),
                ('CHEM',    'SXA_TEST_CHEM'),
                ('MULTI',   'SXA_TEST_MULTI'),
                ('CREA',    'SXA_TEST_CREA'),
                ('DDIMER',  'SXA_TEST_DDIMER'),
                ('BILI P',  'SXA_TEST_BILI'),
                ('BILI',    'SXA_TEST_BILI'),
                ('FER',     'SXA_TEST_FER'),
                ('TFT-BF',  'SXA_TEST_TFT'),
                ('TFT',     'SXA_TEST_TFT'),
                ('URINE',   'SXA_TEST_URINE')
            ) AS codes("Code", "SxaId")
            WHERE NOT EXISTS (
                SELECT 1 FROM "TenantTestMaps" m
                WHERE m."TenantId" = t."Id" AND m."TenantTestCode" = codes."Code"
            );
        """);

        // Ensure all analyte code maps exist for all tenants.
        // Safe to run repeatedly — WHERE NOT EXISTS prevents duplicates.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "TenantAnalyteMaps" ("Id", "TenantId", "TenantAnalyteCode", "AnalyteCode", "IsActive")
            SELECT gen_random_uuid(), t."Id", codes."Code", codes."AxCode", true
            FROM "Tenants" t
            CROSS JOIN (VALUES
                -- Hematology — CBC
                ('HGB',    'SXA_A_HGB'),
                ('HCT',    'SXA_A_HCT'),
                ('RBC',    'SXA_A_RBC'),
                ('WBC',    'SXA_A_WBC'),
                ('PLT',    'SXA_A_PLT'),
                ('MCV',    'SXA_A_MCV'),
                ('MCH',    'SXA_A_MCH'),
                ('MCHC',   'SXA_A_MCHC'),
                ('NEUT',   'SXA_A_NEUT'),
                ('LYMPH',  'SXA_A_LYMPH'),
                ('MONO',   'SXA_A_MONO'),
                ('EO',     'SXA_A_EO'),
                ('BASO',   'SXA_A_BASO'),
                -- Chemistry — Renal / Dialysis
                ('BUNPRE',  'SXA_A_BUN_PRE'),
                ('BUNPOST', 'SXA_A_BUN_POST'),
                ('BUNPOS',  'SXA_A_BUN_POST'),
                ('URR',     'SXA_A_URR'),
                ('KT/V',    'SXA_A_KTV'),
                ('CREA',    'SXA_A_CREA'),
                ('ALB',     'SXA_A_ALB'),
                ('ALKP',    'SXA_A_ALKP'),
                ('UA',      'SXA_A_UA'),
                ('PHOS',    'SXA_A_PHOS'),
                ('NA',      'SXA_A_NA'),
                ('K',       'SXA_A_K'),
                ('ICAL',    'SXA_A_ICAL'),
                ('FBS',     'SXA_A_FBS'),
                ('ALT',     'SXA_A_ALT'),
                ('HBA1C',   'SXA_A_HBA1C'),
                -- Lipid panel
                ('CHOL',    'SXA_A_CHOL'),
                ('TRIG',    'SXA_A_TRIG'),
                ('HDL',     'SXA_A_HDL'),
                ('LDL',     'SXA_A_LDL'),
                ('VLDL',    'SXA_A_VLDL')
            ) AS codes("Code", "AxCode")
            WHERE NOT EXISTS (
                SELECT 1 FROM "TenantAnalyteMaps" m
                WHERE m."TenantId" = t."Id" AND m."TenantAnalyteCode" = codes."Code"
            );
        """);
        logger.LogInformation("HCLAB code mapping aliases ensured");

        // ── RefData seed — all system status / flag / keyword values ─────────
        // Safe to run repeatedly — ON CONFLICT DO NOTHING.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "RefData" ("Id", "Category", "Code", "Label", "Description", "SortOrder", "IsActive")
            VALUES
                -- HL7 processing statuses
                (gen_random_uuid(), 'Hl7Status', 'order_saved',   'Processed',   'HL7 file ingested and orders saved',              1,  true),
                (gen_random_uuid(), 'Hl7Status', 'processed',     'Processed',   'HL7 file ingested successfully',                  2,  true),
                (gen_random_uuid(), 'Hl7Status', 'duplicate',     'Duplicate',   'HL7 message already processed (duplicate MSH-10)',3,  true),
                (gen_random_uuid(), 'Hl7Status', 'quarantined',   'Quarantined', 'HL7 file quarantined due to unmapped codes',      4,  true),
                (gen_random_uuid(), 'Hl7Status', 'error',         'Error',       'HL7 processing failed with exception',            5,  true),
                (gen_random_uuid(), 'Hl7Status', 'skipped',       'Skipped',     'HL7 file skipped (non-HL7 or empty)',             6,  true),
                (gen_random_uuid(), 'Hl7Status', 'unknown',       'Unknown',     'Status not recognized',                          7,  true),

                -- Result / order statuses
                (gen_random_uuid(), 'ResultStatus', 'final',      'Final',       'Result is final and released',                   1,  true),
                (gen_random_uuid(), 'ResultStatus', 'pending',    'Pending',     'Result awaiting verification',                   2,  true),
                (gen_random_uuid(), 'ResultStatus', 'corrected',  'Corrected',   'Previously released result has been corrected',  3,  true),

                -- Abnormal flags (OBX-8)
                (gen_random_uuid(), 'AbnormalFlag', 'H',          'High',        'Result above reference range high',              1,  true),
                (gen_random_uuid(), 'AbnormalFlag', 'L',          'Low',         'Result below reference range low',               2,  true),
                (gen_random_uuid(), 'AbnormalFlag', 'N',          'Normal',      'Result within reference range',                  3,  true),
                (gen_random_uuid(), 'AbnormalFlag', 'HH',         'Critical High','Result critically above range',                 4,  true),
                (gen_random_uuid(), 'AbnormalFlag', 'LL',         'Critical Low', 'Result critically below range',                 5,  true),
                (gen_random_uuid(), 'AbnormalFlag', 'A',          'Abnormal',    'Abnormal (non-numeric)',                         6,  true),

                -- Gender codes
                (gen_random_uuid(), 'Gender', 'M',               'Male',        NULL,                                             1,  true),
                (gen_random_uuid(), 'Gender', 'F',               'Female',      NULL,                                             2,  true),
                (gen_random_uuid(), 'Gender', 'U',               'Unknown',     NULL,                                             3,  true),

                -- Audit actions
                (gen_random_uuid(), 'AuditAction', 'CREATE',      'Created',     'Entity was created',                            1,  true),
                (gen_random_uuid(), 'AuditAction', 'UPDATE',      'Updated',     'Entity was updated',                            2,  true),
                (gen_random_uuid(), 'AuditAction', 'DELETE',      'Deleted',     'Entity was deleted',                            3,  true),
                (gen_random_uuid(), 'AuditAction', 'ACTIVATE',    'Activated',   'Entity was activated (IsActive = true)',         4,  true),
                (gen_random_uuid(), 'AuditAction', 'DEACTIVATE',  'Deactivated', 'Entity was deactivated (IsActive = false)',      5,  true),

                -- OBX section-header keywords (empty-value rows to skip during ingestion)
                (gen_random_uuid(), 'OBXSkipKeyword', 'DIFF',        'Differential header', 'CBC differential section header OBX',   1,  true),
                (gen_random_uuid(), 'OBXSkipKeyword', 'COMPLETED',   'Completed header',    'Test completed section header OBX',     2,  true),
                (gen_random_uuid(), 'OBXSkipKeyword', 'PENDING',     'Pending header',      'Pending section header OBX',            3,  true),
                (gen_random_uuid(), 'OBXSkipKeyword', 'COMMENTS',    'Comments header',     'Comments section header OBX',           4,  true),
                (gen_random_uuid(), 'OBXSkipKeyword', 'HEADER',      'Header row',          'Generic section header OBX',            5,  true),

                -- User roles
                (gen_random_uuid(), 'UserRole', 'pl_admin',      'Platform Admin',    'Platform-level administrator (all tenants)', 1,  true),
                (gen_random_uuid(), 'UserRole', 'clinic_admin',  'Clinic Admin',      'Clinic-level administrator',                 2,  true),
                (gen_random_uuid(), 'UserRole', 'charge_nurse',  'Charge Nurse',      'Charge nurse with scheduling rights',        3,  true),
                (gen_random_uuid(), 'UserRole', 'shift_nurse',   'Shift Nurse',       'Bedside shift nurse',                        4,  true),
                (gen_random_uuid(), 'UserRole', 'md',            'Physician (MD)',     'Nephrologist / attending physician',         5,  true),

                -- HL7 v2.x segment identifiers recognized by the parser
                -- Used when the file has no newlines and segments must be split by regex
                (gen_random_uuid(), 'Hl7SegmentId', 'MSH', 'Message Header',           'HL7 message header segment',             1,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'PID', 'Patient Identification',   'Patient demographics segment',           2,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'PV1', 'Patient Visit',            'Patient visit info segment',             3,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'PV2', 'Patient Visit Additional', 'Additional patient visit segment',       4,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'ORC', 'Common Order',             'Order control segment',                  5,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'OBR', 'Observation Request',      'Lab order / test panel segment',         6,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'OBX', 'Observation Result',       'Individual analyte result segment',      7,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'NTE', 'Notes and Comments',       'Free-text note segment',                 8,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'SPM', 'Specimen',                 'Specimen description segment',           9,  true),
                (gen_random_uuid(), 'Hl7SegmentId', 'SAC', 'Specimen Container',       'Specimen container segment',             10, true),
                (gen_random_uuid(), 'Hl7SegmentId', 'IN1', 'Insurance',                'Insurance segment',                      11, true)
            ON CONFLICT ("Category", "Code") DO NOTHING;
        """);
        logger.LogInformation("RefData seed ensured");

        // ── Test Patient: Cornelio Gallardo — 3 CBC result days ───────────────
        // Uses PL/pgSQL DO block with fixed UUIDs so it is fully idempotent.
        // Safe to run on every restart — ON CONFLICT DO NOTHING throughout.
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            DECLARE
              v_tenant_id  uuid;
              v_client_id  uuid;
              v_patient_id uuid := 'a1000000-0000-0000-0000-000000000001';
              v_hl7_1      uuid := 'a1000000-0000-0000-0000-000000000011';
              v_hl7_2      uuid := 'a1000000-0000-0000-0000-000000000012';
              v_hl7_3      uuid := 'a1000000-0000-0000-0000-000000000013';
              v_ord_1      uuid := 'a1000000-0000-0000-0000-000000000021';
              v_ord_2      uuid := 'a1000000-0000-0000-0000-000000000022';
              v_ord_3      uuid := 'a1000000-0000-0000-0000-000000000023';
              v_hdr_1      uuid := 'a1000000-0000-0000-0000-000000000031';
              v_hdr_2      uuid := 'a1000000-0000-0000-0000-000000000032';
              v_hdr_3      uuid := 'a1000000-0000-0000-0000-000000000033';
            BEGIN
              SELECT "Id" INTO v_tenant_id FROM "Tenants" WHERE "Code" = 'LABExpress' LIMIT 1;
              SELECT "Id" INTO v_client_id  FROM "Clients" WHERE "TenantId" = v_tenant_id LIMIT 1;
              IF v_tenant_id IS NULL OR v_client_id IS NULL THEN RETURN; END IF;

              -- Patient
              INSERT INTO "Patients" ("Id","TenantId","ClientId","LisPatientId","Name","Birthdate","Gender","IsActive","CreatedAt","UpdatedAt")
              VALUES (v_patient_id, v_tenant_id, v_client_id, 'CG00001', 'GALLARDO, CORNELIO', '1979-10-10', 'M', true, now(), now())
              ON CONFLICT ("Id") DO NOTHING;

              -- ── Day 1: 2026-03-05 ─────────────────────────────────────────
              INSERT INTO "Hl7Messages" ("Id","TenantId","MessageControlId","RawPayload","ReceivedAt","ProcessedFlag","QuarantineFlag")
              VALUES (v_hl7_1, v_tenant_id, 'SEED-CG-20260305', '[seeded] CBC GALLARDO CORNELIO 2026-03-05', '2026-03-05 07:00:00Z', true, false)
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "LabOrders" ("Id","TenantId","ClientId","PatientId","AccessionNumber","SourceHl7MessageId","ReleasedAt","CreatedAt")
              VALUES (v_ord_1, v_tenant_id, v_client_id, v_patient_id, 'CGACC001', v_hl7_1, '2026-03-05 07:00:00Z', '2026-03-05 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultHeaders" ("Id","TenantId","OrderId","SourceHl7MessageId","SxaTestId","CollectionDatetime","ResultDatetime")
              VALUES (v_hdr_1, v_tenant_id, v_ord_1, v_hl7_1, 'SXA_TEST_CBC', '2026-03-05 07:00:00Z', '2026-03-05 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultValues" ("Id","TenantId","ResultHeaderId","AnalyteCode","DisplayValue","ValueNumeric","Unit","ReferenceRangeLow","ReferenceRangeHigh","ReferenceRangeRaw","AbnormalFlag","NoSpecimen","NotCalculated","SchemaVersion")
              VALUES
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_HGB',   '9.2',  9.2,  'g/dL',   14.0, 18.0, '14.0 - 18.0', 'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_HCT',  '28.1', 28.1,  '%',      42.0, 52.0, '42.0 - 52.0', 'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_RBC',  '3.12', 3.12,  'x10¹²/L', 4.5,  6.1, '4.5 - 6.1',  'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MCV',  '90.1', 90.1,  'fL',     80.0,100.0, '80.0 - 100.0', null, false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MCH',  '29.5', 29.5,  'pg',     28.0, 32.0, '28.0 - 32.0', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MCHC', '32.7', 32.7,  'g/dL',   32.0, 36.0, '32.0 - 36.0', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_WBC',  '6.20', 6.20,  'x10⁹/L',  5.0, 10.0, '5.0 - 10.0',  null, false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_NEUT', '0.62', 0.62,  '',       0.50, 0.70, '0.50 - 0.70', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_LYMPH','0.25', 0.25,  '',       0.20, 0.40, '0.20 - 0.40', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MONO', '0.08', 0.08,  '',       0.03, 0.05, '0.03 - 0.05', 'H',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_EO',   '0.04', 0.04,  '',       0.00, 0.03, '0.00 - 0.03', 'H',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_BASO', '0.01', 0.01,  '',       0.00, 0.02, '0.00 - 0.02', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_PLT',   '188', 188,   'x10⁹/L',150.0,400.0,'150 - 400',   null,  false, false, 'DX7_CDM_1.0_A1')
              ON CONFLICT DO NOTHING;

              -- ── Day 2: 2026-03-10 ─────────────────────────────────────────
              INSERT INTO "Hl7Messages" ("Id","TenantId","MessageControlId","RawPayload","ReceivedAt","ProcessedFlag","QuarantineFlag")
              VALUES (v_hl7_2, v_tenant_id, 'SEED-CG-20260310', '[seeded] CBC GALLARDO CORNELIO 2026-03-10', '2026-03-10 07:00:00Z', true, false)
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "LabOrders" ("Id","TenantId","ClientId","PatientId","AccessionNumber","SourceHl7MessageId","ReleasedAt","CreatedAt")
              VALUES (v_ord_2, v_tenant_id, v_client_id, v_patient_id, 'CGACC002', v_hl7_2, '2026-03-10 07:00:00Z', '2026-03-10 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultHeaders" ("Id","TenantId","OrderId","SourceHl7MessageId","SxaTestId","CollectionDatetime","ResultDatetime")
              VALUES (v_hdr_2, v_tenant_id, v_ord_2, v_hl7_2, 'SXA_TEST_CBC', '2026-03-10 07:00:00Z', '2026-03-10 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultValues" ("Id","TenantId","ResultHeaderId","AnalyteCode","DisplayValue","ValueNumeric","Unit","ReferenceRangeLow","ReferenceRangeHigh","ReferenceRangeRaw","AbnormalFlag","NoSpecimen","NotCalculated","SchemaVersion")
              VALUES
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_HGB',  '10.4', 10.4,  'g/dL',   14.0, 18.0, '14.0 - 18.0', 'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_HCT',  '31.5', 31.5,  '%',      42.0, 52.0, '42.0 - 52.0', 'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_RBC',  '3.45', 3.45,  'x10¹²/L', 4.5,  6.1, '4.5 - 6.1',  'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MCV',  '91.3', 91.3,  'fL',     80.0,100.0, '80.0 - 100.0', null, false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MCH',  '30.1', 30.1,  'pg',     28.0, 32.0, '28.0 - 32.0', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MCHC', '33.0', 33.0,  'g/dL',   32.0, 36.0, '32.0 - 36.0', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_WBC',  '7.10', 7.10,  'x10⁹/L',  5.0, 10.0, '5.0 - 10.0',  null, false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_NEUT', '0.65', 0.65,  '',       0.50, 0.70, '0.50 - 0.70', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_LYMPH','0.23', 0.23,  '',       0.20, 0.40, '0.20 - 0.40', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MONO', '0.07', 0.07,  '',       0.03, 0.05, '0.03 - 0.05', 'H',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_EO',   '0.04', 0.04,  '',       0.00, 0.03, '0.00 - 0.03', 'H',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_BASO', '0.01', 0.01,  '',       0.00, 0.02, '0.00 - 0.02', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_PLT',   '204', 204,   'x10⁹/L',150.0,400.0,'150 - 400',   null,  false, false, 'DX7_CDM_1.0_A1')
              ON CONFLICT DO NOTHING;

              -- ── Day 3: 2026-03-15 ─────────────────────────────────────────
              INSERT INTO "Hl7Messages" ("Id","TenantId","MessageControlId","RawPayload","ReceivedAt","ProcessedFlag","QuarantineFlag")
              VALUES (v_hl7_3, v_tenant_id, 'SEED-CG-20260315', '[seeded] CBC GALLARDO CORNELIO 2026-03-15', '2026-03-15 07:00:00Z', true, false)
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "LabOrders" ("Id","TenantId","ClientId","PatientId","AccessionNumber","SourceHl7MessageId","ReleasedAt","CreatedAt")
              VALUES (v_ord_3, v_tenant_id, v_client_id, v_patient_id, 'CGACC003', v_hl7_3, '2026-03-15 07:00:00Z', '2026-03-15 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultHeaders" ("Id","TenantId","OrderId","SourceHl7MessageId","SxaTestId","CollectionDatetime","ResultDatetime")
              VALUES (v_hdr_3, v_tenant_id, v_ord_3, v_hl7_3, 'SXA_TEST_CBC', '2026-03-15 07:00:00Z', '2026-03-15 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultValues" ("Id","TenantId","ResultHeaderId","AnalyteCode","DisplayValue","ValueNumeric","Unit","ReferenceRangeLow","ReferenceRangeHigh","ReferenceRangeRaw","AbnormalFlag","NoSpecimen","NotCalculated","SchemaVersion")
              VALUES
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_HGB',  '11.8', 11.8,  'g/dL',   14.0, 18.0, '14.0 - 18.0', 'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_HCT',  '35.2', 35.2,  '%',      42.0, 52.0, '42.0 - 52.0', 'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_RBC',  '3.78', 3.78,  'x10¹²/L', 4.5,  6.1, '4.5 - 6.1',  'L',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MCV',  '93.1', 93.1,  'fL',     80.0,100.0, '80.0 - 100.0', null, false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MCH',  '31.2', 31.2,  'pg',     28.0, 32.0, '28.0 - 32.0', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MCHC', '33.5', 33.5,  'g/dL',   32.0, 36.0, '32.0 - 36.0', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_WBC',  '6.85', 6.85,  'x10⁹/L',  5.0, 10.0, '5.0 - 10.0',  null, false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_NEUT', '0.60', 0.60,  '',       0.50, 0.70, '0.50 - 0.70', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_LYMPH','0.27', 0.27,  '',       0.20, 0.40, '0.20 - 0.40', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MONO', '0.08', 0.08,  '',       0.03, 0.05, '0.03 - 0.05', 'H',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_EO',   '0.04', 0.04,  '',       0.00, 0.03, '0.00 - 0.03', 'H',   false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_BASO', '0.01', 0.01,  '',       0.00, 0.02, '0.00 - 0.02', null,  false, false, 'DX7_CDM_1.0_A1'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_PLT',   '221', 221,   'x10⁹/L',150.0,400.0,'150 - 400',   null,  false, false, 'DX7_CDM_1.0_A1')
              ON CONFLICT DO NOTHING;
            END $$;
        """);
        logger.LogInformation("Test patient GALLARDO CORNELIO seeded (3 CBC days)");

        await DbSeeder.SeedAsync(db);
        logger.LogInformation("DB seed complete");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "DB init failed: {Message}", ex.Message);
        throw; // crash on startup so the error is visible
    }
}

// Serve wwwroot (avatars, etc.) — create folder if missing
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwrootPath);
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwrootPath),
    RequestPath = ""
});
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();