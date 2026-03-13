using Dx7Api.Data;
using Dx7Api.DTOs;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
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

    // POST /api/export/session-pdf?sessionId=xxx
    [HttpGet("session-pdf")]
    public async Task<IActionResult> SessionPdf(
        [FromQuery] Guid sessionId,
        [FromQuery] bool priority = true,
        [FromQuery] bool allResults = true,
        [FromQuery] bool notes = true)
    {
        try
        {
        // Resolve session
        var session = await _db.Sessions
            .Include(s => s.Patient)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == TenantId);
        if (session == null) return NotFound("Session not found");

        // Resolve clinic name
        var client = await _db.Clients.FindAsync(session.ClientId);
        var clinicName = client?.Name ?? "Dx7 Clinic";

        // Get results
        var results = await _db.Results
            .Where(r => r.TenantId == TenantId && r.PatientId == session.PatientId)
            .OrderBy(r => r.TestCode)
            .ThenByDescending(r => r.ResultDate)
            .ToListAsync();

        // Get latest result per test code
        var current = results
            .GroupBy(r => r.TestCode)
            .Select(g => g.First())
            .OrderBy(r => r.TestCode)
            .ToList();

        // Get MD notes
        var mdNotes = await _db.MdNotes
            .Include(n => n.MdUser)
            .Where(n => n.SessionId == sessionId && n.TenantId == TenantId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        // Build PDF
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var pdf = new PdfDocument(writer);
        var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
        doc.SetMargins(36, 36, 36, 36);

        var fontBold   = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
        var fontNormal = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var navy  = new DeviceRgb(30, 58, 138);
        var blue  = new DeviceRgb(37, 99, 235);
        var gray  = new DeviceRgb(107, 114, 128);
        var light = new DeviceRgb(239, 246, 255);
        var red   = new DeviceRgb(220, 38, 38);
        var green = new DeviceRgb(22, 163, 74);

        // ── Header ──────────────────────────────────────────────────────────
        var header = new Table(UnitValue.CreatePercentArray(new float[]{ 1, 2 }))
            .UseAllAvailableWidth().SetMarginBottom(10);
        var logoCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER)
            .Add(new Paragraph("Dx7")
                .SetFont(fontBold).SetFontSize(28).SetFontColor(navy));
        var infoCell = new Cell().SetBorder(iText.Layout.Borders.Border.NO_BORDER)
            .SetTextAlignment(TextAlignment.RIGHT)
            .Add(new Paragraph(clinicName).SetFont(fontBold).SetFontSize(11).SetFontColor(navy))
            .Add(new Paragraph("Lab Results Report · " + DateTime.Now.ToString("MMMM dd, yyyy hh:mm tt"))
                .SetFont(fontNormal).SetFontSize(9).SetFontColor(gray));
        header.AddCell(logoCell).AddCell(infoCell);
        doc.Add(header);

        // Divider
        doc.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(1.5f))
            .SetStrokeColor(blue).SetMarginBottom(10));

        // ── Patient bar ──────────────────────────────────────────────────────
        var patBar = new Table(UnitValue.CreatePercentArray(new float[]{ 3, 2, 2, 2 }))
            .UseAllAvailableWidth().SetMarginBottom(14)
            .SetBackgroundColor(light);
        void AddPatCell(string label, string val) {
            patBar.AddCell(new Cell()
                .SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetPadding(8)
                .Add(new Paragraph(label).SetFont(fontBold).SetFontSize(8).SetFontColor(gray))
                .Add(new Paragraph(val).SetFont(fontBold).SetFontSize(11).SetFontColor(navy)));
        }
        AddPatCell("PATIENT", session.Patient.Name);
        AddPatCell("DATE", session.SessionDate.ToString("MMMM dd, yyyy"));
        AddPatCell("SHIFT", "Shift " + session.ShiftNumber + (session.Chair != null ? " · Chair " + session.Chair : ""));
        AddPatCell("LIS ID", session.Patient.LisPatientId ?? "—");
        doc.Add(patBar);

        // ── Priority Labs ────────────────────────────────────────────────────
        if (priority) {
            var priorityCodes = new[] { "K", "PHOS", "HGB" };
            var priorityResults = priorityCodes
                .Select(code => current.FirstOrDefault(r => r.TestCode == code))
                .ToList();

            if (priorityResults.Any(r => r != null)) {
                doc.Add(new Paragraph("PRIORITY LABS")
                    .SetFont(fontBold).SetFontSize(10).SetFontColor(navy)
                    .SetBorderBottom(new iText.Layout.Borders.SolidBorder(blue, 1))
                    .SetMarginBottom(8));

                var prioTable = new Table(UnitValue.CreatePercentArray(new float[]{ 1, 1, 1 }))
                    .UseAllAvailableWidth().SetMarginBottom(14);
                foreach (var r in priorityResults) {
                    var cell = new Cell()
                        .SetBorder(new iText.Layout.Borders.SolidBorder(new DeviceRgb(229,231,235), 1))
                        .SetPadding(10).SetTextAlignment(TextAlignment.CENTER);
                    if (r == null) {
                        cell.Add(new Paragraph("—").SetFont(fontNormal).SetFontSize(20).SetFontColor(gray));
                        cell.Add(new Paragraph("No result").SetFont(fontNormal).SetFontSize(9).SetFontColor(gray));
                    } else {
                        cell.Add(new Paragraph(r.TestCode).SetFont(fontBold).SetFontSize(9).SetFontColor(gray));
                        var valColor = r.AbnormalFlag?.StartsWith("H") == true ? red :
                                       r.AbnormalFlag?.StartsWith("L") == true ? new DeviceRgb(37,99,235) : navy;
                        cell.Add(new Paragraph(r.ResultValue ?? "—").SetFont(fontBold).SetFontSize(24).SetFontColor(valColor));
                        cell.Add(new Paragraph(r.ResultUnit ?? "").SetFont(fontNormal).SetFontSize(9).SetFontColor(gray));
                        if (!string.IsNullOrEmpty(r.AbnormalFlag))
                            cell.Add(new Paragraph("Flag: " + r.AbnormalFlag).SetFont(fontBold).SetFontSize(8).SetFontColor(valColor));
                        if (!string.IsNullOrEmpty(r.ReferenceRange))
                            cell.Add(new Paragraph("Ref: " + r.ReferenceRange).SetFont(fontNormal).SetFontSize(8).SetFontColor(gray));
                    }
                    prioTable.AddCell(cell);
                }
                doc.Add(prioTable);
            }
        }

        // ── All Results ──────────────────────────────────────────────────────
        if (allResults && current.Any()) {
            doc.Add(new Paragraph("ALL LAB RESULTS")
                .SetFont(fontBold).SetFontSize(10).SetFontColor(navy)
                .SetBorderBottom(new iText.Layout.Borders.SolidBorder(blue, 1))
                .SetMarginBottom(8));

            var tbl = new Table(UnitValue.CreatePercentArray(new float[]{ 2.5f, 1.5f, 1, 1, 2.5f, 1.5f, 1.5f }))
                .UseAllAvailableWidth().SetMarginBottom(14).SetFontSize(9);

            void AddTh(string t) => tbl.AddHeaderCell(new Cell()
                .SetBackgroundColor(new DeviceRgb(249,250,251))
                .SetFont(fontBold).SetFontColor(gray).SetFontSize(8).SetPadding(6)
                .Add(new Paragraph(t)));

            AddTh("Test"); AddTh("Value"); AddTh("Unit"); AddTh("Flag"); AddTh("Reference"); AddTh("Date"); AddTh("Source");

            foreach (var r in current) {
                var flagColor = r.AbnormalFlag?.StartsWith("H") == true ? red :
                                r.AbnormalFlag?.StartsWith("L") == true ? new DeviceRgb(37,99,235) : navy;
                var rowBg = string.IsNullOrEmpty(r.AbnormalFlag) ? null : new DeviceRgb(255,247,247);

                void AddTd(string t, bool bold = false, DeviceRgb? color = null) {
                    var cell = new Cell().SetPadding(6)
                        .SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(243,244,246), 1));
                    if (rowBg != null) cell.SetBackgroundColor(rowBg);
                    var p = new Paragraph(t ?? "—").SetFont(bold ? fontBold : fontNormal);
                    if (color != null) p.SetFontColor(color);
                    cell.Add(p);
                    tbl.AddCell(cell);
                }

                AddTd(r.TestName ?? r.TestCode, true, navy);
                AddTd(r.ResultValue ?? "—", true, flagColor);
                AddTd(r.ResultUnit ?? "—");
                AddTd(r.AbnormalFlag ?? "—", true, string.IsNullOrEmpty(r.AbnormalFlag) ? gray : flagColor);
                AddTd(r.ReferenceRange ?? "—");
                AddTd(r.ResultDate.ToString("MM/dd/yyyy"));
                AddTd(r.SourceLab ?? "—");
            }
            doc.Add(tbl);
        }

        // ── MD Notes ─────────────────────────────────────────────────────────
        if (notes && mdNotes.Any()) {
            doc.Add(new Paragraph("MD NOTES")
                .SetFont(fontBold).SetFontSize(10).SetFontColor(navy)
                .SetBorderBottom(new iText.Layout.Borders.SolidBorder(blue, 1))
                .SetMarginBottom(8));

            foreach (var n in mdNotes) {
                var noteBox = new Cell()
                    .SetBorder(new iText.Layout.Borders.SolidBorder(new DeviceRgb(229,231,235), 1))
                    .SetPadding(10).SetMarginBottom(8);
                noteBox.Add(new Paragraph(n.MdUser?.Name + " · " + n.CreatedAt.ToString("MMM dd, yyyy hh:mm tt"))
                    .SetFont(fontBold).SetFontSize(9).SetFontColor(gray));
                noteBox.Add(new Paragraph(n.NoteText)
                    .SetFont(fontNormal).SetFontSize(10).SetFontColor(new DeviceRgb(55,65,81)));
                doc.Add(new Table(1).UseAllAvailableWidth().AddCell(noteBox).SetMarginBottom(8));
            }
        }

        // ── Footer ───────────────────────────────────────────────────────────
        doc.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(0.5f))
            .SetStrokeColor(gray).SetMarginTop(10));
        doc.Add(new Paragraph("Dx7 Clinical Information System · Results shown as-is from laboratory source. No interpretation. Data only.")
            .SetFont(fontNormal).SetFontSize(8).SetFontColor(gray).SetTextAlignment(TextAlignment.CENTER));

        doc.Close();

        var filename = $"DX7_Results_{session.Patient.Name.Replace(",","").Replace(" ","_")}_{session.SessionDate:yyyyMMdd}.pdf";
        return File(ms.ToArray(), "application/pdf", filename);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    // POST /api/export (CSV + JSON — existing)
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] ExportRequest req)
    {
        if (!IsChargeNurse && !IsClinicAdmin && !IsPlAdmin)
            return Forbid();

        var resolvedClient = ClientId;
        if (!resolvedClient.HasValue) {
            var dbUser = await _db.Users.FindAsync(CurrentUserId);
            resolvedClient = dbUser?.ClientId;
        }
        if (!resolvedClient.HasValue) return BadRequest("Client context required");

        var query = _db.Results
            .Include(r => r.Patient)
            .Where(r => r.TenantId == TenantId
                     && r.Patient.ClientId == resolvedClient.Value
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
                csv.AppendLine($"{r.Patient.Name},{r.Patient.LisPatientId},{r.TestCode},{r.TestName},{r.ResultValue},{r.ResultUnit},{r.ReferenceRange},{r.AbnormalFlag},{r.ResultDate},{r.SourceLab}");
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