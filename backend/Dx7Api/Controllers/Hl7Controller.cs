using Dx7Api.Data;
using Dx7Api.Services.Hl7;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Controllers;

[ApiController]
[Route("api/hl7")]
public class Hl7Controller : TenantBaseController
{
    private readonly AppDbContext _db;
    private readonly Hl7Processor _processor;
    private readonly IConfiguration _config;

    public Hl7Controller(AppDbContext db, Hl7Processor processor, IConfiguration config)
    {
        _db = db;
        _processor = processor;
        _config = config;
    }

    /// <summary>
    /// Upload one or more HL7 files manually via HTTP (multipart/form-data).
    /// Useful for testing or for clinics without folder access.
    /// </summary>
    [HttpPost("upload")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> Upload(List<IFormFile> files)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        if (files == null || files.Count == 0)
            return BadRequest(new { message = "No files provided" });

        var results = new List<object>();

        foreach (var file in files)
        {
            if (!file.FileName.EndsWith(".hl7", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new { file = file.FileName, status = "skipped", notes = "Not an HL7 file" });
                continue;
            }

            string content;
            using (var reader = new StreamReader(file.OpenReadStream()))
                content = await reader.ReadToEndAsync();

            try
            {
                var msg    = Hl7Parser.Parse(content);
                var result = await _processor.ProcessAsync(msg, TenantId);
                results.Add(new {
                    file   = file.FileName,
                    status = result.Status,
                    notes  = result.Notes,
                    patient = result.PatientName,
                    accession = result.AccessionId,
                    saved  = result.ResultsSaved
                });

                // Also save to inbox processed folder for audit trail
                SaveToInbox(content, file.FileName, result.Status == "error" ? "error" : "processed");
            }
            catch (Exception ex)
            {
                results.Add(new { file = file.FileName, status = "error", notes = ex.Message });
            }
        }

        return Ok(new { processed = results.Count, results });
    }

    /// <summary>
    /// Upload a raw HL7 message body as text/plain.
    /// </summary>
    [HttpPost("message")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> PostMessage()
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        string content;
        using (var reader = new StreamReader(Request.Body))
            content = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { message = "Empty message body" });

        var msg    = Hl7Parser.Parse(content);
        var result = await _processor.ProcessAsync(msg, TenantId);

        return result.Status == "error"
            ? BadRequest(result)
            : Ok(result);
    }

    /// <summary>
    /// Get recent HL7 processing log entries.
    /// </summary>
    [HttpGet("log")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult GetLog([FromQuery] int lines = 100)
    {
        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
        if (!Directory.Exists(inboxRoot))
            return Ok(new { entries = Array.Empty<object>(), message = "Inbox folder not found" });

        // Collect log lines from all tenant subfolders + root
        var logFiles = Directory.GetFiles(inboxRoot, "dx7_hl7.log", SearchOption.AllDirectories).ToList();
        var rootLog  = Path.Combine(inboxRoot, "dx7_hl7.log");
        if (System.IO.File.Exists(rootLog) && !logFiles.Contains(rootLog))
            logFiles.Add(rootLog);

        if (logFiles.Count == 0)
            return Ok(new { entries = Array.Empty<object>(), message = "No log file yet. Drop .hl7 files into the inbox folder." });

        var allLines = logFiles
            .SelectMany(f => System.IO.File.ReadAllLines(f))
            .OrderByDescending(l => l) // timestamp is at start, descending = newest first
            .ToList();

        var recent = allLines.Take(lines).ToList();

        var entries = recent.Select(l =>
        {
            var parts = l.Split('|');
            return new {
                timestamp  = parts.Length > 0 ? parts[0].Trim() : "",
                status     = parts.Length > 1 ? parts[1].Trim() : "",
                msgType    = parts.Length > 2 ? parts[2].Trim() : "",
                patient    = parts.Length > 3 ? parts[3].Trim() : "",
                accession  = parts.Length > 4 ? parts[4].Trim() : "",
                saved      = parts.Length > 5 ? parts[5].Trim() : "",
                file       = parts.Length > 6 ? parts[6].Trim() : "",
                notes      = parts.Length > 7 ? parts[7].Trim() : "",
                raw        = l
            };
        }).ToList();

        return Ok(new { total = allLines.Count, showing = entries.Count, entries });
    }

    /// <summary>
    /// Get inbox folder stats — how many files pending, processed, errored.
    /// </summary>
    [HttpGet("inbox/status")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult GetInboxStatus()
    {
        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");

        if (!Directory.Exists(inboxRoot))
            return Ok(new { inboxPath = inboxRoot, exists = false });

        var pending   = Directory.GetFiles(inboxRoot, "*.hl7", SearchOption.AllDirectories)
            .Count(f2 => !f2.Contains("processed") && !f2.Contains("error"));
        var processed = Directory.GetFiles(inboxRoot, "*.hl7", SearchOption.AllDirectories)
            .Count(f2 => f2.Contains("processed"));
        var errored   = Directory.GetFiles(inboxRoot, "*.hl7", SearchOption.AllDirectories)
            .Count(f2 => f2.Contains("error"));

        return Ok(new {
            inboxPath = inboxRoot,
            exists    = true,
            pending,
            processed,
            errored,
            tenantFolders = Directory.GetDirectories(inboxRoot).Select(Path.GetFileName).ToList()
        });
    }

    private void SaveToInbox(string content, string fileName, string subfolder)
    {
        try
        {
            var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
            var dest      = Path.Combine(inboxRoot, subfolder);
            Directory.CreateDirectory(dest);
            var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dest, $"{ts}_{fileName}");
            System.IO.File.WriteAllText(path, content);
        }
        catch { /* audit save is non-fatal */ }
    }
}