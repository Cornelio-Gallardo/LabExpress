using Dx7Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Tenants.AnyAsync()) return;

        // ── Tenant ────────────────────────────────────────────────────────────
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "LABExpress",
            PrimaryColor = "#0D7377",
            IsActive = true
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // ── Client (Clinic) ───────────────────────────────────────────────────
        var client = new Client
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Metro Dialysis Center",
            Code = "MDC",
            Address = "123 Health Ave., Manila",
            IsActive = true
        };
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
        var roles = new List<RoleDefinition>
        {
            new() { TenantId = tenant.Id, RoleKey = "charge_nurse",  Label = "Charge Nurse",  Description = "Manages shift roster, assigns chairs, views all results, exports reports", SortOrder = 1 },
            new() { TenantId = tenant.Id, RoleKey = "shift_nurse",   Label = "Shift Nurse",   Description = "Views patient results and MD notes. Read-only access.", SortOrder = 2 },
            new() { TenantId = tenant.Id, RoleKey = "md",            Label = "Nephrologist",  Description = "Views results and writes/edits session notes (24hr edit window)", SortOrder = 3 },
            new() { TenantId = tenant.Id, RoleKey = "clinic_admin",  Label = "Clinic Admin",  Description = "Full access to clinic: manage users, patients, sessions, export", SortOrder = 4 },
            new() { TenantId = tenant.Id, RoleKey = "pl_admin",      Label = "PL Admin",      Description = "Partner Lab admin — manages all clinics under the tenant", SortOrder = 5 },
        };
        db.RoleDefinitions.AddRange(roles);
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
            Id           = Guid.NewGuid(),
            TenantId     = tenant.Id,
            ClientId     = client.Id,
            Name         = p.Item1,
            LisPatientId = p.Item2,
            PhilhealthNo = p.Item3,
            Gender       = p.Item4,
            Birthdate    = p.Item5,
            ContactNumber = p.Item6,
            IsActive     = true
        }).ToList();

        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        // ── SXA Catalog seed (analytes + tests) ───────────────────────────────
        // Tests and analytes are seeded by add_cdm_v1.sql migration.
        // Here we just look them up to build analyte maps for seeding.
        var analyteMap = await db.SxaAnalytes.ToDictionaryAsync(a => a.AnalyteCode);
        var testMap    = await db.SxaTests.ToDictionaryAsync(t => t.SxaTestId);

        // ── Lab Results seeded as CDM chain: HL7_Message → Order → Header → Values
        // Two result dates per patient. CBC order + CHEM order per date.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rand  = new Random(42);

        // Analyte definitions aligned to SXA catalog codes
        // (code, displayName, baseValue, unit, refRangeLow, refRangeHigh, defaultFlag)
        var cbcAnalytes = new[]
        {
            ("SXA_A_WBC", "White Blood Cells", 5.2,   "x10³/µL", 4.5m,  11.0m,  ""),
            ("SXA_A_HGB", "Hemoglobin",        112.0, "g/L",     130m,  170m,   "L"),
            ("SXA_A_HCT", "Hematocrit",        0.34,  "",        0.38m, 0.50m,  "L"),
            ("SXA_A_PLT", "Platelets",         210.0, "x10³/µL", 150m,  400m,   ""),
        };

        var chemAnalytes = new[]
        {
            ("SXA_A_CREAT",    "Creatinine",     8.4,  "mg/dL",  0.6m,  1.2m,  "H"),
            ("SXA_A_BUN_PRE",  "BUN Pre-Dialysis",  42.0, "mg/dL",  7m,    20m,   "H"),
            ("SXA_A_BUN_POST", "BUN Post-Dialysis", 18.0, "mg/dL",  7m,    20m,   ""),
            ("SXA_A_K",        "Potassium",      5.1,  "mEq/L",  3.5m,  5.0m,  "H"),
            ("SXA_A_NA",       "Sodium",         138.0,"mEq/L",  135m,  145m,  ""),
            ("SXA_A_CA",       "Calcium",        2.1,  "mg/dL",  2.2m,  2.6m,  "L"),
            ("SXA_A_PHOS",     "Phosphorus",     1.8,  "mg/dL",  0.8m,  1.5m,  "H"),
            ("SXA_A_ALB",      "Albumin",        32.0, "g/dL",   35m,   50m,   "L"),
            ("SXA_A_GLU",      "Glucose (FBS)",  5.9,  "mg/dL",  3.9m,  6.1m,  ""),
        };

        var orders       = new List<LabOrder>();
        var headers      = new List<ResultHeader>();
        var resultValues = new List<ResultValue>();

        // Synthetic HL7 message archive — one per order batch per date
        var hl7Messages  = new List<Hl7Message>();

        foreach (var patient in patients)
        {
            var dates = new[]
            {
                today.AddDays(-rand.Next(25, 35)),
                today.AddDays(-rand.Next(3, 8))
            };

            foreach (var date in dates)
            {
                var resultDt = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);

                // ── CBC order ─────────────────────────────────────────────────
                var cbcMsgId = $"SEED-{date:yyyyMMdd}-{patient.LisPatientId}-CBC";
                var cbcMsg = new Hl7Message
                {
                    Id               = Guid.NewGuid(),
                    TenantId         = tenant.Id,
                    MessageControlId = cbcMsgId,
                    RawPayload       = $"[SEEDED] CBC result for {patient.Name} on {date}",
                    ReceivedAt       = resultDt,
                    ProcessedFlag    = true,
                    QuarantineFlag   = false
                };
                hl7Messages.Add(cbcMsg);

                var cbcOrder = new LabOrder
                {
                    Id                  = Guid.NewGuid(),
                    TenantId            = tenant.Id,
                    ClientId            = client.Id,
                    PatientId           = patient.Id,
                    AccessionNumber     = $"ACC-{date:yyyyMMdd}-{patient.LisPatientId}-CBC",
                    SourceHl7MessageId  = cbcMsg.Id,
                    ReleasedAt          = resultDt,
                    CreatedAt           = resultDt
                };
                orders.Add(cbcOrder);

                var cbcHeader = new ResultHeader
                {
                    Id                 = Guid.NewGuid(),
                    OrderId            = cbcOrder.Id,
                    TenantId           = tenant.Id,
                    SourceHl7MessageId = cbcMsg.Id,
                    SxaTestId          = testMap.ContainsKey("SXA_TEST_CBC") ? "SXA_TEST_CBC" : null,
                    SpecimenType       = "Whole Blood",
                    CollectionDatetime = resultDt.AddHours(-2),
                    ResultDatetime     = resultDt
                };
                headers.Add(cbcHeader);

                foreach (var (code, name, baseVal, unit, refLow, refHigh, flag) in cbcAnalytes)
                {
                    var val = Math.Round(baseVal * (0.85 + rand.NextDouble() * 0.3), 2);
                    var dispFlag = val < (double)refLow ? "L" : val > (double)refHigh ? "H" : "";
                    resultValues.Add(new ResultValue
                    {
                        Id              = Guid.NewGuid(),
                        ResultHeaderId  = cbcHeader.Id,
                        TenantId        = tenant.Id,
                        AnalyteCode     = analyteMap.ContainsKey(code) ? code : null,
                        DisplayValue    = val.ToString("0.##"),
                        ValueNumeric    = (decimal)val,
                        Unit            = unit,
                        ReferenceRangeLow  = refLow,
                        ReferenceRangeHigh = refHigh,
                        ReferenceRangeRaw  = $"{refLow}-{refHigh}",
                        AbnormalFlag    = string.IsNullOrEmpty(dispFlag) ? null : dispFlag,
                        RawHl7Segment   = $"OBX|1|NM|{code}||{val}|{unit}|{refLow}-{refHigh}|{dispFlag}|||F"
                    });
                }

                // ── CHEM order ────────────────────────────────────────────────
                var chemMsgId = $"SEED-{date:yyyyMMdd}-{patient.LisPatientId}-CHEM";
                var chemMsg = new Hl7Message
                {
                    Id               = Guid.NewGuid(),
                    TenantId         = tenant.Id,
                    MessageControlId = chemMsgId,
                    RawPayload       = $"[SEEDED] Chemistry result for {patient.Name} on {date}",
                    ReceivedAt       = resultDt,
                    ProcessedFlag    = true,
                    QuarantineFlag   = false
                };
                hl7Messages.Add(chemMsg);

                var chemOrder = new LabOrder
                {
                    Id                  = Guid.NewGuid(),
                    TenantId            = tenant.Id,
                    ClientId            = client.Id,
                    PatientId           = patient.Id,
                    AccessionNumber     = $"ACC-{date:yyyyMMdd}-{patient.LisPatientId}-CHEM",
                    SourceHl7MessageId  = chemMsg.Id,
                    ReleasedAt          = resultDt,
                    CreatedAt           = resultDt
                };
                orders.Add(chemOrder);

                var chemHeader = new ResultHeader
                {
                    Id                 = Guid.NewGuid(),
                    OrderId            = chemOrder.Id,
                    TenantId           = tenant.Id,
                    SourceHl7MessageId = chemMsg.Id,
                    SxaTestId          = testMap.ContainsKey("SXA_TEST_BUN_PRE") ? "SXA_TEST_BUN_PRE" : null,
                    SpecimenType       = "Serum",
                    CollectionDatetime = resultDt.AddHours(-2),
                    ResultDatetime     = resultDt
                };
                headers.Add(chemHeader);

                foreach (var (code, name, baseVal, unit, refLow, refHigh, flag) in chemAnalytes)
                {
                    var val = Math.Round(baseVal * (0.85 + rand.NextDouble() * 0.3), 2);
                    var dispFlag = val < (double)refLow ? "L" : val > (double)refHigh ? "H" : "";
                    resultValues.Add(new ResultValue
                    {
                        Id              = Guid.NewGuid(),
                        ResultHeaderId  = chemHeader.Id,
                        TenantId        = tenant.Id,
                        AnalyteCode     = analyteMap.ContainsKey(code) ? code : null,
                        DisplayValue    = val.ToString("0.##"),
                        ValueNumeric    = (decimal)val,
                        Unit            = unit,
                        ReferenceRangeLow  = refLow,
                        ReferenceRangeHigh = refHigh,
                        ReferenceRangeRaw  = $"{refLow}-{refHigh}",
                        AbnormalFlag    = string.IsNullOrEmpty(dispFlag) ? null : dispFlag,
                        RawHl7Segment   = $"OBX|1|NM|{code}||{val}|{unit}|{refLow}-{refHigh}|{dispFlag}|||F"
                    });
                }
            }
        }

        db.Hl7Messages.AddRange(hl7Messages);
        await db.SaveChangesAsync();

        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();

        db.ResultHeaders.AddRange(headers);
        await db.SaveChangesAsync();

        db.ResultValues.AddRange(resultValues);
        await db.SaveChangesAsync();

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