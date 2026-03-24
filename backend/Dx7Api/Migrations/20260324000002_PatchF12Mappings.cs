using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dx7Api.Migrations
{
    /// <summary>
    /// F-12: Add missing HCLab OBR-4 / OBX-3 mappings needed to process HL7WNFS_00000005.
    ///
    ///   OBR-4 "CREA" (Creatinine/chemistry panel) was absent from TenantTestMaps,
    ///   causing the entire message to be quarantined (CDM §6.3).
    ///
    ///   OBX-3 "BUN" (standalone BUN, not BUNPRE/BUNPOST) was absent from
    ///   TenantAnalyteMaps, causing that OBX to be quarantined individually (CDM §6.3).
    ///
    /// Both rows use ON CONFLICT DO NOTHING — safe to apply against a DB that was
    /// freshly seeded from the updated DbSeeder (which now includes both entries).
    /// </summary>
    public partial class PatchF12Mappings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure the SXA catalog entries referenced by the maps below exist.
            // This migration runs during MigrateAsync, before the post-migration catalog
            // seed in Program.cs, so the FK targets must be inserted here first.
            // ON CONFLICT DO NOTHING makes this idempotent if the seed already ran.
            migrationBuilder.Sql("""
                INSERT INTO "SxaTestCatalog" ("SxaTestId", "CanonicalName", "Category", "ResultType", "ActiveFlag")
                VALUES ('SXA_TEST_CHEM', 'General Chemistry Panel', 'CHEMISTRY', 'PANEL', true)
                ON CONFLICT ("SxaTestId") DO NOTHING;

                INSERT INTO "SxaAnalytes" ("AnalyteCode", "DisplayName", "DefaultUnit", "ResultType")
                VALUES ('SXA_A_BUN_POST', 'BUN Post-Dialysis', 'mmol/L', 'NUMERIC')
                ON CONFLICT ("AnalyteCode") DO NOTHING;
                """);

            // Patch TenantTestMap: CREA → SXA_TEST_CHEM for every active tenant.
            // Uses gen_random_uuid() so each tenant gets a unique PK.
            migrationBuilder.Sql("""
                INSERT INTO "TenantTestMaps" ("Id", "TenantId", "TenantTestCode", "SxaTestId", "IsActive")
                SELECT gen_random_uuid(), t."Id", 'CREA', 'SXA_TEST_CHEM', true
                FROM "Tenants" t
                WHERE t."IsActive" = true
                  AND NOT EXISTS (
                      SELECT 1 FROM "TenantTestMaps" m
                      WHERE m."TenantId" = t."Id"
                        AND m."TenantTestCode" = 'CREA'
                  );
                """);

            // Patch TenantAnalyteMap: BUN → SXA_A_BUN_POST for every active tenant.
            migrationBuilder.Sql("""
                INSERT INTO "TenantAnalyteMaps" ("Id", "TenantId", "TenantAnalyteCode", "AnalyteCode", "IsActive")
                SELECT gen_random_uuid(), t."Id", 'BUN', 'SXA_A_BUN_POST', true
                FROM "Tenants" t
                WHERE t."IsActive" = true
                  AND NOT EXISTS (
                      SELECT 1 FROM "TenantAnalyteMaps" m
                      WHERE m."TenantId" = t."Id"
                        AND m."TenantAnalyteCode" = 'BUN'
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "TenantTestMaps"   WHERE "TenantTestCode"   = 'CREA';
                DELETE FROM "TenantAnalyteMaps" WHERE "TenantAnalyteCode" = 'BUN';
                """);
        }
    }
}
