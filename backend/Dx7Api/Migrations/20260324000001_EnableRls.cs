using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dx7Api.Migrations
{
    /// <summary>
    /// F-10: PostgreSQL Row Level Security — database-level tenant isolation backstop.
    ///
    /// Pattern:
    ///   1. RESTRICTIVE FOR SELECT policy — enforces tenant isolation on every read.
    ///      nullif(current_setting('app.current_tenant_id', true), '')::uuid
    ///        • current_setting(..., true) → returns NULL if param not set (missing_ok)
    ///        • nullif(..., '') → converts empty string → NULL
    ///        • NULL::uuid = NULL; "TenantId" = NULL → false → zero rows (fail-closed)
    ///
    ///   2. Permissive USING(true) / WITH CHECK(true) policy covers INSERT/UPDATE/DELETE.
    ///      Writes are unrestricted at the DB level; TenantId is enforced by the EF model.
    ///      This prevents the default-deny behaviour that would block the seeder and migrations.
    ///
    ///   3. FORCE ROW LEVEL SECURITY — applies RLS even to the table owner so the
    ///      backstop holds regardless of the database role running the application.
    ///
    ///   4. TenantRlsInterceptor prepends  SET app.current_tenant_id = '&lt;uuid&gt;'
    ///      to every EF Core command, so pool-borrowed connections are always updated
    ///      before a query executes.
    ///
    /// Tables excluded from RLS (no "TenantId" column):
    ///   Tenants, SxaTestCatalog, SxaAnalytes, RefData, __EFMigrationsHistory
    /// </summary>
    public partial class EnableRls : Migration
    {
        // All tables that carry a "TenantId" column and need row-level isolation.
        // NOTE: "ChairAudits" is intentionally absent — it was created in InitialCreate
        // without a TenantId column.  RLS is added for it in the AddChairAuditTenantId
        // migration once the column exists.
        private static readonly string[] TenantTables =
        [
            "Clients", "Users", "Patients", "Sessions", "MdNotes",
            "RoleDefinitions",
            "Hl7Messages", "LabOrders", "ResultHeaders", "ResultValues",
            "TenantTestMaps", "TenantAnalyteMaps",
            "ShiftSchedules", "ShiftNurseAssignments",
            "Results", "AuditLogs", "LabNotes"
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                // ── Enable & force RLS ────────────────────────────────────────
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    """);

                // ── RESTRICTIVE SELECT isolation ──────────────────────────────
                // Combines with any permissive policies using AND, so it cannot be
                // overridden by the permissive allow_writes policy below.
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS tenant_read ON "{table}";
                    CREATE POLICY tenant_read ON "{table}"
                        AS RESTRICTIVE
                        FOR SELECT
                        USING (
                            "TenantId" = nullif(
                                current_setting('app.current_tenant_id', true), ''
                            )::uuid
                        );
                    """);

                // ── Permissive write passthrough ──────────────────────────────
                // Prevents default-deny on INSERT/UPDATE/DELETE.
                // Application-layer (EF TenantId + query filters) enforces write isolation.
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS allow_writes ON "{table}";
                    CREATE POLICY allow_writes ON "{table}"
                        AS PERMISSIVE
                        FOR ALL
                        USING (true)
                        WITH CHECK (true);
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS tenant_read  ON "{table}";
                    DROP POLICY IF EXISTS allow_writes ON "{table}";
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;
                    """);
            }
        }
    }
}
