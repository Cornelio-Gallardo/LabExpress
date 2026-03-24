using Dx7Api.Data;
using Dx7Api.Middleware;
using Dx7Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── IHttpContextAccessor (required by TenantRlsInterceptor) ──────────────────
builder.Services.AddHttpContextAccessor();

// ── RLS interceptor (singleton — safe because it uses IHttpContextAccessor) ──
builder.Services.AddSingleton<TenantRlsInterceptor>();

// ── Database with RLS interceptor ────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>((sp, opts) =>
{
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    opts.AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
});

// ── JWT Auth — reads token from httpOnly cookie (F-09) ───────────────────────
// Token is never exposed to JavaScript; the browser sends the cookie automatically.
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Read JWT from httpOnly cookie instead of Authorization header
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                ctx.Token = ctx.Request.Cookies["dx7_token"];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<Dx7Api.Services.Hl7.Hl7Processor>();
builder.Services.AddHttpClient();                           // IHttpClientFactory for SSO
builder.Services.AddHostedService<Hl7FileWatcherService>(); // HL7 background watcher
builder.Services.AddControllers();

// ── CORS — must use AllowCredentials() to allow cookie transport ─────────────
// AllowCredentials() requires explicit origins (no wildcard).
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // Required: browser only sends cookies for credentialed requests

// ── Cookie policy (Secure flag conditional on environment) ───────────────────
builder.Services.Configure<CookiePolicyOptions>(opts =>
{
    opts.HttpOnly    = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
    opts.Secure      = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest   // allow http in local dev
        : CookieSecurePolicy.Always;         // enforce HTTPS in production
    opts.MinimumSameSitePolicy = SameSiteMode.Lax;
});

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Dx7 API", Version = "v1" });
    c.AddSecurityDefinition("cookieAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In   = ParameterLocation.Cookie,
        Name = "dx7_token",
        Description = "httpOnly session cookie set by POST /api/auth/login"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "cookieAuth" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── Ensure the database exists (CREATE DATABASE if missing) ───────────────────
// MigrateAsync requires the DB to exist first. Connect to the postgres maintenance
// database and create "dx7" (or whatever the DB name is) if it is absent.
{
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;
    var builder2 = new NpgsqlConnectionStringBuilder(connStr);
    var dbName   = builder2.Database!;
    builder2.Database = "postgres"; // maintenance DB — always exists

    await using var adminConn = new NpgsqlConnection(builder2.ToString());
    await adminConn.OpenAsync();
    await using var chkCmd = adminConn.CreateCommand();
    chkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = $1";
    chkCmd.Parameters.AddWithValue(dbName);
    var exists = await chkCmd.ExecuteScalarAsync() is not null;
    if (!exists)
    {
        await using var createCmd = adminConn.CreateCommand();
        // Identifier cannot be parameterized — dbName comes from our own config.
        createCmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await createCmd.ExecuteNonQueryAsync();
        app.Logger.LogInformation("Created database '{Db}'", dbName);
    }
}

// ── Auto-migrate, seed, and patch SXA catalog ────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // ── Migration history backfill ────────────────────────────────────────────
    // If the database was created outside of EF (init.sql / EnsureCreated) the
    // __EFMigrationsHistory table may be empty or absent even though all tables
    // already exist.  MigrateAsync() would then try to re-run every migration
    // from scratch, hitting "relation already exists" on the very first one.
    //
    // Fix: use a raw ADO.NET command on the underlying connection so that
    // TenantRlsInterceptor (which only intercepts EF-Core commands) is bypassed,
    // then insert history rows for every migration whose structural artefacts are
    // already present, using ON CONFLICT DO NOTHING so the block is fully
    // idempotent and safe to run on a brand-new DB as well.
    {
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();

            // 1. Ensure the history table itself exists.
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId"    character varying(150) NOT NULL,
                    "ProductVersion" character varying(32)  NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                """;
            await cmd.ExecuteNonQueryAsync();

            // 2. Pre-CDM migrations (InitialCreate → AddResultStatus).
            //    Backfill when the "Tenants" table already exists.
            cmd.CommandText = """
                DO $$
                BEGIN
                  IF EXISTS (
                      SELECT 1 FROM information_schema.tables
                      WHERE table_schema = 'public' AND table_name = 'Tenants'
                  ) THEN
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
                        ('20260306024449_InitialCreate',         '8.0.0'),
                        ('20260306033220_ShiftManagement',       '8.0.0'),
                        ('20260306061402_MultiTenantHardening',  '8.0.0'),
                        ('20260306063855_RoleDefinitions',       '8.0.0'),
                        ('20260306070709_AddPhilhealthNo',       '8.0.0'),
                        ('20260306070829_AddPhilhealthAndRoles', '8.0.0'),
                        ('20260306100847_AddResultStatus',       '8.0.0')
                    ON CONFLICT ("MigrationId") DO NOTHING;
                  END IF;
                END$$;
                """;
            await cmd.ExecuteNonQueryAsync();

            // 3a. Invalidate a stale AddCdmSchema history row.
            //     If a previous startup incorrectly backfilled AddCdmSchema as "done" but the
            //     "Superseded" column on ResultValues is still missing, remove that row so that
            //     MigrateAsync will re-run the migration and actually add the column.
            //     AddCdmSchema is fully idempotent (all CREATE TABLE / ADD COLUMN use IF NOT
            //     EXISTS), so re-running it is always safe.
            cmd.CommandText = """
                DO $$
                BEGIN
                  IF NOT EXISTS (
                      SELECT 1 FROM information_schema.columns
                      WHERE table_schema = 'public'
                        AND table_name   = 'ResultValues'
                        AND column_name  = 'Superseded'
                  ) THEN
                    DELETE FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20260323000001_AddCdmSchema';
                  END IF;
                END$$;
                """;
            await cmd.ExecuteNonQueryAsync();

            // 3b. AddCdmSchema — backfill ONLY when both "Hl7Messages" and the "Superseded"
            //     column already exist (proving the migration has fully run).
            cmd.CommandText = """
                DO $$
                BEGIN
                  IF EXISTS (
                      SELECT 1 FROM information_schema.tables
                      WHERE table_schema = 'public' AND table_name = 'Hl7Messages'
                  ) AND EXISTS (
                      SELECT 1 FROM information_schema.columns
                      WHERE table_schema = 'public'
                        AND table_name   = 'ResultValues'
                        AND column_name  = 'Superseded'
                  ) THEN
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ('20260323000001_AddCdmSchema', '8.0.0')
                    ON CONFLICT ("MigrationId") DO NOTHING;
                  END IF;
                END$$;
                """;
            await cmd.ExecuteNonQueryAsync();

            // 4. EnableRls — backfill when an RLS policy already exists on "Clients".
            //    ("Tenants" is excluded from RLS and has no policies — never use it as the probe.)
            cmd.CommandText = """
                DO $$
                BEGIN
                  IF EXISTS (
                      SELECT 1 FROM pg_policies
                      WHERE schemaname = 'public' AND tablename = 'Clients'
                  ) THEN
                    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ('20260324000001_EnableRls', '8.0.0')
                    ON CONFLICT ("MigrationId") DO NOTHING;
                  END IF;
                END$$;
                """;
            await cmd.ExecuteNonQueryAsync();

            // 5. PatchF12Mappings — backfill when the CREA mapping already exists.
            //    The second EXISTS references "TenantTestMaps" directly, which PL/pgSQL would
            //    try to compile even when the table is absent.  Use EXECUTE (dynamic SQL) so
            //    the table reference is only resolved after the table-existence check passes.
            cmd.CommandText = """
                DO $$
                DECLARE v_has_crea boolean := false;
                BEGIN
                  IF EXISTS (
                      SELECT 1 FROM information_schema.tables
                      WHERE table_schema = 'public' AND table_name = 'TenantTestMaps'
                  ) THEN
                    EXECUTE $q$
                      SELECT EXISTS (
                        SELECT 1 FROM "TenantTestMaps" WHERE "TenantTestCode" = 'CREA'
                      )
                    $q$ INTO v_has_crea;
                    IF v_has_crea THEN
                      INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                      VALUES ('20260324000002_PatchF12Mappings', '8.0.0')
                      ON CONFLICT ("MigrationId") DO NOTHING;
                    END IF;
                  END IF;
                END$$;
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    await db.Database.MigrateAsync();

    // Idempotent SXA catalog seed (ON CONFLICT DO NOTHING = safe to run every startup).
    // Must run BEFORE DbSeeder because DbSeeder inserts TenantTestMaps rows that have
    // FK references into SxaTestCatalog and SxaAnalytes.
    await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "SxaTestCatalog" ("SxaTestId", "CanonicalName", "Category", "ResultType", "ActiveFlag") VALUES
            ('SXA_TEST_BUN_PRE',  'BUN Pre-Dialysis',        'RENAL',      'SINGLE', true),
            ('SXA_TEST_BUN_POST', 'BUN Post-Dialysis',       'RENAL',      'SINGLE', true),
            ('SXA_TEST_CBC',      'Complete Blood Count',    'HEMATOLOGY', 'PANEL',  true),
            ('SXA_TEST_FBS',      'Fasting Blood Sugar',     'CHEMISTRY',  'SINGLE', true),
            ('SXA_TEST_K',        'Potassium',               'RENAL',      'SINGLE', true),
            ('SXA_TEST_LIPID',    'Lipid Panel',             'LIPID',      'PANEL',  true),
            ('SXA_TEST_CHEM',     'General Chemistry Panel', 'CHEMISTRY',  'PANEL',  true),
            ('SXA_TEST_MULTI',    'Multi-Test Panel',        'CHEMISTRY',  'PANEL',  true)
        ON CONFLICT ("SxaTestId") DO NOTHING;

        INSERT INTO "SxaAnalytes" ("AnalyteCode", "DisplayName", "DefaultUnit", "ResultType") VALUES
            -- Hematology
            ('SXA_A_HGB',      'Hemoglobin',                 'g/dL',    'NUMERIC'),
            ('SXA_A_HCT',      'Hematocrit',                 '%',       'NUMERIC'),
            ('SXA_A_RBC',      'Red Blood Cells',            '10^12/L', 'NUMERIC'),
            ('SXA_A_WBC',      'White Blood Cells',          '10^9/L',  'NUMERIC'),
            ('SXA_A_PLT',      'Platelets',                  '10^9/L',  'NUMERIC'),
            ('SXA_A_MCV',      'Mean Corpuscular Volume',    'fL',      'NUMERIC'),
            ('SXA_A_MCH',      'Mean Corpuscular Hgb',       'pg',      'NUMERIC'),
            ('SXA_A_MCHC',     'MCHC',                       'g/dL',    'NUMERIC'),
            ('SXA_A_NEUT',     'Neutrophils',                '%',       'NUMERIC'),
            ('SXA_A_LYMPH',    'Lymphocytes',                '%',       'NUMERIC'),
            ('SXA_A_MONO',     'Monocytes',                  '%',       'NUMERIC'),
            ('SXA_A_EO',       'Eosinophils',                '%',       'NUMERIC'),
            ('SXA_A_BASO',     'Basophils',                  '%',       'NUMERIC'),
            -- Renal / Dialysis
            ('SXA_A_BUN_PRE',  'BUN Pre-Dialysis',           'mmol/L',  'NUMERIC'),
            ('SXA_A_BUN_POST', 'BUN Post-Dialysis',          'mmol/L',  'NUMERIC'),
            ('SXA_A_URR',      'Urea Reduction Ratio',       '%',       'NUMERIC'),
            ('SXA_A_KTV',      'Kt/V',                       NULL,      'NUMERIC'),
            ('SXA_A_CREA',     'Creatinine',                 'umol/L',  'NUMERIC'),
            ('SXA_A_ALB',      'Albumin',                    'g/L',     'NUMERIC'),
            ('SXA_A_ALKP',     'Alkaline Phosphatase',       'U/L',     'NUMERIC'),
            ('SXA_A_UA',       'Uric Acid',                  'umol/L',  'NUMERIC'),
            ('SXA_A_PHOS',     'Phosphorus',                 'mmol/L',  'NUMERIC'),
            ('SXA_A_NA',       'Sodium',                     'mmol/L',  'NUMERIC'),
            ('SXA_A_K',        'Potassium',                  'mmol/L',  'NUMERIC'),
            ('SXA_A_ICAL',     'Ionized Calcium',            'mmol/L',  'NUMERIC'),
            ('SXA_A_FBS',      'Fasting Blood Sugar',        'mmol/L',  'NUMERIC'),
            ('SXA_A_ALT',      'Alanine Aminotransferase',   'U/L',     'NUMERIC'),
            ('SXA_A_HBA1C',    'HbA1c',                      '%',       'NUMERIC'),
            -- Lipid panel
            ('SXA_A_CHOL',     'Total Cholesterol',          'mmol/L',  'NUMERIC'),
            ('SXA_A_TRIG',     'Triglycerides',              'mmol/L',  'NUMERIC'),
            ('SXA_A_HDL',      'HDL Cholesterol',            'mmol/L',  'NUMERIC'),
            ('SXA_A_LDL',      'LDL Cholesterol',            'mmol/L',  'NUMERIC'),
            ('SXA_A_VLDL',     'VLDL Cholesterol',           'mmol/L',  'NUMERIC')
        ON CONFLICT ("AnalyteCode") DO NOTHING;
        """);

    // Seed tenant/users/roles/maps only after the SXA catalog exists.
    await DbSeeder.SeedAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
