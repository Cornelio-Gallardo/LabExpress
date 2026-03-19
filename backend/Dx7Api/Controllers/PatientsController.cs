using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/patients")]
public class PatientsController : TenantBaseController
{
    private readonly AppDbContext _db;
    public PatientsController(AppDbContext db) => _db = db;

    // ── GET /api/patients/summary ─────────────────────────────────────────────
    // Returns total/ready/stale/noData counts for the stat cards.
    // Fast: three COUNT queries, no data transferred.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] Guid? clientId)
    {
        var tenantId = TenantId;
        var cutoff   = DateTime.UtcNow.AddDays(-30);

        Guid? resolvedClientId = IsPlAdmin ? (clientId ?? ClientId) : ClientId;

        var q = _db.Patients.Where(p => p.TenantId == tenantId && p.IsActive);
        if (resolvedClientId.HasValue)
            q = q.Where(p => p.ClientId == resolvedClientId.Value);

        var total  = await q.CountAsync();
        var ready  = await q.CountAsync(p =>
            _db.Orders
               .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
               .SelectMany(o => _db.ResultHeaders
                   .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                   .Select(h => h.ResultDatetime!.Value))
               .Any(d => d >= cutoff));
        var noData = await q.CountAsync(p =>
            !_db.Orders
               .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
               .Any(o => _db.ResultHeaders
                   .Any(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)));
        var stale = total - ready - noData;

        return Ok(new { total, ready, stale, noData });
    }

    // ── GET /api/patients ─────────────────────────────────────────────────────
    // Paginated, server-side filtered. Returns { data, total, page, pageSize }.
    // All heavy JOINs run in SQL — no data loaded into .NET memory until the
    // final paged slice.
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid?   clientId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? sortBy,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 25)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        var tenantId = TenantId;
        var cutoff   = DateTime.UtcNow.AddDays(-30);

        Guid? resolvedClientId = IsPlAdmin ? (clientId ?? ClientId) : ClientId;

        // ── Base patient filter ───────────────────────────────────────────────
        var q = _db.Patients.Where(p => p.TenantId == tenantId && p.IsActive);

        if (resolvedClientId.HasValue)
            q = q.Where(p => p.ClientId == resolvedClientId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p =>
                p.Name.ToLower().Contains(s) ||
                (p.LisPatientId != null && p.LisPatientId.ToLower().Contains(s)));
        }

        // ── Status filter (runs as SQL EXISTS) ───────────────────────────────
        if (status == "nodata")
            q = q.Where(p =>
                !_db.Orders
                   .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                   .Any(o => _db.ResultHeaders
                       .Any(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)));

        else if (status == "ready")
            q = q.Where(p =>
                _db.Orders
                   .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                   .SelectMany(o => _db.ResultHeaders
                       .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                       .Select(h => h.ResultDatetime!.Value))
                   .Any(d => d >= cutoff));

        else if (status == "stale")
            q = q.Where(p =>
                _db.Orders
                   .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                   .SelectMany(o => _db.ResultHeaders
                       .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                       .Select(h => h.ResultDatetime!.Value))
                   .Any() &&
                !_db.Orders
                   .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                   .SelectMany(o => _db.ResultHeaders
                       .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                       .Select(h => h.ResultDatetime!.Value))
                   .Any(d => d >= cutoff));

        // ── Total count (before pagination) ──────────────────────────────────
        var total = await q.CountAsync();

        // ── Fetch paged slice with correlated subqueries ──────────────────────
        // Each subquery runs once per row in the paged slice (25-100 rows),
        // not for the entire table. PostgreSQL handles this efficiently.
        var lastResultSubq = (IQueryable<Patient> src) => src
            .OrderByDescending(p => _db.Orders
                .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                .SelectMany(o => _db.ResultHeaders
                    .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                    .Select(h => h.ResultDatetime!.Value))
                .Max());

        var paged = await (sortBy == "lastResult" ? lastResultSubq(q) : q.OrderBy(p => p.Name))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                Patient    = p,
                LastResult = (DateTime?)_db.Orders
                    .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                    .SelectMany(o => _db.ResultHeaders
                        .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                        .Select(h => h.ResultDatetime!.Value))
                    .Max(),
                DateCount = _db.Orders
                    .Where(o => o.PatientId == p.Id && o.TenantId == tenantId)
                    .SelectMany(o => _db.ResultHeaders
                        .Where(h => h.OrderId == o.Id && h.TenantId == tenantId && h.ResultDatetime != null)
                        .Select(h => h.ResultDatetime!.Value.Date))
                    .Distinct()
                    .Count()
            })
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var dtos = paged.Select(x =>
        {
            var lastDate     = x.LastResult.HasValue ? DateOnly.FromDateTime(x.LastResult.Value) : (DateOnly?)null;
            var days         = lastDate.HasValue ? today.DayNumber - lastDate.Value.DayNumber : (int?)null;
            var resultStatus = lastDate == null ? "nodata" : days > 30 ? "stale" : "ready";

            return new PatientDto(
                x.Patient.Id, x.Patient.Name, x.Patient.LisPatientId, x.Patient.PhilhealthNo,
                x.Patient.Birthdate, x.Patient.Gender, x.Patient.ContactNumber,
                x.Patient.IsActive, resultStatus, days, lastDate, x.DateCount
            );
        }).ToList();

        return Ok(new { data = dtos, total, page, pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var query = _db.Patients.Where(p => p.Id == id && p.TenantId == TenantId);
        if (!IsPlAdmin && ClientId.HasValue)
            query = query.Where(p => p.ClientId == ClientId.Value);

        var p = await query.FirstOrDefaultAsync();
        if (p == null) return NotFound();

        var tenantId = TenantId;
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);

        var lastHeader = await _db.Orders
            .Where(o => o.PatientId == id && o.TenantId == tenantId)
            .Join(_db.ResultHeaders, o => o.Id, h => h.OrderId, (o, h) => h)
            .Where(h => h.ResultDatetime != null)
            .OrderByDescending(h => h.ResultDatetime)
            .FirstOrDefaultAsync();

        var lastDate     = lastHeader?.ResultDatetime != null
            ? DateOnly.FromDateTime(lastHeader.ResultDatetime.Value) : (DateOnly?)null;
        var days         = lastDate.HasValue ? today.DayNumber - lastDate.Value.DayNumber : (int?)null;
        var resultStatus = lastDate == null ? "nodata" : days > 30 ? "stale" : "ready";

        return Ok(new PatientDto(p.Id, p.Name, p.LisPatientId, p.PhilhealthNo, p.Birthdate, p.Gender,
            p.ContactNumber, p.IsActive, resultStatus, days, lastDate));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (!IsClinicAdmin && !IsPlAdmin) return Forbid();

        var query = _db.Patients.Where(p => p.Id == id && p.TenantId == TenantId);
        if (!IsPlAdmin && ClientId.HasValue)
            query = query.Where(p => p.ClientId == ClientId.Value);

        var patient = await query.FirstOrDefaultAsync();
        if (patient == null) return NotFound();

        patient.IsActive  = false;
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
