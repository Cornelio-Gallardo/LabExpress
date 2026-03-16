using Dx7Api.Data;
using Dx7Api.Models;
using System.Text.Json;

namespace Dx7Api.Services;

/// <summary>
/// Appendix B §4 — Audit Path Guarantee.
/// All write operations must call Log() before SaveChangesAsync().
/// Audit records are append-only — never updated or deleted.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public void Log(
        Guid tenantId,
        Guid? userId,
        string action,
        string entity,
        Guid? entityId = null,
        object? before = null,
        object? after  = null,
        string? notes  = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId   = userId,
            Action   = action,
            Entity   = entity,
            EntityId = entityId,
            Before   = before == null ? null : JsonSerializer.Serialize(before),
            After    = after  == null ? null : JsonSerializer.Serialize(after),
            Notes    = notes,
            Timestamp = DateTime.UtcNow
        });
    }
}
