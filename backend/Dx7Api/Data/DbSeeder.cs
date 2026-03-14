using Dx7Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Tenants.AnyAsync()) return;

        // ── Tenant ────────────────────────────────────────────────────────────
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "LABExpress", Code = "LABExpress", PrimaryColor = "#0D7377", IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // ── Client ────────────────────────────────────────────────────────────
        var client = new Client { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Metro Dialysis Center", Code = "MDC", Address = "123 Health Ave., Manila", IsActive = true };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        // ── Users ─────────────────────────────────────────────────────────────
        var users = new List<User>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id, Email = "admin@dx7.local",   PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),  Name = "Clinic Admin",     Role = UserRole.clinic_admin, IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id, Email = "charge@dx7.local",  PasswordHash = BCrypt.Net.BCrypt.HashPassword("Nurse@1234"),   Name = "Ana Reyes",        Role = UserRole.charge_nurse, IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id, Email = "nurse@dx7.local",   PasswordHash = BCrypt.Net.BCrypt.HashPassword("Nurse@1234"),   Name = "Ben Santos",       Role = UserRole.shift_nurse,  IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id, Email = "md@dx7.local",      PasswordHash = BCrypt.Net.BCrypt.HashPassword("Doctor@1234"),  Name = "Dr. Maria Cruz",   Role = UserRole.md,           IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = null,      Email = "pladmin@dx7.local", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),   Name = "PL Administrator", Role = UserRole.pl_admin,     IsActive = true },
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        // ── Roles ─────────────────────────────────────────────────────────────
        db.RoleDefinitions.AddRange(
            new() { TenantId = tenant.Id, RoleKey = "charge_nurse",  Label = "Charge Nurse",  Description = "Manages shift roster, assigns chairs, views all results, exports reports", SortOrder = 1 },
            new() { TenantId = tenant.Id, RoleKey = "shift_nurse",   Label = "Shift Nurse",   Description = "Views patient results and MD notes. Read-only access.",                   SortOrder = 2 },
            new() { TenantId = tenant.Id, RoleKey = "md",            Label = "Nephrologist",  Description = "Views results and writes/edits session notes (24hr edit window)",         SortOrder = 3 },
            new() { TenantId = tenant.Id, RoleKey = "clinic_admin",  Label = "Clinic Admin",  Description = "Full access to clinic: manage users, patients, sessions, export",         SortOrder = 4 },
            new() { TenantId = tenant.Id, RoleKey = "pl_admin",      Label = "PL Admin",      Description = "Partner Lab admin — manages all clinics under the tenant",                SortOrder = 5 }
        );
        await db.SaveChangesAsync();

        // ── Patients ──────────────────────────────────────────────────────────
        var patientData = new[]
        {
            ("DELA CRUZ, Juan", "P001", "PH-0412-8871-2", "M", new DateOnly(1965,  3, 12), "09171234501"),
            ("SANTOS, Maria",   "P002", "PH-0318-4421-7", "F", new DateOnly(1972,  7, 25), "09181234502"),
            ("REYES, Pedro",    "P003", "PH-0509-3312-5", "M", new DateOnly(1958, 11,  4), "09191234503"),
            ("LOPEZ, Jose",     "P004", "PH-0621-7743-1", "M", new DateOnly(1970,  1, 30), "09201234504"),
            ("BAUTISTA, Carlo", "P005", "PH-0734-5512-9", "M", new DateOnly(1983,  9, 17), "09211234505"),
            ("GARCIA, Rosa",    "P006", "PH-0845-2298-3", "F", new DateOnly(1961,  5, 22), "09221234506"),
            ("TORRES, Elena",   "P007", "PH-0956-8834-6", "F", new DateOnly(1975,  8,  9), "09231234507"),
            ("FLORES, Miguel",  "P008", "PH-1067-1123-4", "M", new DateOnly(1968, 12,  3), "09241234508"),
        };
        var patients = patientData.Select(p => new Patient
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id,
            Name = p.Item1, LisPatientId = p.Item2, PhilhealthNo = p.Item3,
            Gender = p.Item4, Birthdate = p.Item5, ContactNumber = p.Item6, IsActive = true
        }).ToList();
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        // ── §5 SXA Test Catalog ───────────────────────────────────────────────
        db.SxaTests.AddRange(
            new() { SxaTestId = "SXA_TEST_CBC",      CanonicalName = "Complete Blood Count",               Category = "Hematology",  ResultType = SxaResultType.PANEL,  ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_CHEM",     CanonicalName = "Chemistry Panel",                    Category = "Chemistry",   ResultType = SxaResultType.PANEL,  ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_FBS",      CanonicalName = "Fasting Blood Sugar",                Category = "Chemistry",   ResultType = SxaResultType.SINGLE, ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_BUN_PRE",  CanonicalName = "Blood Urea Nitrogen (Pre-Dialysis)", Category = "Chemistry",   ResultType = SxaResultType.SINGLE, ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_BUN_POST", CanonicalName = "Blood Urea Nitrogen (Post-Dialysis)",Category = "Chemistry",   ResultType = SxaResultType.SINGLE, ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_LIPID",    CanonicalName = "Lipid Panel",                        Category = "Chemistry",   ResultType = SxaResultType.PANEL,  ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_K",        CanonicalName = "Potassium",                          Category = "Chemistry",   ResultType = SxaResultType.SINGLE, ActiveFlag = true },
            new() { SxaTestId = "SXA_TEST_MULTI",    CanonicalName = "Multi-Panel (Fallback)",             Category = "Chemistry",   ResultType = SxaResultType.PANEL,  ActiveFlag = true }
        );
        await db.SaveChangesAsync();

        // ── §5 SXA Analyte Catalog — full HCLab dialysis scope ───────────────
        db.SxaAnalytes.AddRange(
            // Hematology — CBC
            new() { AnalyteCode = "SXA_A_HGB",   DisplayName = "Hemoglobin",             DefaultUnit = "g/dL",    ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_HCT",   DisplayName = "Hematocrit",             DefaultUnit = "%",       ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_RBC",   DisplayName = "RBC",                    DefaultUnit = "x10¹²/L", ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_WBC",   DisplayName = "WBC",                    DefaultUnit = "x10⁹/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_PLT",   DisplayName = "Platelets",              DefaultUnit = "x10⁹/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_MCV",   DisplayName = "MCV",                    DefaultUnit = "fL",      ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_MCH",   DisplayName = "MCH",                    DefaultUnit = "pg",      ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_MCHC",  DisplayName = "MCHC",                   DefaultUnit = "g/dL",    ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_NEUT",  DisplayName = "Neutrophils",            DefaultUnit = "",        ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_LYMPH", DisplayName = "Lymphocytes",            DefaultUnit = "",        ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_MONO",  DisplayName = "Monocytes",              DefaultUnit = "",        ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_EO",    DisplayName = "Eosinophils",            DefaultUnit = "",        ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_BASO",  DisplayName = "Basophils",              DefaultUnit = "",        ResultType = SxaAnalyteResultType.NUMERIC },
            // Chemistry — Renal / Dialysis
            new() { AnalyteCode = "SXA_A_BUN_PRE",  DisplayName = "BUN Pre-Dialysis",   DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_BUN_POST", DisplayName = "BUN Post-Dialysis",  DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_URR",   DisplayName = "URR",                    DefaultUnit = "%",       ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_KTV",   DisplayName = "Kt/V",                   DefaultUnit = "",        ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_CREAT", DisplayName = "Creatinine",             DefaultUnit = "µmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_ALB",   DisplayName = "Albumin",                DefaultUnit = "g/L",     ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_ALKP",  DisplayName = "Alkaline Phosphatase",   DefaultUnit = "U/L",     ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_UA",    DisplayName = "Uric Acid",              DefaultUnit = "µmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_PHOS",  DisplayName = "Inorganic Phosphorus",   DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_NA",    DisplayName = "Sodium",                 DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_K",     DisplayName = "Potassium",              DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_ICAL",  DisplayName = "Ionized Calcium",        DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_GLU",   DisplayName = "Glucose (FBS)",          DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_ALT",   DisplayName = "SGPT (ALT)",             DefaultUnit = "U/L",     ResultType = SxaAnalyteResultType.NUMERIC },
            // Lipid panel
            new() { AnalyteCode = "SXA_A_CHOL",  DisplayName = "Cholesterol",            DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_TRIG",  DisplayName = "Triglycerides",          DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_HDL",   DisplayName = "HDL Cholesterol",        DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_LDL",   DisplayName = "LDL Cholesterol",        DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC },
            new() { AnalyteCode = "SXA_A_VLDL",  DisplayName = "VLDL Cholesterol",       DefaultUnit = "mmol/L",  ResultType = SxaAnalyteResultType.NUMERIC }
        );
        await db.SaveChangesAsync();

        // ── §6.1 TenantTestMaps — OBR-4 codes from HCLab ────────────────────
        db.TenantTestMaps.AddRange(
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "BUNPRE",   SxaTestId = "SXA_TEST_BUN_PRE",  IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "BUNPOST",  SxaTestId = "SXA_TEST_BUN_POST", IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "DGC0074",  SxaTestId = "SXA_TEST_CBC",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "DGC0035",  SxaTestId = "SXA_TEST_FBS",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "K",        SxaTestId = "SXA_TEST_K",        IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "LIPID",    SxaTestId = "SXA_TEST_LIPID",    IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "CHEM",     SxaTestId = "SXA_TEST_CHEM",     IsActive = true },
            // Fallback for any unmapped OBR-4 — prevents full message quarantine
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "MULTI",    SxaTestId = "SXA_TEST_MULTI",    IsActive = true }
        );
        await db.SaveChangesAsync();

        // ── §6.2 TenantAnalyteMaps — all OBX-3 codes from real HCLab file ───
        db.TenantAnalyteMaps.AddRange(
            // Hematology
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "HGB",   AnalyteCode = "SXA_A_HGB",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "HCT",   AnalyteCode = "SXA_A_HCT",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "RBC",   AnalyteCode = "SXA_A_RBC",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "WBC",   AnalyteCode = "SXA_A_WBC",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "PLT",   AnalyteCode = "SXA_A_PLT",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "MCV",   AnalyteCode = "SXA_A_MCV",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "MCH",   AnalyteCode = "SXA_A_MCH",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "MCHC",  AnalyteCode = "SXA_A_MCHC",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "NEUT",  AnalyteCode = "SXA_A_NEUT",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "LYMPH", AnalyteCode = "SXA_A_LYMPH",    IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "MONO",  AnalyteCode = "SXA_A_MONO",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "EO",    AnalyteCode = "SXA_A_EO",       IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "BASO",  AnalyteCode = "SXA_A_BASO",     IsActive = true },
            // Chemistry — Renal / Dialysis
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "BUNPRE",  AnalyteCode = "SXA_A_BUN_PRE",  IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "BUNPOS",  AnalyteCode = "SXA_A_BUN_POST", IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "URR",     AnalyteCode = "SXA_A_URR",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "KT/V",    AnalyteCode = "SXA_A_KTV",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "CREA",    AnalyteCode = "SXA_A_CREAT",    IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "ALB",     AnalyteCode = "SXA_A_ALB",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "ALKP",    AnalyteCode = "SXA_A_ALKP",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "UA",      AnalyteCode = "SXA_A_UA",       IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "PHOS",    AnalyteCode = "SXA_A_PHOS",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "NA",      AnalyteCode = "SXA_A_NA",       IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "K",       AnalyteCode = "SXA_A_K",        IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "ICAL",    AnalyteCode = "SXA_A_ICAL",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "FBS",     AnalyteCode = "SXA_A_GLU",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "ALT",     AnalyteCode = "SXA_A_ALT",      IsActive = true },
            // Lipid panel
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "CHOL",    AnalyteCode = "SXA_A_CHOL",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "TRIG",    AnalyteCode = "SXA_A_TRIG",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "HDL",     AnalyteCode = "SXA_A_HDL",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "LDL",     AnalyteCode = "SXA_A_LDL",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "VLDL",    AnalyteCode = "SXA_A_VLDL",     IsActive = true }
        );
        await db.SaveChangesAsync();

        // ── Seeded demo results — base values randomized ±15% per patient/date
        var cbcAnalytes = new[] {
            ("SXA_A_WBC",  5.2,   "x10⁹/L",  5.0m,  10.0m),
            ("SXA_A_HGB",  112.0, "g/dL",    140m,  180m ),
            ("SXA_A_HCT",  0.34,  "%",        42m,   52m  ),
            ("SXA_A_PLT",  210.0, "x10⁹/L",  150m,  400m ),
            ("SXA_A_RBC",  4.5,   "x10¹²/L", 4.5m,  6.1m ),
        };
        var chemAnalytes = new[] {
            ("SXA_A_BUN_PRE",  42.0, "mmol/L", 2.99m, 8.82m),
            ("SXA_A_BUN_POST", 18.0, "mmol/L", 2.99m, 8.82m),
            ("SXA_A_CREAT",   450.0, "µmol/L", 55m,   115m ),
            ("SXA_A_K",        5.1,  "mmol/L", 3.5m,  5.5m ),
            ("SXA_A_NA",      138.0, "mmol/L", 135m,  145m ),
            ("SXA_A_PHOS",     1.8,  "mmol/L", 0.32m, 1.45m),
            ("SXA_A_ALB",     32.0,  "g/L",    35m,   52m  ),
            ("SXA_A_GLU",      5.9,  "mmol/L", 3.89m, 5.83m),
        };

        var hl7Messages  = new List<Hl7Message>();
        var orders       = new List<LabOrder>();
        var headers      = new List<ResultHeader>();
        var resultValues = new List<ResultValue>();
        var today        = DateOnly.FromDateTime(DateTime.UtcNow);
        var rand         = new Random(42);

        foreach (var patient in patients)
        {
            var dates = new[] { today.AddDays(-rand.Next(25, 35)), today.AddDays(-rand.Next(3, 8)) };
            foreach (var date in dates)
            {
                var resultDt = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);

                foreach (var (panelSuffix, analytes, testId, specimen) in new (string, (string, double, string, decimal, decimal)[], string, string)[] {
                    ("CBC",  cbcAnalytes,  "SXA_TEST_CBC",     "Whole Blood"),
                    ("CHEM", chemAnalytes, "SXA_TEST_BUN_PRE", "Serum")
                })
                {
                    var msgId = $"SEED-{date:yyyyMMdd}-{patient.LisPatientId}-{panelSuffix}";
                    var msg = new Hl7Message { Id = Guid.NewGuid(), TenantId = tenant.Id, MessageControlId = msgId, RawPayload = $"[SEEDED] {panelSuffix} for {patient.Name} on {date}", ReceivedAt = resultDt, ProcessedFlag = true };
                    hl7Messages.Add(msg);

                    var order = new LabOrder { Id = Guid.NewGuid(), TenantId = tenant.Id, ClientId = client.Id, PatientId = patient.Id, AccessionNumber = $"ACC-{date:yyyyMMdd}-{patient.LisPatientId}-{panelSuffix}", SourceHl7MessageId = msg.Id, ReleasedAt = resultDt, CreatedAt = resultDt };
                    orders.Add(order);

                    var header = new ResultHeader { Id = Guid.NewGuid(), OrderId = order.Id, TenantId = tenant.Id, SourceHl7MessageId = msg.Id, SxaTestId = testId, SpecimenType = specimen, CollectionDatetime = resultDt.AddHours(-2), ResultDatetime = resultDt };
                    headers.Add(header);

                    foreach (var (code, baseVal, unit, refLow, refHigh) in analytes)
                    {
                        var val  = Math.Round(baseVal * (0.85 + rand.NextDouble() * 0.3), 2);
                        var flag = (decimal)val < refLow ? "L" : (decimal)val > refHigh ? "H" : (string?)null;
                        resultValues.Add(new ResultValue { Id = Guid.NewGuid(), ResultHeaderId = header.Id, TenantId = tenant.Id, AnalyteCode = code, DisplayValue = val.ToString("0.##"), ValueNumeric = (decimal)val, Unit = unit, ReferenceRangeLow = refLow, ReferenceRangeHigh = refHigh, ReferenceRangeRaw = $"{refLow}–{refHigh}", AbnormalFlag = flag, RawHl7Segment = $"OBX|NM|{code}||{val}|{unit}|||F" });
                    }
                }
            }
        }

        db.Hl7Messages.AddRange(hl7Messages); await db.SaveChangesAsync();
        db.Orders.AddRange(orders);           await db.SaveChangesAsync();
        db.ResultHeaders.AddRange(headers);   await db.SaveChangesAsync();
        db.ResultValues.AddRange(resultValues); await db.SaveChangesAsync();

        Console.WriteLine($"Database seeded successfully");
        Console.WriteLine($"  Tenant:        {tenant.Name}");
        Console.WriteLine($"  Clinic:        {client.Name}");
        Console.WriteLine($"  Users:         {users.Count}");
        Console.WriteLine($"  Patients:      {patients.Count}");
        Console.WriteLine($"  HL7 Messages:  {hl7Messages.Count}");
        Console.WriteLine($"  Orders:        {orders.Count}");
        Console.WriteLine($"  ResultHeaders: {headers.Count}");
        Console.WriteLine($"  ResultValues:  {resultValues.Count}");
    }
}