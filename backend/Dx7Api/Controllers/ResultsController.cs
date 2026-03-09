using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/results")]
public class ResultsController : TenantBaseController
{
    private readonly AppDbContext _db;
    public ResultsController(AppDbContext db) => _db = db;

    // Latest result per test for a patient — includes pending (null value) records
    [HttpGet("current/{patientId}")]
    public async Task<IActionResult> GetCurrent(Guid patientId)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p =>
            p.Id == patientId && p.TenantId == TenantId &&
            (!ClientId.HasValue || p.ClientId == ClientId.Value));
        if (patient == null) return NotFound();

        var results = await _db.Results
            .Where(r => r.PatientId == patientId && r.TenantId == TenantId)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = results
            .GroupBy(r => r.TestCode)
            .Select(g =>
            {
                // Prefer final/corrected over pending when grouping
                var ordered = g.OrderByDescending(r =>
                    r.ResultStatus == "final" || r.ResultStatus == "corrected" ? 1 : 0)
                    .ThenByDescending(r => r.ResultDate)
                    .ToList();
                var latest = ordered.First();
                var days = today.DayNumber - latest.ResultDate.DayNumber;
                return ToDto(latest, days);
            })
            .OrderBy(r => r.TestName)
            .ToList();

        return Ok(current);
    }

    // Full history for a patient+test — all dates including pending
    [HttpGet("history/{patientId}/{testCode}")]
    public async Task<IActionResult> GetHistory(
        Guid patientId, string testCode,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p =>
            p.Id == patientId && p.TenantId == TenantId &&
            (!ClientId.HasValue || p.ClientId == ClientId.Value));
        if (patient == null) return NotFound();

        var query = _db.Results
            .Where(r => r.PatientId == patientId && r.TenantId == TenantId
                     && r.TestCode == testCode);

        if (from.HasValue) query = query.Where(r => r.ResultDate >= from.Value);
        if (to.HasValue)   query = query.Where(r => r.ResultDate <= to.Value);

        var today   = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = await query.OrderByDescending(r => r.ResultDate).ToListAsync();

        return Ok(results.Select(r => ToDto(r, today.DayNumber - r.ResultDate.DayNumber)));
    }

    // All results for a patient grouped by date — for the date-tab view
    [HttpGet("by-date/{patientId}")]
    public async Task<IActionResult> GetByDate(Guid patientId)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p =>
            p.Id == patientId && p.TenantId == TenantId &&
            (!ClientId.HasValue || p.ClientId == ClientId.Value));
        if (patient == null) return NotFound();

        var results = await _db.Results
            .Where(r => r.PatientId == patientId && r.TenantId == TenantId)
            .OrderByDescending(r => r.ResultDate)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var grouped = results
            .GroupBy(r => r.ResultDate)
            .Select(g => new {
                date        = g.Key,
                displayDate = g.Key.ToString("yyyy-MM-dd"),
                totalTests  = g.Count(),
                pendingCount = g.Count(r => r.ResultStatus == "pending"),
                finalCount  = g.Count(r => r.ResultStatus == "final" || r.ResultStatus == "corrected"),
                results     = g.OrderBy(r => r.TestName).Select(r => ToDto(r, today.DayNumber - r.ResultDate.DayNumber)).ToList()
            })
            .OrderByDescending(g => g.date)
            .ToList();

        return Ok(grouped);
    }

    // HL7 ingestion endpoint — restricted to admins
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateResultRequest req)
    {
        if (!IsPlAdmin && UserRole != "sysad") return Forbid();

        var patient = await _db.Patients.FirstOrDefaultAsync(p =>
            p.Id == req.PatientId && p.TenantId == TenantId);
        if (patient == null) return BadRequest(new { message = "Patient not found in this tenant" });

        var result = new Result
        {
            TenantId       = TenantId,
            PatientId      = req.PatientId,
            TestCode       = req.TestCode,
            TestName       = req.TestName,
            ResultValue    = req.ResultValue,
            ResultUnit     = req.ResultUnit,
            ReferenceRange = req.ReferenceRange,
            AbnormalFlag   = req.AbnormalFlag,
            ResultDate     = req.ResultDate,
            ResultStatus   = string.IsNullOrEmpty(req.ResultValue) ? "pending" : "final",
            SourceLab      = req.SourceLab,
            AccessionId    = req.AccessionId,
        };

        _db.Results.Add(result);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCurrent), new { patientId = req.PatientId }, result.Id);
    }

    private static ResultDto ToDto(Result r, int days) => new(
        r.Id, r.TestCode, r.TestName,
        r.ResultValue, r.ResultUnit, r.ReferenceRange,
        r.AbnormalFlag, r.ResultDate, r.SourceLab, days,
        r.ResultStatus ?? "final",
        r.AccessionId
    );
}