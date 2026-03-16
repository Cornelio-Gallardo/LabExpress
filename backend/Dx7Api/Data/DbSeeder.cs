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

        // SXA catalog (SxaTestCatalog + SxaAnalytes) is inserted by the Program.cs
        // startup patch via ON CONFLICT DO NOTHING — no need to insert here.

        // ── §6.1 TenantTestMaps — OBR-4 codes from HCLab ────────────────────
        db.TenantTestMaps.AddRange(
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "BUNPRE",   SxaTestId = "SXA_TEST_BUN_PRE",  IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "BUNPOST",  SxaTestId = "SXA_TEST_BUN_POST", IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "BUNPOS",   SxaTestId = "SXA_TEST_BUN_POST", IsActive = true }, // HCLAB alias
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "DGC0074",  SxaTestId = "SXA_TEST_CBC",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantTestCode = "CBC",      SxaTestId = "SXA_TEST_CBC",      IsActive = true }, // HCLAB format
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
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "BUNPOST", AnalyteCode = "SXA_A_BUN_POST", IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "BUNPOS",  AnalyteCode = "SXA_A_BUN_POST", IsActive = true }, // HCLAB alias
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
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "HBA1C",   AnalyteCode = "SXA_A_HBA1C",    IsActive = true },
            // Lipid panel
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "CHOL",    AnalyteCode = "SXA_A_CHOL",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "TRIG",    AnalyteCode = "SXA_A_TRIG",     IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "HDL",     AnalyteCode = "SXA_A_HDL",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "LDL",     AnalyteCode = "SXA_A_LDL",      IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenant.Id, TenantAnalyteCode = "VLDL",    AnalyteCode = "SXA_A_VLDL",     IsActive = true }
        );
        await db.SaveChangesAsync();

        Console.WriteLine($"Database seeded: tenant={tenant.Name}, clinic={client.Name}, users={users.Count}");
    }
}