using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : TenantBaseController
{
    private readonly AppDbContext _db;
    public SessionsController(AppDbContext db) => _db = db;

    // Resolve ClientId from JWT, then request body, then DB — in that order
    private async Task<Guid?> ResolveClientId(Guid? reqClientId = null)
    {
        if (ClientId.HasValue) return ClientId;
        if (reqClientId.HasValue) return reqClientId;
        var dbUser = await _db.Users.FindAsync(CurrentUserId);
        return dbUser?.ClientId;
    }

    // All clinical roles can view sessions
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? clientId,
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] int? shift)
    {
        var resolvedClientId = clientId ?? ClientId;

        IQueryable<Session> query;

        if (dateFrom.HasValue || dateTo.HasValue)
        {
            // Range query — no default, return all in range
            query = _db.Sessions
                .Include(s => s.Patient)
                .Include(s => s.AssignedByUser)
                .Where(s => s.TenantId == TenantId);
            if (dateFrom.HasValue) query = query.Where(s => s.SessionDate >= dateFrom.Value);
            if (dateTo.HasValue)   query = query.Where(s => s.SessionDate <= dateTo.Value);
        }
        else
        {
            // Single date (default today)
            var resolvedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            query = _db.Sessions
                .Include(s => s.Patient)
                .Include(s => s.AssignedByUser)
                .Where(s => s.TenantId == TenantId && s.SessionDate == resolvedDate);
        }

        if (resolvedClientId.HasValue)
            query = query.Where(s => s.ClientId == resolvedClientId.Value);

        if (shift.HasValue)
            query = query.Where(s => s.ShiftNumber == shift.Value);

        var sessions = await query.OrderBy(s => s.Chair).ThenBy(s => s.Patient.Name).ToListAsync();

        return Ok(sessions.Select(s => new SessionDto(
            s.Id, s.PatientId, s.Patient.Name,
            s.SessionDate, s.ShiftNumber, s.ShiftLabel, s.Chair,
            s.AssignedByUser.Name, s.AssignedAt
        )));
    }


    // GET /api/sessions/last-date — returns the most recent session date for this tenant/client
    [HttpGet("last-date")]
    public async Task<IActionResult> GetLastDate([FromQuery] Guid? clientId)
    {
        var resolvedClientId = clientId ?? ClientId;
        var query = _db.Sessions.Where(s => s.TenantId == TenantId);
        if (resolvedClientId.HasValue) query = query.Where(s => s.ClientId == resolvedClientId.Value);
        var last = await query.OrderByDescending(s => s.SessionDate).Select(s => s.SessionDate).FirstOrDefaultAsync();
        return Ok(new { date = last == default ? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd") : last.ToString("yyyy-MM-dd") });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var session = await _db.Sessions
            .Include(s => s.Patient)
            .Include(s => s.AssignedByUser)
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == TenantId);
        if (session == null) return NotFound();
        return Ok(new SessionDto(
            session.Id, session.PatientId, session.Patient.Name,
            session.SessionDate, session.ShiftNumber, session.ShiftLabel, session.Chair,
            session.AssignedByUser.Name, session.AssignedAt
        ));
    }

    // Charge nurse and above can create sessions
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequest req)
    {
        if (!IsChargeNurse && !IsShiftNurse && !IsClinicAdmin && !IsPlAdmin)
            return Forbid();

        var resolvedClientId = await ResolveClientId(req.ClientId);
        if (!resolvedClientId.HasValue)
            return BadRequest(new { message = "Client context required" });

        // Resolve label and numeric index
        var label = !string.IsNullOrWhiteSpace(req.ShiftLabel) ? req.ShiftLabel.Trim() : "Shift 1";
        int shiftNum = req.ShiftNumber > 0 ? req.ShiftNumber
            : int.TryParse(System.Text.RegularExpressions.Regex.Match(label, @"\d+").Value, out var n) ? n : 0;

        // Prevent duplicate patient on same date (any shift)
        var alreadyExists = await _db.Sessions.AnyAsync(s =>
            s.ClientId == resolvedClientId.Value &&
            s.PatientId == req.PatientId &&
            s.SessionDate == req.SessionDate);

        if (alreadyExists)
            return Ok(new { warning = "Patient already assigned on this date", created = false, duplicate = true });

        // Flag duplicate chair but still create
        bool chairDuplicate = false;
        if (!string.IsNullOrEmpty(req.Chair))
        {
            chairDuplicate = await _db.Sessions.AnyAsync(s =>
                s.ClientId == resolvedClientId.Value &&
                s.SessionDate == req.SessionDate &&
                s.ShiftLabel == label &&
                s.Chair == req.Chair);
        }

        var session = new Session
        {
            TenantId    = TenantId,
            ClientId    = resolvedClientId.Value,
            PatientId   = req.PatientId,
            SessionDate = req.SessionDate,
            ShiftNumber = shiftNum,
            ShiftLabel  = label,
            Chair       = req.Chair,
            AssignedBy  = CurrentUserId,
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        await _db.Entry(session).Reference(s => s.Patient).LoadAsync();
        await _db.Entry(session).Reference(s => s.AssignedByUser).LoadAsync();

        var dto = new SessionDto(
            session.Id, session.PatientId, session.Patient.Name,
            session.SessionDate, session.ShiftNumber, session.ShiftLabel, session.Chair,
            session.AssignedByUser.Name, session.AssignedAt
        );

        if (chairDuplicate)
            return Ok(new { warning = $"Chair {req.Chair} already assigned in {label}", created = true, session = dto });

        return Ok(new { created = true, session = dto });
    }

    // Bulk create sessions
    [HttpPost("bulk")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkCreateSessionRequest req)
    {
        if (!IsChargeNurse && !IsShiftNurse && !IsClinicAdmin && !IsPlAdmin)
            return Forbid();

        var resolvedClientId = await ResolveClientId(req.ClientId);
        if (!resolvedClientId.HasValue)
            return BadRequest(new { message = "Client context required" });

        // Get already-assigned patient IDs for this shift
        var existing = await _db.Sessions
            .Where(s => s.ClientId == resolvedClientId.Value
                     && s.SessionDate == req.SessionDate
                     && s.ShiftNumber == req.ShiftNumber)
            .Select(s => s.PatientId)
            .ToListAsync();

        var toCreate = req.PatientIds.Except(existing).ToList();

        var sessions = toCreate.Select(pid => new Session
        {
            TenantId    = TenantId,
            ClientId    = resolvedClientId.Value,
            PatientId   = pid,
            SessionDate = req.SessionDate,
            ShiftNumber = req.ShiftNumber,
            AssignedBy  = CurrentUserId,
        }).ToList();

        _db.Sessions.AddRange(sessions);
        await _db.SaveChangesAsync();
        return Ok(new { created = sessions.Count, skipped = existing.Count });
    }

    // Charge nurse can assign/update chairs
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateChair(Guid id, [FromBody] UpdateChairRequest req)
    {
        if (!IsChargeNurse && !IsShiftNurse && !IsClinicAdmin && !IsPlAdmin)
            return Forbid();

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == TenantId);
        if (session == null) return NotFound();

        var audit = new ChairAudit
        {
            SessionId = id,
            ChairOld  = session.Chair,
            ChairNew  = req.Chair,
            ChangedBy = CurrentUserId,
        };

        session.Chair     = req.Chair;
        session.UpdatedAt = DateTime.UtcNow;

        _db.ChairAudits.Add(audit);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Remove patient from shift
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!IsChargeNurse && !IsShiftNurse && !IsClinicAdmin && !IsPlAdmin)
            return Forbid();

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == TenantId);
        if (session == null) return NotFound();
        _db.Sessions.Remove(session);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}