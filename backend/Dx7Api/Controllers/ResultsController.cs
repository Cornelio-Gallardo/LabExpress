using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

/// <summary>
/// All result reads go through the CDM §9 chain:
///   LabOrder → ResultHeader → ResultValue → SXA_Analyte
/// The flat Results table is no longer queried here.
/// </summary>
[ApiController]
[Route("api/results")]
public class ResultsController : TenantBaseController
{
    private readonly AppDbContext _db;
    public ResultsController(AppDbContext db) => _db = db;

    // ── GET /api/results/current/{patientId} ──────────────────────────────────
    // Latest value per analyte — for PatientRoster status indicators.
    [HttpGet("current/{patientId}")]
    public async Task<IActionResult> GetCurrent(Guid patientId)
    {
        if (!await PatientBelongsToTenant(patientId)) return NotFound();
        var values = await LoadCdmValues(patientId);
        var today  = DateOnly.FromDateTime(DateTime.UtcNow);

        var current = values
            .GroupBy(v => v.AnalyteCode ?? v.Id.ToString())
            .Select(g =>
            {
                var latest = g.OrderByDescending(v => v.ResultHeader.ResultDatetime ?? DateTime.MinValue).First();
                var date   = DateOnly.FromDateTime(latest.ResultHeader.ResultDatetime ?? DateTime.UtcNow);
                return ToDtoFromValue(latest, today.DayNumber - date.DayNumber);
            })
            .OrderBy(r => r.TestName)
            .ToList();

        return Ok(current);
    }

    // ── GET /api/results/compare/{patientId}?count=3 ─────────────────────────
    // Last N result dates per analyte as columns — primary SessionView data source.
    [HttpGet("compare/{patientId}")]
    public async Task<IActionResult> GetCompare(Guid patientId, [FromQuery] int count = 3)
    {
        count = Math.Clamp(count, 1, 10);
        if (!await PatientBelongsToTenant(patientId)) return NotFound();

        var values = await LoadCdmValues(patientId);
        var today  = DateOnly.FromDateTime(DateTime.UtcNow);

        var recentDates = values
            .Select(v => DateOnly.FromDateTime(v.ResultHeader.ResultDatetime ?? DateTime.UtcNow))
            .Distinct()
            .OrderByDescending(d => d)
            .Take(count)
            .ToList();

        var rows = values
            .GroupBy(v => v.AnalyteCode ?? v.Id.ToString())
            .Select(g =>
            {
                var byDate = g.ToLookup(v => DateOnly.FromDateTime(v.ResultHeader.ResultDatetime ?? DateTime.UtcNow));
                var sample = g.OrderByDescending(v => v.ResultHeader.ResultDatetime).First();
                return new
                {
                    testCode = sample.AnalyteCode ?? "",
                    testName = sample.Analyte?.DisplayName ?? sample.AnalyteCode ?? "",
                    unit     = sample.Unit ?? sample.Analyte?.DefaultUnit,
                    refRange = sample.ReferenceRangeRaw,
                    cols     = recentDates.Select(d =>
                    {
                        var rv = byDate[d].FirstOrDefault();
                        return rv == null ? null : ToDtoFromValue(rv, today.DayNumber - d.DayNumber);
                    }).ToList()
                };
            })
            .OrderBy(r => r.testName)
            .ToList();

        return Ok(new { dates = recentDates, rows });
    }

    // ── GET /api/results/by-date/{patientId} ─────────────────────────────────
    // All results grouped by result date — used by ResultReportModal in PatientsView.
    [HttpGet("by-date/{patientId}")]
    public async Task<IActionResult> GetByDate(Guid patientId)
    {
        if (!await PatientBelongsToTenant(patientId)) return NotFound();

        var values = await LoadCdmValues(patientId);
        var today  = DateOnly.FromDateTime(DateTime.UtcNow);

        var grouped = values
            .GroupBy(v => DateOnly.FromDateTime(v.ResultHeader.ResultDatetime ?? DateTime.UtcNow))
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                date         = g.Key,
                displayDate  = g.Key.ToString("yyyy-MM-dd"),
                totalTests   = g.Count(),
                finalCount   = g.Count(),
                pendingCount = 0,
                results      = g.OrderBy(v => v.Analyte?.DisplayName ?? v.AnalyteCode)
                                .Select(v => ToDtoFromValue(v, today.DayNumber - g.Key.DayNumber))
                                .ToList()
            })
            .ToList();

        return Ok(grouped);
    }

    // ── GET /api/results/orders/{patientId} ───────────────────────────────────
    // Full CDM chain: Orders → ResultHeaders → ResultValues.
    // Primary data source for SessionView orders panel.
    [HttpGet("orders/{patientId}")]
    public async Task<IActionResult> GetOrders(Guid patientId)
    {
        if (!await PatientBelongsToTenant(patientId)) return NotFound();

        var orders = await _db.Orders
            .Where(o => o.PatientId == patientId && o.TenantId == TenantId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        if (orders.Count == 0) return Ok(new List<object>());

        var orderMap = orders.ToDictionary(o => o.Id);
        var orderIds = orders.Select(o => o.Id).ToList();

        var headers = await _db.ResultHeaders
            .Include(h => h.SxaTest)
            .Where(h => h.TenantId == TenantId && orderIds.Contains(h.OrderId))
            .ToListAsync();

        foreach (var h in headers)
            if (orderMap.TryGetValue(h.OrderId, out var o)) h.Order = o;

        var headerMap = headers.ToDictionary(h => h.Id);
        var headerIds = headers.Select(h => h.Id).ToList();

        var values = await _db.ResultValues
            .Include(v => v.Analyte)
            .Where(v => v.TenantId == TenantId && headerIds.Contains(v.ResultHeaderId))
            .ToListAsync();

        foreach (var v in values)
            if (headerMap.TryGetValue(v.ResultHeaderId, out var h)) v.ResultHeader = h;

        var valuesByHeader  = values.GroupBy(v => v.ResultHeaderId).ToDictionary(g => g.Key, g => g.ToList());
        var headersByOrder  = headers.GroupBy(h => h.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var result = orders.Select(o => new LabOrderDto(
            o.Id,
            o.AccessionNumber,
            o.ReleasedAt,
            o.CreatedAt,
            o.SourceHl7MessageId,
            (headersByOrder.TryGetValue(o.Id, out var hdrs) ? hdrs : new())
                .Select(h => new ResultHeaderDto(
                    h.Id, h.OrderId, h.SxaTestId, h.SxaTest?.CanonicalName,
                    h.SpecimenType, h.CollectionDatetime, h.ResultDatetime,
                    h.SourceHl7MessageId,
                    (valuesByHeader.TryGetValue(h.Id, out var vals) ? vals : new())
                        .Select(v => new ResultValueDto(
                            v.Id, v.ResultHeaderId, v.AnalyteCode,
                            v.Analyte?.DisplayName, v.DisplayValue,
                            v.ValueNumeric, v.Unit,
                            v.ReferenceRangeLow, v.ReferenceRangeHigh, v.ReferenceRangeRaw,
                            v.AbnormalFlag, v.RawHl7Segment
                        )).ToList()
                )).ToList()
        )).ToList();

        return Ok(result);
    }

    // ── POST /api/results — manual entry (admin/sysad only) ───────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateResultRequest req)
    {
        if (!IsPlAdmin && UserRole != "sysad") return Forbid();

        var patient = await _db.Patients.FirstOrDefaultAsync(p =>
            p.Id == req.PatientId && p.TenantId == TenantId);
        if (patient == null) return BadRequest(new { message = "Patient not found in this tenant" });

        // Manual entries: create a synthetic HL7_Message + LabOrder + ResultHeader + ResultValue
        var msg = new Hl7Message
        {
            Id               = Guid.NewGuid(),
            TenantId         = TenantId,
            MessageControlId = $"MANUAL-{Guid.NewGuid():N}",
            RawPayload       = $"[MANUAL ENTRY] {req.TestName} for patient {req.PatientId}",
            ReceivedAt       = DateTime.UtcNow,
            ProcessedFlag    = true,
            QuarantineFlag   = false
        };
        _db.Hl7Messages.Add(msg);

        var order = new LabOrder
        {
            Id                 = Guid.NewGuid(),
            TenantId           = TenantId,
            ClientId           = patient.ClientId,
            PatientId          = req.PatientId,
            AccessionNumber    = req.AccessionId ?? $"MANUAL-{DateTime.UtcNow:yyyyMMdd}-{req.TestCode}",
            SourceHl7MessageId = msg.Id,
            ReleasedAt         = DateTime.UtcNow,
            CreatedAt          = DateTime.UtcNow
        };
        _db.Orders.Add(order);

        var resultDt = req.ResultDate.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc);
        var header = new ResultHeader
        {
            Id                 = Guid.NewGuid(),
            OrderId            = order.Id,
            TenantId           = TenantId,
            SourceHl7MessageId = msg.Id,
            ResultDatetime     = resultDt,
            CollectionDatetime = resultDt
        };
        _db.ResultHeaders.Add(header);

        var value = new ResultValue
        {
            Id             = Guid.NewGuid(),
            ResultHeaderId = header.Id,
            TenantId       = TenantId,
            AnalyteCode    = req.TestCode,
            DisplayValue   = req.ResultValue ?? "",
            Unit           = req.ResultUnit,
            ReferenceRangeRaw = req.ReferenceRange,
            AbnormalFlag   = req.AbnormalFlag,
            RawHl7Segment  = $"[MANUAL] {req.TestName}: {req.ResultValue} {req.ResultUnit}"
        };
        _db.ResultValues.Add(value);

        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCurrent), new { patientId = req.PatientId }, value.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Three-step load — avoids EF global-filter issues on ThenInclude chains.
    private async Task<List<ResultValue>> LoadCdmValues(Guid patientId)
    {
        var orders = await _db.Orders
            .Where(o => o.TenantId == TenantId && o.PatientId == patientId)
            .ToListAsync();
        if (orders.Count == 0) return new();

        var orderMap = orders.ToDictionary(o => o.Id);
        var orderIds = orders.Select(o => o.Id).ToList();

        var headers = await _db.ResultHeaders
            .Include(h => h.SxaTest)
            .Where(h => h.TenantId == TenantId && orderIds.Contains(h.OrderId))
            .ToListAsync();

        foreach (var h in headers)
            if (orderMap.TryGetValue(h.OrderId, out var o)) h.Order = o;

        var headerMap = headers.ToDictionary(h => h.Id);
        var headerIds = headers.Select(h => h.Id).ToList();
        if (headerIds.Count == 0) return new();

        var values = await _db.ResultValues
            .Include(v => v.Analyte)
            .Where(v => v.TenantId == TenantId && headerIds.Contains(v.ResultHeaderId))
            .ToListAsync();

        foreach (var v in values)
            if (headerMap.TryGetValue(v.ResultHeaderId, out var h)) v.ResultHeader = h;

        return values;
    }

    private async Task<bool> PatientBelongsToTenant(Guid patientId) =>
        await _db.Patients.AnyAsync(p =>
            p.Id == patientId && p.TenantId == TenantId &&
            (!ClientId.HasValue || p.ClientId == ClientId.Value));

    private static ResultDto ToDtoFromValue(ResultValue v, int days)
    {
        var date = DateOnly.FromDateTime(v.ResultHeader.ResultDatetime ?? DateTime.UtcNow);
        return new ResultDto(
            v.Id,
            TestCode:        v.AnalyteCode ?? "",
            TestName:        v.Analyte?.DisplayName ?? v.AnalyteCode ?? "",
            ResultValue:     v.DisplayValue,
            ResultUnit:      v.Unit,
            ReferenceRange:  v.ReferenceRangeRaw,
            AbnormalFlag:    v.AbnormalFlag,
            ResultDate:      date,
            SourceLab:       null,
            DaysSinceResult: days,
            ResultStatus:    "final",
            AccessionId:     v.ResultHeader.Order?.AccessionNumber,
            AnalyteCode:     v.AnalyteCode,
            SxaTestId:       v.ResultHeader.SxaTestId,
            ResultHeaderId:  v.ResultHeaderId,
            OrderId:         v.ResultHeader.OrderId,
            Hl7MessageId:    v.ResultHeader.SourceHl7MessageId
        );
    }
}