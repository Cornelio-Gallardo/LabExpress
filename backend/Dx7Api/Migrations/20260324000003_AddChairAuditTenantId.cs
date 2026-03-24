using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dx7Api.Migrations
{
    /// <summary>
    /// Adds the missing TenantId column to ChairAudits and applies the same
    /// RLS policies used for every other tenant-scoped table (EnableRls migration).
    ///
    /// Root cause: InitialCreate created ChairAudits without TenantId; the column
    /// was added to the C# model later but the corresponding migration was never
    /// generated.  This migration closes that gap.
    ///
    /// The DEFAULT clause backfills existing rows with the single active tenant so
    /// the NOT NULL constraint can be satisfied without manual intervention.
    /// In a fresh DB there are no rows, so the DEFAULT is a no-op.
    /// </summary>
    public partial class AddChairAuditTenantId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL does not allow subqueries in DEFAULT expressions.
            // Standard pattern: add nullable → backfill → enforce NOT NULL.

            // Step 1: add the column as nullable.
            migrationBuilder.Sql("""
                ALTER TABLE "ChairAudits"
                    ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
                """);

            // Step 2: backfill any existing rows with the first active tenant.
            // In a fresh DB this is a no-op (no rows exist yet).
            migrationBuilder.Sql("""
                UPDATE "ChairAudits"
                SET "TenantId" = (
                    SELECT "Id" FROM "Tenants"
                    WHERE "IsActive" = true
                    ORDER BY "CreatedAt"
                    LIMIT 1
                )
                WHERE "TenantId" IS NULL;
                """);

            // Step 3: enforce NOT NULL now that all rows have a value.
            migrationBuilder.Sql("""
                ALTER TABLE "ChairAudits" ALTER COLUMN "TenantId" SET NOT NULL;
                """);

            // Foreign key to Tenants (same pattern as every other tenant table).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints
                        WHERE constraint_name = 'FK_ChairAudits_Tenants_TenantId'
                          AND table_name = 'ChairAudits'
                    ) THEN
                        ALTER TABLE "ChairAudits"
                            ADD CONSTRAINT "FK_ChairAudits_Tenants_TenantId"
                            FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id")
                            ON DELETE RESTRICT;
                    END IF;
                END $$;
                """);

            // Index for the FK.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ChairAudits_TenantId"
                    ON "ChairAudits" ("TenantId");
                """);

            // Enable RLS + apply the same two policies used by EnableRls for all other tables.
            migrationBuilder.Sql("""
                ALTER TABLE "ChairAudits" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "ChairAudits" FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_read ON "ChairAudits";
                CREATE POLICY tenant_read ON "ChairAudits"
                    AS RESTRICTIVE
                    FOR SELECT
                    USING (
                        "TenantId" = nullif(
                            current_setting('app.current_tenant_id', true), ''
                        )::uuid
                    );
                """);

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS allow_writes ON "ChairAudits";
                CREATE POLICY allow_writes ON "ChairAudits"
                    AS PERMISSIVE
                    FOR ALL
                    USING (true)
                    WITH CHECK (true);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_read  ON "ChairAudits";
                DROP POLICY IF EXISTS allow_writes ON "ChairAudits";
                ALTER TABLE "ChairAudits" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE "ChairAudits" NO FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "ChairAudits"
                    DROP CONSTRAINT IF EXISTS "FK_ChairAudits_Tenants_TenantId";
                DROP INDEX IF EXISTS "IX_ChairAudits_TenantId";
                ALTER TABLE "ChairAudits" DROP COLUMN IF EXISTS "TenantId";
                """);
        }
    }
}
