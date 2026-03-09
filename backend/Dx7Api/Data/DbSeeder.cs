using Dx7Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Tenants.AnyAsync()) return;

        // Tenant
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "LABExpress",
            PrimaryColor = "#0D7377",
            IsActive = true
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // Client (Clinic)
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

        // Users
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

        // Roles
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

        // Patients
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
            Id         = Guid.NewGuid(),
            TenantId   = tenant.Id,
            ClientId   = client.Id,
            Name       = p.Item1,
            LisPatientId = p.Item2,
            PhilhealthNo = p.Item3,
            Gender     = p.Item4,
            Birthdate  = p.Item5,
            ContactNumber = p.Item6,
            IsActive   = true
        }).ToList();

        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        // Lab Results - CBC + Chemistry panel for each patient (2 dates each)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<Result>();
        var rand = new Random(42);

        var cbcTests = new[]
        {
            ("CBC-WBC",  "WBC",             "5.2",  "x10/uL",  "4.5-11.0",  "N"),
            ("CBC-RBC",  "RBC",             "4.1",  "x10/uL",  "4.2-5.4",   "L"),
            ("CBC-HGB",  "Hemoglobin",      "112",  "g/L",     "130-170",   "L"),
            ("CBC-HCT",  "Hematocrit",      "0.34", "",        "0.38-0.50", "L"),
            ("CBC-PLT",  "Platelets",       "210",  "x10/uL",  "150-400",   "N"),
            ("CBC-NEUT", "Neutrophils",     "62",   "%",       "50-70",     "N"),
            ("CBC-LYMP", "Lymphocytes",     "28",   "%",       "20-40",     "N"),
        };

        var chemTests = new[]
        {
            ("CHEM-CRE", "Creatinine",      "8.4",  "mg/dL",  "0.6-1.2",  "H"),
            ("CHEM-BUN", "BUN",             "42",   "mg/dL",  "7-20",     "H"),
            ("CHEM-K",   "Potassium",       "5.1",  "mEq/L",  "3.5-5.0",  "H"),
            ("CHEM-NA",  "Sodium",          "138",  "mEq/L",  "135-145",  "N"),
            ("CHEM-CA",  "Calcium",         "2.1",  "mmol/L", "2.2-2.6",  "L"),
            ("CHEM-PHOS","Phosphorus",      "1.8",  "mmol/L", "0.8-1.5",  "H"),
            ("CHEM-ALB", "Albumin",         "32",   "g/L",    "35-50",    "L"),
            ("CHEM-URB", "Uric Acid",       "7.8",  "mg/dL",  "3.4-7.0",  "H"),
            ("CHEM-GLU", "Glucose (FBS)",   "5.9",  "mmol/L", "3.9-6.1",  "N"),
            ("CHEM-CO2", "Bicarbonate",     "18",   "mEq/L",  "22-29",    "L"),
        };

        foreach (var patient in patients)
        {
            var dates = new[] { today.AddDays(-rand.Next(25, 35)), today.AddDays(-rand.Next(3, 8)) };
            foreach (var date in dates)
            {
                foreach (var t in cbcTests)
                {
                    var val = double.Parse(t.Item3) * (0.85 + rand.NextDouble() * 0.3);
                    results.Add(new Result {
                        Id = Guid.NewGuid(), TenantId = tenant.Id, PatientId = patient.Id,
                        TestCode = t.Item1, TestName = t.Item2,
                        ResultValue = Math.Round(val, 2).ToString(),
                        ResultUnit = t.Item4, ReferenceRange = t.Item5, AbnormalFlag = t.Item6,
                        ResultDate = date, SourceLab = "LABExpress Central",
                        AccessionId = $"ACC-{date:yyyyMMdd}-{patient.LisPatientId}-CBC"
                    });
                }
                foreach (var t in chemTests)
                {
                    var val = double.Parse(t.Item3) * (0.85 + rand.NextDouble() * 0.3);
                    results.Add(new Result {
                        Id = Guid.NewGuid(), TenantId = tenant.Id, PatientId = patient.Id,
                        TestCode = t.Item1, TestName = t.Item2,
                        ResultValue = Math.Round(val, 2).ToString(),
                        ResultUnit = t.Item4, ReferenceRange = t.Item5, AbnormalFlag = t.Item6,
                        ResultDate = date, SourceLab = "LABExpress Central",
                        AccessionId = $"ACC-{date:yyyyMMdd}-{patient.LisPatientId}-CHEM"
                    });
                }
            }
        }

        db.Results.AddRange(results);
        await db.SaveChangesAsync();

        Console.WriteLine($"Database seeded successfully");
        Console.WriteLine($"  Tenant:   {tenant.Name}");
        Console.WriteLine($"  Clinic:   {client.Name}");
        Console.WriteLine($"  Users:    {users.Count}");
        Console.WriteLine($"  Patients: {patients.Count}");
        Console.WriteLine($"  Results:  {results.Count}");
    }
}