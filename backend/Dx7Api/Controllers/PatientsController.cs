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
        // PL Admin can pass any clientId within their tenant
        // All other roles are locked to their own ClientId from JWT
        Guid? resolvedClientId;
        if (IsPlAdmin)
            resolvedClientId = clientId ?? ClientId;
        else
            resolvedClientId = ClientId; // always enforce own clinic

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

        var latestResults = await _db.Results
            .Where(r => patientIds.Contains(r.PatientId) && r.TenantId == TenantId)
            .GroupBy(r => r.PatientId)
            .Select(g => new { PatientId = g.Key, LastDate = g.Max(r => r.ResultDate) })
            .ToDictionaryAsync(x => x.PatientId, x => x.LastDate);

        // Count of distinct result dates per patient
        var resultDateCounts = await _db.Results
            .Where(r => patientIds.Contains(r.PatientId) && r.TenantId == TenantId)
            .GroupBy(r => new { r.PatientId, r.ResultDate })
            .Select(g => new { g.Key.PatientId, g.Key.ResultDate })
            .GroupBy(r => r.PatientId)
            .Select(g => new { PatientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PatientId, x => x.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var dtos = patients.Select(p =>
        {
            latestResults.TryGetValue(p.Id, out var lastDate);
            var days = lastDate != default ? today.DayNumber - lastDate.DayNumber : (int?)null;
            var resultStatus = lastDate == default ? "nodata"
                : days > 30 ? "stale" : "ready";

            if (!string.IsNullOrEmpty(status) && status != resultStatus) return null;

            resultDateCounts.TryGetValue(p.Id, out var dateCount);

            return new PatientDto(
                p.Id, p.Name, p.LisPatientId, p.PhilhealthNo,
                p.Birthdate, p.Gender, p.ContactNumber,
                p.IsActive, resultStatus, days, lastDate == default ? null : lastDate,
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
        // Always scope to tenant, and to client unless PL Admin
        var query = _db.Patients.Where(p => p.Id == id && p.TenantId == TenantId);
        if (!IsPlAdmin && ClientId.HasValue)
            query = query.Where(p => p.ClientId == ClientId.Value);

        var p = await query.FirstOrDefaultAsync();
        if (p == null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastResult = await _db.Results
            .Where(r => r.PatientId == id && r.TenantId == TenantId)
            .OrderByDescending(r => r.ResultDate)
            .FirstOrDefaultAsync();

        var days = lastResult != null ? today.DayNumber - lastResult.ResultDate.DayNumber : (int?)null;
        var resultStatus = lastResult == null ? "nodata" : days > 30 ? "stale" : "ready";

        return Ok(new PatientDto(p.Id, p.Name, p.LisPatientId, p.PhilhealthNo, p.Birthdate, p.Gender,
            p.ContactNumber, p.IsActive, resultStatus, days, lastResult?.ResultDate));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePatientRequest req)
    {
        // Charge nurse and admins can create patients; shift nurse and MD cannot
        if (!IsChargeNurse && !IsClinicAdmin && !IsPlAdmin)
            return Forbid();

        if (!ClientId.HasValue) return BadRequest(new { message = "Client context required" });

        var patient = new Patient
        {
            TenantId = TenantId,
            ClientId = ClientId.Value,
            Name = req.Name,
            LisPatientId = req.LisPatientId,
            PhilhealthNo = req.PhilhealthNo,
            Birthdate = req.Birthdate,
            Gender = req.Gender,
            ContactNumber = req.ContactNumber
        };

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = patient.Id }, patient);
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

        patient.IsActive = false;
        patient.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}