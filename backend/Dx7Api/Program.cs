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
                ('SXA_TEST_MULTI',    'Multi-Panel (Fallback)',               'Chemistry',  'PANEL',  true)
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
                ('SXA_A_CREAT',    'Creatinine',                  'µmol/L',    'NUMERIC'),
                ('SXA_A_ALB',      'Albumin',                     'g/dL',      'NUMERIC'),
                ('SXA_A_ALKP',     'Alkaline Phosphatase',        'U/L',       'NUMERIC'),
                ('SXA_A_UA',       'Uric Acid',                   'µmol/L',    'NUMERIC'),
                ('SXA_A_PHOS',     'Inorganic Phosphorus',        'mg/dL',     'NUMERIC'),
                ('SXA_A_NA',       'Sodium',                      'mEq/L',     'NUMERIC'),
                ('SXA_A_K',        'Potassium',                   'mEq/L',     'NUMERIC'),
                ('SXA_A_CA',       'Calcium',                     'mg/dL',     'NUMERIC'),
                ('SXA_A_ICAL',     'Ionized Calcium',             'mmol/L',    'NUMERIC'),
                ('SXA_A_GLU',      'Glucose (FBS)',               'mg/dL',     'NUMERIC'),
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
                ('K',       'SXA_TEST_K'),
                ('LIPID',   'SXA_TEST_LIPID'),
                ('CHEM',    'SXA_TEST_CHEM'),
                ('MULTI',   'SXA_TEST_MULTI')
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
                ('CREA',    'SXA_A_CREAT'),
                ('ALB',     'SXA_A_ALB'),
                ('ALKP',    'SXA_A_ALKP'),
                ('UA',      'SXA_A_UA'),
                ('PHOS',    'SXA_A_PHOS'),
                ('NA',      'SXA_A_NA'),
                ('K',       'SXA_A_K'),
                ('ICAL',    'SXA_A_ICAL'),
                ('FBS',     'SXA_A_GLU'),
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

              INSERT INTO "Orders" ("Id","TenantId","ClientId","PatientId","AccessionNumber","SourceHl7MessageId","ReleasedAt","CreatedAt")
              VALUES (v_ord_1, v_tenant_id, v_client_id, v_patient_id, 'CGACC001', v_hl7_1, '2026-03-05 07:00:00Z', '2026-03-05 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultHeaders" ("Id","TenantId","OrderId","SourceHl7MessageId","SxaTestId","CollectionDatetime","ResultDatetime")
              VALUES (v_hdr_1, v_tenant_id, v_ord_1, v_hl7_1, 'SXA_TEST_CBC', '2026-03-05 07:00:00Z', '2026-03-05 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultValues" ("Id","TenantId","ResultHeaderId","AnalyteCode","DisplayValue","ValueNumeric","Unit","ReferenceRangeLow","ReferenceRangeHigh","ReferenceRangeRaw","AbnormalFlag")
              VALUES
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_HGB',   '9.2',  9.2,  'g/dL',   14.0, 18.0, '14.0 - 18.0', 'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_HCT',  '28.1', 28.1,  '%',      42.0, 52.0, '42.0 - 52.0', 'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_RBC',  '3.12', 3.12,  'x10¹²/L', 4.5,  6.1, '4.5 - 6.1',  'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MCV',  '90.1', 90.1,  'fL',     80.0,100.0, '80.0 - 100.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MCH',  '29.5', 29.5,  'pg',     28.0, 32.0, '28.0 - 32.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MCHC', '32.7', 32.7,  'g/dL',   32.0, 36.0, '32.0 - 36.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_WBC',  '6.20', 6.20,  'x10⁹/L',  5.0, 10.0, '5.0 - 10.0',  null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_NEUT', '0.62', 0.62,  '',       0.50, 0.70, '0.50 - 0.70', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_LYMPH','0.25', 0.25,  '',       0.20, 0.40, '0.20 - 0.40', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_MONO', '0.08', 0.08,  '',       0.03, 0.05, '0.03 - 0.05', 'H'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_EO',   '0.04', 0.04,  '',       0.00, 0.03, '0.00 - 0.03', 'H'),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_BASO', '0.01', 0.01,  '',       0.00, 0.02, '0.00 - 0.02', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_1, 'SXA_A_PLT',   '188', 188,   'x10⁹/L',150.0,400.0,'150 - 400',   null)
              ON CONFLICT DO NOTHING;

              -- ── Day 2: 2026-03-10 ─────────────────────────────────────────
              INSERT INTO "Hl7Messages" ("Id","TenantId","MessageControlId","RawPayload","ReceivedAt","ProcessedFlag","QuarantineFlag")
              VALUES (v_hl7_2, v_tenant_id, 'SEED-CG-20260310', '[seeded] CBC GALLARDO CORNELIO 2026-03-10', '2026-03-10 07:00:00Z', true, false)
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "Orders" ("Id","TenantId","ClientId","PatientId","AccessionNumber","SourceHl7MessageId","ReleasedAt","CreatedAt")
              VALUES (v_ord_2, v_tenant_id, v_client_id, v_patient_id, 'CGACC002', v_hl7_2, '2026-03-10 07:00:00Z', '2026-03-10 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultHeaders" ("Id","TenantId","OrderId","SourceHl7MessageId","SxaTestId","CollectionDatetime","ResultDatetime")
              VALUES (v_hdr_2, v_tenant_id, v_ord_2, v_hl7_2, 'SXA_TEST_CBC', '2026-03-10 07:00:00Z', '2026-03-10 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultValues" ("Id","TenantId","ResultHeaderId","AnalyteCode","DisplayValue","ValueNumeric","Unit","ReferenceRangeLow","ReferenceRangeHigh","ReferenceRangeRaw","AbnormalFlag")
              VALUES
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_HGB',  '10.4', 10.4,  'g/dL',   14.0, 18.0, '14.0 - 18.0', 'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_HCT',  '31.5', 31.5,  '%',      42.0, 52.0, '42.0 - 52.0', 'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_RBC',  '3.45', 3.45,  'x10¹²/L', 4.5,  6.1, '4.5 - 6.1',  'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MCV',  '91.3', 91.3,  'fL',     80.0,100.0, '80.0 - 100.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MCH',  '30.1', 30.1,  'pg',     28.0, 32.0, '28.0 - 32.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MCHC', '33.0', 33.0,  'g/dL',   32.0, 36.0, '32.0 - 36.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_WBC',  '7.10', 7.10,  'x10⁹/L',  5.0, 10.0, '5.0 - 10.0',  null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_NEUT', '0.65', 0.65,  '',       0.50, 0.70, '0.50 - 0.70', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_LYMPH','0.23', 0.23,  '',       0.20, 0.40, '0.20 - 0.40', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_MONO', '0.07', 0.07,  '',       0.03, 0.05, '0.03 - 0.05', 'H'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_EO',   '0.04', 0.04,  '',       0.00, 0.03, '0.00 - 0.03', 'H'),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_BASO', '0.01', 0.01,  '',       0.00, 0.02, '0.00 - 0.02', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_2, 'SXA_A_PLT',   '204', 204,   'x10⁹/L',150.0,400.0,'150 - 400',   null)
              ON CONFLICT DO NOTHING;

              -- ── Day 3: 2026-03-15 ─────────────────────────────────────────
              INSERT INTO "Hl7Messages" ("Id","TenantId","MessageControlId","RawPayload","ReceivedAt","ProcessedFlag","QuarantineFlag")
              VALUES (v_hl7_3, v_tenant_id, 'SEED-CG-20260315', '[seeded] CBC GALLARDO CORNELIO 2026-03-15', '2026-03-15 07:00:00Z', true, false)
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "Orders" ("Id","TenantId","ClientId","PatientId","AccessionNumber","SourceHl7MessageId","ReleasedAt","CreatedAt")
              VALUES (v_ord_3, v_tenant_id, v_client_id, v_patient_id, 'CGACC003', v_hl7_3, '2026-03-15 07:00:00Z', '2026-03-15 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultHeaders" ("Id","TenantId","OrderId","SourceHl7MessageId","SxaTestId","CollectionDatetime","ResultDatetime")
              VALUES (v_hdr_3, v_tenant_id, v_ord_3, v_hl7_3, 'SXA_TEST_CBC', '2026-03-15 07:00:00Z', '2026-03-15 07:00:00Z')
              ON CONFLICT ("Id") DO NOTHING;

              INSERT INTO "ResultValues" ("Id","TenantId","ResultHeaderId","AnalyteCode","DisplayValue","ValueNumeric","Unit","ReferenceRangeLow","ReferenceRangeHigh","ReferenceRangeRaw","AbnormalFlag")
              VALUES
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_HGB',  '11.8', 11.8,  'g/dL',   14.0, 18.0, '14.0 - 18.0', 'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_HCT',  '35.2', 35.2,  '%',      42.0, 52.0, '42.0 - 52.0', 'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_RBC',  '3.78', 3.78,  'x10¹²/L', 4.5,  6.1, '4.5 - 6.1',  'L'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MCV',  '93.1', 93.1,  'fL',     80.0,100.0, '80.0 - 100.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MCH',  '31.2', 31.2,  'pg',     28.0, 32.0, '28.0 - 32.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MCHC', '33.5', 33.5,  'g/dL',   32.0, 36.0, '32.0 - 36.0', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_WBC',  '6.85', 6.85,  'x10⁹/L',  5.0, 10.0, '5.0 - 10.0',  null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_NEUT', '0.60', 0.60,  '',       0.50, 0.70, '0.50 - 0.70', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_LYMPH','0.27', 0.27,  '',       0.20, 0.40, '0.20 - 0.40', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_MONO', '0.08', 0.08,  '',       0.03, 0.05, '0.03 - 0.05', 'H'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_EO',   '0.04', 0.04,  '',       0.00, 0.03, '0.00 - 0.03', 'H'),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_BASO', '0.01', 0.01,  '',       0.00, 0.02, '0.00 - 0.02', null),
                (gen_random_uuid(), v_tenant_id, v_hdr_3, 'SXA_A_PLT',   '221', 221,   'x10⁹/L',150.0,400.0,'150 - 400',   null)
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