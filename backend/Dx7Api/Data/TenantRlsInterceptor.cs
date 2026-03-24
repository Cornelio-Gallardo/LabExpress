using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Dx7Api.Data;

/// <summary>
/// CDM Trust Boundary — PostgreSQL RLS backstop (F-10).
///
/// Executes  SET app.current_tenant_id = '&lt;uuid&gt;'  as a dedicated command immediately
/// before every EF Core command so that the database-level Row Level Security policies
/// act as a fail-closed isolation layer independent of the EF query filters.
///
/// Design notes:
///   • Uses IHttpContextAccessor (singleton-safe) to avoid circular dependency with AppDbContext.
///   • Web requests: TenantMiddleware stores the resolved GUID in HttpContext.Items["CurrentTenantId"].
///   • Background services: caller wraps work in  using (TenantAmbient.Use(tenantId)) { ... }
///     so the GUID is carried in an AsyncLocal and picked up here as a fallback.
///   • SET (session-level) is used rather than SET LOCAL (transaction-level) so it works
///     without explicit transaction blocks.  Because the interceptor runs on EVERY command,
///     pool-borrowed connections with a stale value are always corrected before the query.
///   • IMPORTANT — the SET is executed as a SEPARATE DbCommand (not prepended to CommandText).
///     Prepending would add an extra result set to batched commands and corrupt EF Core's
///     row-count tracking, causing spurious DbUpdateConcurrencyException on batched INSERTs.
///   • When no tenant is resolved (login endpoint, migration startup), the setting is '' (empty).
///     The RLS USING clause uses nullif(...,'')::uuid which evaluates to NULL, making
///     "TenantId" = NULL → false → zero rows (fail-closed).
/// </summary>
public class TenantRlsInterceptor(IHttpContextAccessor httpContextAccessor) : DbCommandInterceptor
{
    private readonly IHttpContextAccessor _http = httpContextAccessor;

    // ── Async paths ───────────────────────────────────────────────────────────

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantSetAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantSetAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantSetAsync(command, cancellationToken);
        return result;
    }

    // ── Sync paths (migration, seeder) ───────────────────────────────────────

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ApplyTenantSet(command);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTenantSet(command);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result)
    {
        ApplyTenantSet(command);
        return result;
    }

    // ── Core logic ────────────────────────────────────────────────────────────

    private string GetTenantId()
    {
        // 1. Web requests: TenantMiddleware writes the resolved GUID into HttpContext.Items.
        Guid? tenantId = null;
        if (_http.HttpContext?.Items.TryGetValue("CurrentTenantId", out var raw) == true)
            tenantId = raw as Guid?;

        // 2. Background services: caller uses  using (TenantAmbient.Use(tenantId)) { ... }
        if (!tenantId.HasValue)
            tenantId = TenantAmbient.Current;

        return tenantId.HasValue ? tenantId.Value.ToString() : "";
    }

    /// <summary>
    /// Runs SET app.current_tenant_id as an independent ADO.NET command BEFORE the EF
    /// command executes.  Because we use a separate DbCommand (not CommandText prepend),
    /// the SET result is fully consumed here and never appears in EF's result-set reader.
    /// </summary>
    private void ApplyTenantSet(DbCommand command)
    {
        if (command.Connection is null) return;
        var tid = GetTenantId();
        using var setCmd = command.Connection.CreateCommand();
        setCmd.Transaction = command.Transaction; // run in the same transaction if active
        setCmd.CommandText  = $"SET app.current_tenant_id = '{tid}'";
        setCmd.ExecuteNonQuery();
    }

    private async Task ApplyTenantSetAsync(DbCommand command, CancellationToken ct)
    {
        if (command.Connection is null) return;
        var tid = GetTenantId();
        await using var setCmd = command.Connection.CreateCommand();
        setCmd.Transaction  = command.Transaction;
        setCmd.CommandText  = $"SET app.current_tenant_id = '{tid}'";
        await setCmd.ExecuteNonQueryAsync(ct);
    }
}
