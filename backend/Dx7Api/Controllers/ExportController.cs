using Dx7Api.Data;
using Dx7Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/export")]
public class ExportController : TenantBaseController
{
    private readonly AppDbContext _db;
    public ExportController(AppDbContext db) => _db = db;

    // PRD: Export/Print = Charge Nurse only
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] ExportRequest req)
    {
        if (!IsChargeNurse)
            return Forbid();

        if (!ClientId.HasValue) return BadRequest("Client context required");

        var query = _db.Results
            .Include(r => r.Patient)
            .Where(r => r.TenantId == TenantId
                     && r.Patient.ClientId == ClientId.Value
                     && req.PatientIds.Contains(r.PatientId)
                     && r.ResultDate >= req.FromDate
                     && r.ResultDate <= req.ToDate);

        if (req.TestCodes != null && req.TestCodes.Count > 0)
            query = query.Where(r => req.TestCodes.Contains(r.TestCode));

        var results = await query
            .OrderBy(r => r.Patient.Name)
            .ThenBy(r => r.TestCode)
            .ThenByDescending(r => r.ResultDate)
            .ToListAsync();

        if (req.Format == "csv")
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("PatientName,LisPatientId,TestCode,TestName,ResultValue,Unit,ReferenceRange,AbnormalFlag,ResultDate,SourceLab");
            foreach (var r in results)
            {
                csv.AppendLine($"{r.Patient.Name},{r.Patient.LisPatientId},{r.TestCode},{r.TestName},{r.ResultValue},{r.ResultUnit},{r.ReferenceRange},{r.AbnormalFlag},{r.ResultDate},{r.SourceLab}");
            }
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"dx7_export_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        return Ok(results.Select(r => new ResultDto(
            r.Id, r.TestCode, r.TestName,
            r.ResultValue, r.ResultUnit, r.ReferenceRange,
            r.AbnormalFlag, r.ResultDate, r.SourceLab,
            (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - r.ResultDate.DayNumber),
            r.ResultStatus ?? "final",
            r.AccessionId
        )));
    }
}