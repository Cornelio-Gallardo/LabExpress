using Dx7Api.Data;
using Dx7Api.DTOs;
using Dx7Api.Models;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Geom;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/export")]
public class ExportController : TenantBaseController
{
    private readonly AppDbContext _db;
    public ExportController(AppDbContext db) => _db = db;

    // GET /api/export/session-pdf?sessionId=xxx
    [HttpGet("session-pdf")]
    public async Task<IActionResult> SessionPdf(
        [FromQuery] Guid sessionId,
        [FromQuery] bool allResults = true,
        [FromQuery] bool notes = true)
    {
        try
        {
            var session = await _db.Sessions
                .Include(s => s.Patient)
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == TenantId);
            if (session == null) return NotFound("Session not found");

            var client     = await _db.Clients.FindAsync(session.ClientId);
            var clinicName = client?.Name ?? "Dx7 Clinic";

            // ── Load results from CDM chain ───────────────────────────────────
            var orders = await _db.Orders
                .Where(o => o.PatientId == session.PatientId && o.TenantId == TenantId)
                .ToListAsync();

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

            var values = headerIds.Count == 0 ? new List<ResultValue>()
                : await _db.ResultValues
                    .Include(v => v.Analyte)
                    .Where(v => v.TenantId == TenantId && headerIds.Contains(v.ResultHeaderId))
                    .ToListAsync();
            foreach (var v in values)
                if (headerMap.TryGetValue(v.ResultHeaderId, out var h)) v.ResultHeader = h;

            // Latest value per analyte for the priority/all sections
            var today   = DateOnly.FromDateTime(DateTime.UtcNow);
            var current = values
                .GroupBy(v => v.AnalyteCode ?? v.Id.ToString())
                .Select(g => g.OrderByDescending(v => v.ResultHeader.ResultDatetime).First())
                .OrderBy(v => v.Analyte?.DisplayName ?? v.AnalyteCode)
                .ToList();

            var mdNotes = await _db.MdNotes
                .Include(n => n.MdUser)
                .Where(n => n.SessionId == sessionId && n.TenantId == TenantId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            // ── Build PDF ─────────────────────────────────────────────────────
            using var ms     = new MemoryStream();
            var writer       = new PdfWriter(ms);
            var pdf          = new PdfDocument(writer);
            var doc          = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            doc.SetMargins(36, 36, 36, 36);

            var fontBold   = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var navy  = new DeviceRgb(30, 58, 138);
            var blue  = new DeviceRgb(37, 99, 235);
            var gray  = new DeviceRgb(107, 114, 128);
            var light = new DeviceRgb(239, 246, 255);
            var red   = new DeviceRgb(220, 38, 38);

            // Header
            var headerTbl = new Table(UnitValue.CreatePercentArray(new float[]{ 1, 2 }))
                .UseAllAvailableWidth().SetMarginBottom(10);
            headerTbl.AddCell(new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                .Add(new Paragraph("Dx7").SetFont(fontBold).SetFontSize(28).SetFontColor(navy)));
            headerTbl.AddCell(new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT)
                .Add(new Paragraph(clinicName).SetFont(fontBold).SetFontSize(11).SetFontColor(navy))
                .Add(new Paragraph("Lab Results Report · " + DateTime.Now.ToString("MMMM dd, yyyy hh:mm tt"))
                    .SetFont(fontNormal).SetFontSize(9).SetFontColor(gray)));
            doc.Add(headerTbl);
            doc.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(1.5f))
                .SetStrokeColor(blue).SetMarginBottom(10));

            // Patient bar
            var patBar = new Table(UnitValue.CreatePercentArray(new float[]{ 3, 2, 2, 2 }))
                .UseAllAvailableWidth().SetMarginBottom(14).SetBackgroundColor(light);
            void AddPatCell(string label, string val) =>
                patBar.AddCell(new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(8)
                    .Add(new Paragraph(label).SetFont(fontBold).SetFontSize(8).SetFontColor(gray))
                    .Add(new Paragraph(val).SetFont(fontBold).SetFontSize(11).SetFontColor(navy)));
            AddPatCell("PATIENT",  session.Patient.Name);
            AddPatCell("DATE",     session.SessionDate.ToString("MMMM dd, yyyy"));
            AddPatCell("SHIFT",    "Shift " + session.ShiftNumber + (session.Chair != null ? " · Chair " + session.Chair : ""));
            AddPatCell("LIS ID",   session.Patient.LisPatientId ?? "—");
            doc.Add(patBar);

            // All Results table
            if (allResults && current.Any())
            {
                doc.Add(new Paragraph("ALL LAB RESULTS")
                    .SetFont(fontBold).SetFontSize(10).SetFontColor(navy)
                    .SetBorderBottom(new iText.Layout.Borders.SolidBorder(blue, 1))
                    .SetMarginBottom(8));

                var tbl = new Table(UnitValue.CreatePercentArray(new float[]{ 3f, 1.5f, 1f, 1f, 2f, 2f }))
                    .UseAllAvailableWidth().SetMarginBottom(14).SetFontSize(9);

                void AddTh(string t) => tbl.AddHeaderCell(new Cell()
                    .SetBackgroundColor(new DeviceRgb(249, 250, 251))
                    .SetFont(fontBold).SetFontColor(gray).SetFontSize(8).SetPadding(6)
                    .Add(new Paragraph(t)));
                AddTh("Analyte"); AddTh("Value"); AddTh("Unit"); AddTh("Flag"); AddTh("Ref Range"); AddTh("Date");

                foreach (var v in current)
                {
                    var flagColor = v.AbnormalFlag == "H" ? red
                        : v.AbnormalFlag == "L" ? new DeviceRgb(37, 99, 235) : navy;
                    var rowBg = string.IsNullOrEmpty(v.AbnormalFlag) ? null : new DeviceRgb(255, 247, 247);
                    var date  = v.ResultHeader.ResultDatetime.HasValue
                        ? DateOnly.FromDateTime(v.ResultHeader.ResultDatetime.Value).ToString("MM/dd/yyyy")
                        : "—";

                    void AddTd(string t, bool bold = false, DeviceRgb? color = null)
                    {
                        var cell = new Cell().SetPadding(6)
                            .SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(243, 244, 246), 1));
                        if (rowBg != null) cell.SetBackgroundColor(rowBg);
                        var p = new Paragraph(t ?? "—").SetFont(bold ? fontBold : fontNormal);
                        if (color != null) p.SetFontColor(color);
                        cell.Add(p);
                        tbl.AddCell(cell);
                    }

                    AddTd(v.Analyte?.DisplayName ?? v.AnalyteCode ?? "—", true, navy);
                    AddTd(v.DisplayValue ?? "—", true, flagColor);
                    AddTd(v.Unit ?? "—");
                    AddTd(v.AbnormalFlag ?? "—", true, string.IsNullOrEmpty(v.AbnormalFlag) ? gray : flagColor);
                    AddTd(v.ReferenceRangeRaw ?? "—");
                    AddTd(date);
                }
                doc.Add(tbl);
            }

            // MD Notes
            if (notes && mdNotes.Any())
            {
                doc.Add(new Paragraph("MD NOTES")
                    .SetFont(fontBold).SetFontSize(10).SetFontColor(navy)
                    .SetBorderBottom(new iText.Layout.Borders.SolidBorder(blue, 1))
                    .SetMarginBottom(8));
                foreach (var n in mdNotes)
                {
                    var noteBox = new Cell()
                        .SetBorder(new iText.Layout.Borders.SolidBorder(new DeviceRgb(229, 231, 235), 1))
                        .SetPadding(10);
                    noteBox.Add(new Paragraph((n.MdUser?.Name ?? "MD") + " · " + n.CreatedAt.ToString("MMM dd, yyyy hh:mm tt"))
                        .SetFont(fontBold).SetFontSize(9).SetFontColor(gray));
                    noteBox.Add(new Paragraph(n.NoteText)
                        .SetFont(fontNormal).SetFontSize(10).SetFontColor(new DeviceRgb(55, 65, 81)));
                    doc.Add(new Table(1).UseAllAvailableWidth().AddCell(noteBox).SetMarginBottom(8));
                }
            }

            // Footer
            doc.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(0.5f))
                .SetStrokeColor(gray).SetMarginTop(10));
            doc.Add(new Paragraph("Dx7 Clinical Information System · Results shown as-is from laboratory source. No interpretation. Data only.")
                .SetFont(fontNormal).SetFontSize(8).SetFontColor(gray).SetTextAlignment(TextAlignment.CENTER));

            doc.Close();

            var filename = $"DX7_Results_{session.Patient.Name.Replace(",", "").Replace(" ", "_")}_{session.SessionDate:yyyyMMdd}.pdf";
            return File(ms.ToArray(), "application/pdf", filename);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    // POST /api/export/shift-pdf — batch PDF for all sessions in a shift
    [HttpPost("shift-pdf")]
    public async Task<IActionResult> ShiftPdf([FromBody] ShiftPdfRequest req)
    {
        if (!IsChargeNurse && !IsClinicAdmin && !IsPlAdmin) return Forbid();
        if (req.SessionIds == null || req.SessionIds.Count == 0) return BadRequest("No sessions specified");

        try
        {
            using var ms     = new MemoryStream();
            var writer       = new PdfWriter(ms);
            var pdf          = new PdfDocument(writer);
            var doc          = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            doc.SetMargins(36, 36, 36, 36);

            var fontBold   = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var navy  = new DeviceRgb(30, 58, 138);
            var blue  = new DeviceRgb(37, 99, 235);
            var gray  = new DeviceRgb(107, 114, 128);
            var red   = new DeviceRgb(220, 38, 38);
            var light = new DeviceRgb(239, 246, 255);

            bool firstPage = true;

            foreach (var sessionId in req.SessionIds)
            {
                var session = await _db.Sessions
                    .Include(s => s.Patient)
                    .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == TenantId);
                if (session == null) continue;

                if (!firstPage) doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                firstPage = false;

                var client     = await _db.Clients.FindAsync(session.ClientId);
                var clinicName = client?.Name ?? "Dx7 Clinic";

                var orders = await _db.Orders
                    .Where(o => o.PatientId == session.PatientId && o.TenantId == TenantId)
                    .ToListAsync();
                var orderIds  = orders.Select(o => o.Id).ToList();
                var orderMap  = orders.ToDictionary(o => o.Id);

                var headers = await _db.ResultHeaders
                    .Include(h => h.SxaTest)
                    .Where(h => h.TenantId == TenantId && orderIds.Contains(h.OrderId))
                    .ToListAsync();
                foreach (var h in headers)
                    if (orderMap.TryGetValue(h.OrderId, out var o)) h.Order = o;

                var headerMap  = headers.ToDictionary(h => h.Id);
                var headerIds  = headers.Select(h => h.Id).ToList();
                var values     = headerIds.Count == 0 ? new List<ResultValue>()
                    : await _db.ResultValues.Include(v => v.Analyte)
                        .Where(v => v.TenantId == TenantId && headerIds.Contains(v.ResultHeaderId))
                        .ToListAsync();
                foreach (var v in values)
                    if (headerMap.TryGetValue(v.ResultHeaderId, out var h)) v.ResultHeader = h;

                var current = values
                    .GroupBy(v => v.AnalyteCode ?? v.Id.ToString())
                    .Select(g => g.OrderByDescending(v => v.ResultHeader.ResultDatetime).First())
                    .OrderBy(v => v.Analyte?.DisplayName ?? v.AnalyteCode)
                    .ToList();

                // Patient header
                var headerTbl = new Table(UnitValue.CreatePercentArray(new float[]{ 1, 2 }))
                    .UseAllAvailableWidth().SetMarginBottom(8);
                headerTbl.AddCell(new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                    .Add(new Paragraph("Dx7").SetFont(fontBold).SetFontSize(22).SetFontColor(navy)));
                headerTbl.AddCell(new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .Add(new Paragraph(clinicName).SetFont(fontBold).SetFontSize(10).SetFontColor(navy))
                    .Add(new Paragraph("Shift Report · " + session.SessionDate.ToString("MMMM dd, yyyy"))
                        .SetFont(fontNormal).SetFontSize(8).SetFontColor(gray)));
                doc.Add(headerTbl);
                doc.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(1f))
                    .SetStrokeColor(blue).SetMarginBottom(8));

                var patBar = new Table(UnitValue.CreatePercentArray(new float[]{ 3, 2, 2, 2 }))
                    .UseAllAvailableWidth().SetMarginBottom(10).SetBackgroundColor(light);
                void AddPat(string label, string val) =>
                    patBar.AddCell(new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(6)
                        .Add(new Paragraph(label).SetFont(fontBold).SetFontSize(7).SetFontColor(gray))
                        .Add(new Paragraph(val).SetFont(fontBold).SetFontSize(10).SetFontColor(navy)));
                AddPat("PATIENT",  session.Patient.Name);
                AddPat("DATE",     session.SessionDate.ToString("MMMM dd, yyyy"));
                AddPat("SHIFT",    "Shift " + session.ShiftNumber + (session.Chair != null ? " · " + session.Chair : ""));
                AddPat("LIS ID",   session.Patient.LisPatientId ?? "—");
                doc.Add(patBar);

                if (current.Any())
                {
                    var tbl = new Table(UnitValue.CreatePercentArray(new float[]{ 3f, 1.5f, 1f, 1f, 2f, 2f }))
                        .UseAllAvailableWidth().SetMarginBottom(10).SetFontSize(8);
                    foreach (var th in new[]{"Analyte","Value","Unit","Flag","Ref Range","Date"})
                        tbl.AddHeaderCell(new Cell()
                            .SetBackgroundColor(new DeviceRgb(249, 250, 251))
                            .SetFont(fontBold).SetFontColor(gray).SetFontSize(7).SetPadding(5)
                            .Add(new Paragraph(th)));

                    foreach (var v in current)
                    {
                        var flagColor = v.AbnormalFlag == "H" ? red
                            : v.AbnormalFlag == "L" ? new DeviceRgb(37, 99, 235) : navy;
                        var date = v.ResultHeader.ResultDatetime.HasValue
                            ? DateOnly.FromDateTime(v.ResultHeader.ResultDatetime.Value).ToString("MM/dd/yy") : "—";
                        void AddTd(string t, bool bold = false, DeviceRgb? color = null)
                        {
                            var cell = new Cell().SetPadding(5)
                                .SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(243, 244, 246), 1));
                            var p = new Paragraph(t ?? "—").SetFont(bold ? fontBold : fontNormal);
                            if (color != null) p.SetFontColor(color);
                            cell.Add(p); tbl.AddCell(cell);
                        }
                        AddTd(v.Analyte?.DisplayName ?? v.AnalyteCode ?? "—", true, navy);
                        AddTd(v.DisplayValue ?? "—", true, flagColor);
                        AddTd(v.Unit ?? "—");
                        AddTd(v.AbnormalFlag ?? "—", true, string.IsNullOrEmpty(v.AbnormalFlag) ? gray : flagColor);
                        AddTd(v.ReferenceRangeRaw ?? "—");
                        AddTd(date);
                    }
                    doc.Add(tbl);
                }
                else
                {
                    doc.Add(new Paragraph("No lab results on file.")
                        .SetFont(fontNormal).SetFontSize(9).SetFontColor(gray).SetMarginBottom(10));
                }
            }

            doc.Close();
            var filename = $"DX7_ShiftReport_{DateTime.UtcNow:yyyyMMdd}.pdf";
            return File(ms.ToArray(), "application/pdf", filename);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    // GET /api/export/urr?date=2024-01-15&clientId=xxx — URR/Kt/V audit defense
    [HttpGet("urr")]
    public async Task<IActionResult> UrrReport([FromQuery] DateOnly? date, [FromQuery] Guid? clientId)
    {
        if (!IsChargeNurse && !IsClinicAdmin && !IsPlAdmin) return Forbid();

        var resolvedClient = clientId ?? ClientId;
        if (!resolvedClient.HasValue)
        {
            var dbUser = await _db.Users.FindAsync(CurrentUserId);
            resolvedClient = dbUser?.ClientId;
        }

        var reportDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Get all patients with sessions on this date
        var sessions = await _db.Sessions
            .Include(s => s.Patient)
            .Where(s => s.TenantId == TenantId
                     && s.SessionDate == reportDate
                     && (!resolvedClient.HasValue || s.ClientId == resolvedClient.Value))
            .ToListAsync();

        if (sessions.Count == 0) return Ok(Array.Empty<object>());

        var patientIds = sessions.Select(s => s.PatientId).Distinct().ToList();
        var cutoff = reportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-7);
        var ceiling = reportDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // Load result values for BUNPRE and BUNPOST within the last 7 days
        var orders = await _db.Orders
            .Where(o => o.TenantId == TenantId && patientIds.Contains(o.PatientId))
            .ToListAsync();
        var orderMap  = orders.ToDictionary(o => o.Id);
        var orderIds  = orders.Select(o => o.Id).ToList();

        var headers = await _db.ResultHeaders
            .Where(h => h.TenantId == TenantId && orderIds.Contains(h.OrderId)
                     && h.ResultDatetime >= cutoff && h.ResultDatetime <= ceiling)
            .ToListAsync();
        foreach (var h in headers)
            if (orderMap.TryGetValue(h.OrderId, out var o)) h.Order = o;

        var headerIds = headers.Select(h => h.Id).ToList();
        var values = headerIds.Count == 0 ? new List<ResultValue>()
            : await _db.ResultValues
                .Where(v => v.TenantId == TenantId && headerIds.Contains(v.ResultHeaderId)
                         && (v.AnalyteCode == "BUNPRE" || v.AnalyteCode == "BUNPOST"))
                .ToListAsync();
        foreach (var v in values)
            if (headers.FirstOrDefault(h => h.Id == v.ResultHeaderId) is { } h)
                v.ResultHeader = h;

        var rows = new List<UrRDto>();
        foreach (var s in sessions.OrderBy(s => s.Patient.Name))
        {
            var patValues = values.Where(v => v.ResultHeader.Order?.PatientId == s.PatientId).ToList();
            var bunPreVal  = patValues.Where(v => v.AnalyteCode == "BUNPRE")
                                      .OrderByDescending(v => v.ResultHeader.ResultDatetime).FirstOrDefault();
            var bunPostVal = patValues.Where(v => v.AnalyteCode == "BUNPOST")
                                      .OrderByDescending(v => v.ResultHeader.ResultDatetime).FirstOrDefault();

            decimal? bunPre  = bunPreVal?.ValueNumeric;
            decimal? bunPost = bunPostVal?.ValueNumeric;

            decimal? urr = null;
            decimal? ktv = null;
            if (bunPre.HasValue && bunPost.HasValue && bunPre.Value > 0)
            {
                var R = (double)(bunPost.Value / bunPre.Value);
                urr = Math.Round((decimal)((1 - R) * 100), 1);
                // Daugirdas single-pool Kt/V (t=3.5h, UF/W ≈ 0.55/70 for average HD patient)
                var t = 3.5;
                var inner = R - 0.008 * t;
                if (inner > 0)
                {
                    var ktvRaw = -Math.Log(inner) + (4 - 3.5 * R) * 0.55 / 70;
                    ktv = Math.Round((decimal)ktvRaw, 2);
                }
            }

            rows.Add(new UrRDto(
                PatientName:    s.Patient.Name,
                LisPatientId:   s.Patient.LisPatientId,
                Date:           reportDate.ToString("yyyy-MM-dd"),
                AccessionPre:   bunPreVal?.ResultHeader.Order?.AccessionNumber,
                AccessionPost:  bunPostVal?.ResultHeader.Order?.AccessionNumber,
                BunPre:         bunPre,
                BunPost:        bunPost,
                Urr:            urr,
                KtV:            ktv
            ));
        }

        return Ok(rows);
    }

    // POST /api/export — CSV export (reads from CDM chain)
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] ExportRequest req)
    {
        if (!IsChargeNurse && !IsClinicAdmin && !IsPlAdmin) return Forbid();

        var resolvedClient = ClientId;
        if (!resolvedClient.HasValue)
        {
            var dbUser = await _db.Users.FindAsync(CurrentUserId);
            resolvedClient = dbUser?.ClientId;
        }
        if (!resolvedClient.HasValue) return BadRequest("Client context required");

        // Load orders for requested patients within date range
        var orders = await _db.Orders
            .Where(o => o.TenantId == TenantId
                     && req.PatientIds.Contains(o.PatientId)
                     && o.ClientId == resolvedClient.Value)
            .ToListAsync();

        var orderMap = orders.ToDictionary(o => o.Id);
        var orderIds = orders.Select(o => o.Id).ToList();
        if (orderIds.Count == 0)
            return req.Format == "csv"
                ? File(System.Text.Encoding.UTF8.GetBytes("PatientId,Analyte,Value,Unit,Flag,Date,Accession\n"), "text/csv", "dx7_export_empty.csv")
                : Ok(Array.Empty<object>());

        var headers = await _db.ResultHeaders
            .Include(h => h.SxaTest)
            .Where(h => h.TenantId == TenantId
                     && orderIds.Contains(h.OrderId)
                     && h.ResultDatetime >= req.FromDate.ToDateTime(TimeOnly.MinValue)
                     && h.ResultDatetime <= req.ToDate.ToDateTime(TimeOnly.MaxValue))
            .ToListAsync();
        foreach (var h in headers)
            if (orderMap.TryGetValue(h.OrderId, out var o)) h.Order = o;

        var headerMap = headers.ToDictionary(h => h.Id);
        var headerIds = headers.Select(h => h.Id).ToList();
        if (headerIds.Count == 0)
            return req.Format == "csv"
                ? File(System.Text.Encoding.UTF8.GetBytes("PatientId,Analyte,Value,Unit,Flag,Date,Accession\n"), "text/csv", "dx7_export_empty.csv")
                : Ok(Array.Empty<object>());

        var values = await _db.ResultValues
            .Include(v => v.Analyte)
            .Where(v => v.TenantId == TenantId && headerIds.Contains(v.ResultHeaderId))
            .ToListAsync();
        foreach (var v in values)
            if (headerMap.TryGetValue(v.ResultHeaderId, out var h)) v.ResultHeader = h;

        if (req.TestCodes?.Count > 0)
            values = values.Where(v => req.TestCodes.Contains(v.AnalyteCode ?? "")).ToList();

        // Load patient names
        var patientIds  = orders.Select(o => o.PatientId).Distinct().ToList();
        var patientMap  = await _db.Patients
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p);

        if (req.Format == "csv")
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("PatientName,LisPatientId,AnalyteCode,AnalyteName,DisplayValue,Unit,ReferenceRange,AbnormalFlag,ResultDate,Accession");
            foreach (var v in values.OrderBy(v => v.ResultHeader.Order?.PatientId).ThenBy(v => v.AnalyteCode).ThenByDescending(v => v.ResultHeader.ResultDatetime))
            {
                var pat  = v.ResultHeader.Order != null && patientMap.TryGetValue(v.ResultHeader.Order.PatientId, out var p) ? p : null;
                var date = v.ResultHeader.ResultDatetime.HasValue ? DateOnly.FromDateTime(v.ResultHeader.ResultDatetime.Value).ToString("yyyy-MM-dd") : "";
                csv.AppendLine($"{pat?.Name ?? ""},{pat?.LisPatientId ?? ""},{v.AnalyteCode},{v.Analyte?.DisplayName ?? ""},{v.DisplayValue},{v.Unit},{v.ReferenceRangeRaw},{v.AbnormalFlag},{date},{v.ResultHeader.Order?.AccessionNumber ?? ""}");
            }
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"dx7_export_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(values.Select(v =>
        {
            var date = v.ResultHeader.ResultDatetime.HasValue ? DateOnly.FromDateTime(v.ResultHeader.ResultDatetime.Value) : today;
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
                DaysSinceResult: today.DayNumber - date.DayNumber,
                ResultStatus:    "final",
                AccessionId:     v.ResultHeader.Order?.AccessionNumber,
                AnalyteCode:     v.AnalyteCode,
                SxaTestId:       v.ResultHeader.SxaTestId,
                SxaTestName:     v.ResultHeader.SxaTest?.CanonicalName,
                ResultHeaderId:  v.ResultHeaderId,
                OrderId:         v.ResultHeader.OrderId,
                Hl7MessageId:    v.ResultHeader.SourceHl7MessageId
            );
        }));
    }
}