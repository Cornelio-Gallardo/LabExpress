using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/shifts")]
public class ShiftsController : TenantBaseController
{
    private readonly AppDbContext _db;
    public ShiftsController(AppDbContext db) => _db = db;

    // GET /api/shifts?date=2026-03-06
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateOnly? date, [FromQuery] Guid? clientId)
    {
        // Non-PL admins are always locked to their own clinic
        var resolvedClientId = IsPlAdmin ? (clientId ?? ClientId) : ClientId;
        var resolvedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var schedules = await _db.ShiftSchedules
            .Include(s => s.NurseAssignments)
                .ThenInclude(n => n.NurseUser)
            .Where(s => s.TenantId == TenantId && s.ScheduleDate == resolvedDate)
            .Where(s => !resolvedClientId.HasValue || s.ClientId == resolvedClientId.Value)
            .OrderBy(s => s.ShiftNumber)
            .ToListAsync();

        // Get patient counts per shift
        var patientCounts = await _db.Sessions
            .Where(s => s.TenantId == TenantId && s.SessionDate == resolvedDate)
            .Where(s => !resolvedClientId.HasValue || s.ClientId == resolvedClientId.Value)
            .GroupBy(s => s.ShiftNumber)
            .Select(g => new { ShiftNumber = g.Key, Count = g.Count(), Filled = g.Count(x => x.Chair != null) })
            .ToListAsync();

        var countDict = patientCounts.ToDictionary(x => x.ShiftNumber);

        return Ok(schedules.Select(s =>
        {
            countDict.TryGetValue(s.ShiftNumber, out var counts);
            return new ShiftScheduleDto(
                s.Id, s.ClientId, s.ScheduleDate,
                s.ShiftNumber, s.ShiftLabel, s.StartTime, s.EndTime,
                s.MaxChairs, s.IsActive, s.Notes,
                counts?.Count ?? 0, counts?.Filled ?? 0,
                s.NurseAssignments.Select(n => new ShiftNurseDto(
                    n.Id, n.NurseUserId, n.NurseUser.Name,
                    n.NurseUser.Email, n.AssignmentRole, n.AssignedAt
                )).ToList()
            );
        }));
    }

    // GET /api/shifts/week?from=2026-03-01
    [HttpGet("week")]
    public async Task<IActionResult> GetWeek([FromQuery] DateOnly? from, [FromQuery] Guid? clientId)
    {
        var resolvedClientId = clientId ?? ClientId;
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var end = start.AddDays(6);

        var schedules = await _db.ShiftSchedules
            .Include(s => s.NurseAssignments).ThenInclude(n => n.NurseUser)
            .Where(s => s.TenantId == TenantId
                     && s.ScheduleDate >= start && s.ScheduleDate <= end)
            .Where(s => !resolvedClientId.HasValue || s.ClientId == resolvedClientId.Value)
            .OrderBy(s => s.ScheduleDate).ThenBy(s => s.ShiftNumber)
            .ToListAsync();

        var patientCounts = await _db.Sessions
            .Where(s => s.TenantId == TenantId
                     && s.SessionDate >= start && s.SessionDate <= end)
            .Where(s => !resolvedClientId.HasValue || s.ClientId == resolvedClientId.Value)
            .GroupBy(s => new { s.SessionDate, s.ShiftNumber })
            .Select(g => new { g.Key.SessionDate, g.Key.ShiftNumber, Count = g.Count(), Filled = g.Count(x => x.Chair != null) })
            .ToListAsync();

        var countDict = patientCounts.ToDictionary(x => $"{x.SessionDate}_{x.ShiftNumber}");

        return Ok(schedules.Select(s =>
        {
            countDict.TryGetValue($"{s.ScheduleDate}_{s.ShiftNumber}", out var counts);
            return new ShiftScheduleDto(
                s.Id, s.ClientId, s.ScheduleDate,
                s.ShiftNumber, s.ShiftLabel, s.StartTime, s.EndTime,
                s.MaxChairs, s.IsActive, s.Notes,
                counts?.Count ?? 0, counts?.Filled ?? 0,
                s.NurseAssignments.Select(n => new ShiftNurseDto(
                    n.Id, n.NurseUserId, n.NurseUser.Name,
                    n.NurseUser.Email, n.AssignmentRole, n.AssignedAt
                )).ToList()
            );
        }));
    }

    // POST /api/shifts
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftScheduleRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin && !IsChargeNurse) return Forbid();
        var resolvedClient = ClientId ?? req.ClientId;
        // Fallback: look up ClientId from DB in case JWT is stale
        if (!resolvedClient.HasValue)
        {
            var dbUser = await _db.Users.FindAsync(CurrentUserId);
            resolvedClient = dbUser?.ClientId;
        }
        if (!resolvedClient.HasValue) return BadRequest(new {
            message = "Client context required",
            debugJwtClientId = ClientId?.ToString() ?? "null",
            debugReqClientId = req.ClientId?.ToString() ?? "null",
            debugUserId = CurrentUserId.ToString()
        });

        // Check for duplicate
        var exists = await _db.ShiftSchedules.AnyAsync(s =>
            s.ClientId == resolvedClient.Value &&
            s.ScheduleDate == req.ScheduleDate &&
            s.ShiftNumber == req.ShiftNumber);

        if (exists)
            return BadRequest(new { message = $"Shift {req.ShiftNumber} already exists for {req.ScheduleDate}" });

        var schedule = new ShiftSchedule
        {
            TenantId = TenantId,
            ClientId = resolvedClient.Value,
            ScheduleDate = req.ScheduleDate,
            ShiftNumber = req.ShiftNumber,
            ShiftLabel = req.ShiftLabel,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            MaxChairs = req.MaxChairs,
            Notes = req.Notes,
            IsActive = true
        };

        _db.ShiftSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = schedule.Id }, schedule.Id);
    }

    // POST /api/shifts/bulk - create all 4 shifts for a date range
    [HttpPost("bulk")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkShiftRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin && !IsChargeNurse) return Forbid();
        var resolvedClientBulk = ClientId ?? req.ClientId;
        // Fallback: look up ClientId from DB in case JWT is stale
        if (!resolvedClientBulk.HasValue)
        {
            var dbUser = await _db.Users.FindAsync(CurrentUserId);
            resolvedClientBulk = dbUser?.ClientId;
        }
        if (!resolvedClientBulk.HasValue) return BadRequest(new { message = "Client context required" });

        // Use custom shifts from request, or fall back to defaults
        var shiftsToCreate = req.Shifts?.Count > 0
            ? req.Shifts.Select(s => new { Num = s.ShiftNumber, Label = s.ShiftLabel, Start = s.StartTime, End = s.EndTime }).ToArray()
            : new[]
            {
                new { Num = 1, Label = "Morning",     Start = "06:00", End = "10:00" },
                new { Num = 2, Label = "Mid-Morning",  Start = "10:00", End = "14:00" },
                new { Num = 3, Label = "Afternoon",    Start = "14:00", End = "18:00" },
                new { Num = 4, Label = "Evening",      Start = "18:00", End = "22:00" },
            };

        var created = 0;
        for (var d = req.FromDate; d <= req.ToDate; d = d.AddDays(1))
        {
            foreach (var shift in shiftsToCreate)
            {
                var exists = await _db.ShiftSchedules.AnyAsync(s =>
                    s.ClientId == resolvedClientBulk.Value &&
                    s.ScheduleDate == d &&
                    s.ShiftNumber == shift.Num);

                if (exists) continue;

                _db.ShiftSchedules.Add(new ShiftSchedule
                {
                    TenantId = TenantId,
                    ClientId = resolvedClientBulk.Value,
                    ScheduleDate = d,
                    ShiftNumber = shift.Num,
                    ShiftLabel = shift.Label,
                    StartTime = shift.Start,
                    EndTime = shift.End,
                    MaxChairs = req.MaxChairs,
                    IsActive = true
                });
                created++;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { created });
    }

    // PATCH /api/shifts/:id
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShiftScheduleRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin && !IsChargeNurse) return Forbid();

        var schedule = await _db.ShiftSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == TenantId);
        if (schedule == null) return NotFound();

        if (req.ShiftLabel != null) schedule.ShiftLabel = req.ShiftLabel;
        if (req.StartTime != null) schedule.StartTime = req.StartTime;
        if (req.EndTime != null) schedule.EndTime = req.EndTime;
        if (req.MaxChairs.HasValue) schedule.MaxChairs = req.MaxChairs.Value;
        if (req.IsActive.HasValue) schedule.IsActive = req.IsActive.Value;
        if (req.Notes != null) schedule.Notes = req.Notes;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /api/shifts/:id
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var schedule = await _db.ShiftSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == TenantId);
        if (schedule == null) return NotFound();

        _db.ShiftSchedules.Remove(schedule);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/shifts/:id/nurses - assign nurse to shift
    [HttpPost("{id}/nurses")]
    public async Task<IActionResult> AssignNurse(Guid id, [FromBody] AssignNurseRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin && !IsChargeNurse) return Forbid();

        var schedule = await _db.ShiftSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == TenantId);
        if (schedule == null) return NotFound();

        // Check already assigned
        var exists = await _db.ShiftNurseAssignments.AnyAsync(n =>
            n.ShiftScheduleId == id && n.NurseUserId == req.NurseUserId);
        if (exists)
            return BadRequest(new { message = "Nurse already assigned to this shift" });

        var assignment = new ShiftNurseAssignment
        {
            TenantId = TenantId,
            ShiftScheduleId = id,
            NurseUserId = req.NurseUserId,
            AssignmentRole = req.AssignmentRole,
            AssignedBy = CurrentUserId
        };

        _db.ShiftNurseAssignments.Add(assignment);
        await _db.SaveChangesAsync();
        return Ok(new { id = assignment.Id });
    }

    // DELETE /api/shifts/:id/nurses/:assignmentId
    [HttpDelete("{id}/nurses/{assignmentId}")]
    public async Task<IActionResult> RemoveNurse(Guid id, Guid assignmentId)
    {
        if (!IsPlAdmin && !IsClinicAdmin && !IsChargeNurse) return Forbid();

        var assignment = await _db.ShiftNurseAssignments
            .FirstOrDefaultAsync(n => n.Id == assignmentId && n.ShiftScheduleId == id && n.TenantId == TenantId);
        if (assignment == null) return NotFound();

        _db.ShiftNurseAssignments.Remove(assignment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // GET /api/shifts/history?from=&to=
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? clientId)
    {
        var resolvedClientId = clientId ?? ClientId;
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var schedules = await _db.ShiftSchedules
            .Include(s => s.NurseAssignments).ThenInclude(n => n.NurseUser)
            .Where(s => s.TenantId == TenantId
                     && s.ScheduleDate >= fromDate && s.ScheduleDate <= toDate)
            .Where(s => !resolvedClientId.HasValue || s.ClientId == resolvedClientId.Value)
            .OrderByDescending(s => s.ScheduleDate).ThenBy(s => s.ShiftNumber)
            .ToListAsync();

        var patientCounts = await _db.Sessions
            .Where(s => s.TenantId == TenantId
                     && s.SessionDate >= fromDate && s.SessionDate <= toDate)
            .Where(s => !resolvedClientId.HasValue || s.ClientId == resolvedClientId.Value)
            .GroupBy(s => new { s.SessionDate, s.ShiftNumber })
            .Select(g => new { g.Key.SessionDate, g.Key.ShiftNumber, Count = g.Count(), Filled = g.Count(x => x.Chair != null) })
            .ToListAsync();

        var countDict = patientCounts.ToDictionary(x => $"{x.SessionDate}_{x.ShiftNumber}");

        return Ok(schedules.Select(s =>
        {
            countDict.TryGetValue($"{s.ScheduleDate}_{s.ShiftNumber}", out var counts);
            return new ShiftScheduleDto(
                s.Id, s.ClientId, s.ScheduleDate,
                s.ShiftNumber, s.ShiftLabel, s.StartTime, s.EndTime,
                s.MaxChairs, s.IsActive, s.Notes,
                counts?.Count ?? 0, counts?.Filled ?? 0,
                s.NurseAssignments.Select(n => new ShiftNurseDto(
                    n.Id, n.NurseUserId, n.NurseUser.Name,
                    n.NurseUser.Email, n.AssignmentRole, n.AssignedAt
                )).ToList()
            );
        }));
    }
}