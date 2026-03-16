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

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? clientId,
        [FromQuery] string? search,
        [FromQuery] string? status)
    {
        Guid? resolvedClientId;
        if (IsPlAdmin)
            resolvedClientId = clientId ?? ClientId;
        else
            resolvedClientId = ClientId;

        var query = _db.Patients
            .Where(p => p.TenantId == TenantId && p.IsActive);

        if (resolvedClientId.HasValue)
            query = query.Where(p => p.ClientId == resolvedClientId.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(p =>
                p.Name.ToLower().Contains(search.ToLower()) ||
                (p.LisPatientId != null && p.LisPatientId.ToLower().Contains(search.ToLower())));

        var patients = await query.OrderBy(p => p.Name).ToListAsync();
        var patientIds = patients.Select(p => p.Id).ToList();

        // ── CDM chain: last result date and distinct date count per patient ──
        // Step 1: orders for these patients
        var orders = await _db.Orders
            .Where(o => patientIds.Contains(o.PatientId) && o.TenantId == TenantId)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();

        // Step 2: result dates from ResultHeaders (one ResultDatetime per header)
        var headerDates = orderIds.Count == 0
            ? new List<(Guid PatientId, DateTime ResultDatetime)>()
            : await _db.ResultHeaders
                .Where(h => orderIds.Contains(h.OrderId) && h.TenantId == TenantId && h.ResultDatetime != null)
                .Select(h => new { h.OrderId, h.ResultDatetime })
                .ToListAsync()
                .ContinueWith(t => t.Result
                    .Select(h => (
                        PatientId: orders.First(o => o.Id == h.OrderId).PatientId,
                        ResultDatetime: h.ResultDatetime!.Value
                    )).ToList());

        // Step 3: derive last date and distinct date count per patient in memory
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var latestByPatient = headerDates
            .GroupBy(x => x.PatientId)
            .ToDictionary(
                g => g.Key,
                g => DateOnly.FromDateTime(g.Max(x => x.ResultDatetime))
            );

        var dateCountByPatient = headerDates
            .GroupBy(x => x.PatientId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => DateOnly.FromDateTime(x.ResultDatetime)).Distinct().Count()
            );

        var dtos = patients.Select(p =>
        {
            latestByPatient.TryGetValue(p.Id, out var lastDate);
            var days = lastDate != default ? today.DayNumber - lastDate.DayNumber : (int?)null;
            var resultStatus = lastDate == default ? "nodata"
                : days > 30 ? "stale" : "ready";

            if (!string.IsNullOrEmpty(status) && status != resultStatus) return null;

            dateCountByPatient.TryGetValue(p.Id, out var dateCount);

            return new PatientDto(
                p.Id, p.Name, p.LisPatientId, p.PhilhealthNo,
                p.Birthdate, p.Gender, p.ContactNumber,
                p.IsActive, resultStatus, days,
                lastDate == default ? null : lastDate,
                dateCount
            );
        })
        .Where(p => p != null)
        .ToList();

        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var query = _db.Patients.Where(p => p.Id == id && p.TenantId == TenantId);
        if (!IsPlAdmin && ClientId.HasValue)
            query = query.Where(p => p.ClientId == ClientId.Value);

        var p = await query.FirstOrDefaultAsync();
        if (p == null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Latest ResultDatetime from CDM chain
        var lastHeader = await _db.Orders
            .Where(o => o.PatientId == id && o.TenantId == TenantId)
            .Join(_db.ResultHeaders, o => o.Id, h => h.OrderId, (o, h) => h)
            .Where(h => h.ResultDatetime != null)
            .OrderByDescending(h => h.ResultDatetime)
            .FirstOrDefaultAsync();

        var lastDate = lastHeader?.ResultDatetime != null
            ? DateOnly.FromDateTime(lastHeader.ResultDatetime.Value)
            : (DateOnly?)null;

        var days = lastDate.HasValue ? today.DayNumber - lastDate.Value.DayNumber : (int?)null;
        var resultStatus = lastDate == null ? "nodata" : days > 30 ? "stale" : "ready";

        return Ok(new PatientDto(p.Id, p.Name, p.LisPatientId, p.PhilhealthNo, p.Birthdate, p.Gender,
            p.ContactNumber, p.IsActive, resultStatus, days, lastDate));
    }

    // CDM §3.3: No manual patient creation. Patients are created automatically from HL7.
    // Corrections must be made in the LIS (HCLab). POST /api/patients is not permitted.

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (!IsClinicAdmin && !IsPlAdmin) return Forbid();

        var query = _db.Patients.Where(p => p.Id == id && p.TenantId == TenantId);
        if (!IsPlAdmin && ClientId.HasValue)
            query = query.Where(p => p.ClientId == ClientId.Value);

        var patient = await query.FirstOrDefaultAsync();
        if (patient == null) return NotFound();

        patient.IsActive = false;
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}