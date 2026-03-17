using Dx7Api.Data;
using Dx7Api.DTOs;
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
                var msg    = Hl7Parser.Parse(content, await GetSegmentIdsAsync());
                var result = await _processor.ProcessAsync(msg, TenantId, content);
                results.Add(new {
                    file   = file.FileName,
                    status = result.Status,
                    notes  = result.Notes,
                    patient = result.PatientName,
                    accession = result.AccessionId,
                    saved  = result.ResultsSaved
                });

                // Also save to inbox processed folder for audit trail
                var prefix   = result.Status == "duplicate" ? "dup_" : "";
                SaveToInbox(content, prefix + file.FileName, (result.Status == "error" || result.Status == "duplicate") ? "error" : "processed");
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

        var msg    = Hl7Parser.Parse(content, await GetSegmentIdsAsync());
        var result = await _processor.ProcessAsync(msg, TenantId, content);

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


    /// <summary>
    /// Delete a specific log entry by its 0-based index (from the full sorted list).
    /// </summary>
    [HttpDelete("log/{index:int}")]
    public IActionResult DeleteLogEntry(int index)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
        if (!Directory.Exists(inboxRoot))
            return NotFound(new { message = "Inbox folder not found" });

        var logFiles = Directory.GetFiles(inboxRoot, "dx7_hl7.log", SearchOption.AllDirectories).ToList();
        if (logFiles.Count == 0) return NotFound(new { message = "No log file found" });

        // Collect all lines sorted descending (same order as GetLog)
        var allLines = logFiles
            .SelectMany(f => System.IO.File.ReadAllLines(f))
            .OrderByDescending(l => l)
            .ToList();

        if (index < 0 || index >= allLines.Count)
            return BadRequest(new { message = "Index out of range" });

        var lineToRemove = allLines[index];

        // Remove from whichever log file contains it
        foreach (var logFile in logFiles)
        {
            var lines = System.IO.File.ReadAllLines(logFile).ToList();
            var idx = lines.LastIndexOf(lineToRemove);
            if (idx >= 0)
            {
                lines.RemoveAt(idx);
                System.IO.File.WriteAllLines(logFile, lines);
                break;
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Clear all log entries.
    /// </summary>
    [HttpDelete("log")]
    public IActionResult ClearLog()
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();

        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
        if (!Directory.Exists(inboxRoot)) return NoContent();

        foreach (var logFile in Directory.GetFiles(inboxRoot, "dx7_hl7.log", SearchOption.AllDirectories))
            System.IO.File.WriteAllText(logFile, "");

        return NoContent();
    }


    /// <summary>
    /// List quarantined (errored) HL7 files available for review/reprocessing.
    /// </summary>
    [HttpGet("quarantine")]
    public IActionResult GetQuarantine()
    {
        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
        if (!Directory.Exists(inboxRoot))
            return Ok(new { files = Array.Empty<object>() });

        var errorFiles = Directory.GetFiles(inboxRoot, "*.hl7", SearchOption.AllDirectories)
            .Where(f => f.Contains("error"))
            .Select(f =>
            {
                // Try to detect reason from log — fallback to "error"
                var fn = Path.GetFileName(f);
                var reason = fn.StartsWith("dup_") ? "duplicate" : "error";
                return new {
                    path     = f,
                    fileName = fn,
                    reason,
                    size     = new FileInfo(f).Length,
                    modified = new FileInfo(f).LastWriteTimeUtc
                };
            })
            .OrderByDescending(f => f.modified)
            .ToList();

        return Ok(new { files = errorFiles });
    }

    /// <summary>
    /// Permanently delete a quarantined file by path.
    /// Only PL Admin or Clinic Admin may delete quarantine files.
    /// </summary>
    [HttpDelete("quarantine")]
    public IActionResult DeleteQuarantineFile([FromQuery] string path)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        if (string.IsNullOrEmpty(path))
            return BadRequest(new { message = "path is required" });

        // Safety: path must be inside the configured inbox root
        var inboxRoot = Path.GetFullPath(_config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox"));
        var fullPath  = Path.GetFullPath(path);
        if (!fullPath.StartsWith(inboxRoot))
            return Forbid();

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "File not found" });

        System.IO.File.Delete(fullPath);
        return NoContent();
    }

    /// <summary>
    /// Reprocess a quarantined file by path.
    /// For duplicates: moves to processed without re-saving results.
    /// For errors: re-runs through the processor.
    /// </summary>
    [HttpPost("quarantine/reprocess")]
    public async Task<IActionResult> ReprocessQuarantine([FromBody] ReprocessRequest req)
    {
        if (!IsPlAdmin && !IsClinicAdmin) return Forbid();
        if (string.IsNullOrEmpty(req.Path) || !System.IO.File.Exists(req.Path))
            return NotFound(new { message = "File not found" });

        var fileName = Path.GetFileName(req.Path);
        var isDuplicate = fileName.StartsWith("dup_");

        Dx7Api.Services.Hl7.Hl7ProcessResult result;

        if (isDuplicate)
        {
            // Duplicate — user has reviewed and confirmed; move to processed, do NOT re-save
            result = new Dx7Api.Services.Hl7.Hl7ProcessResult
            {
                Status = "processed",
                Notes  = "Duplicate reviewed and acknowledged by user — moved to processed without re-saving."
            };
        }
        else
        {
            // Error — re-run through processor
            var content = await System.IO.File.ReadAllTextAsync(req.Path);
            var msg = Dx7Api.Services.Hl7.Hl7Parser.Parse(content, await GetSegmentIdsAsync());
            result = await _processor.ProcessAsync(msg, TenantId, content);
        }

        if (result.Status != "error")
        {
            var processed = Path.Combine(
                Path.GetDirectoryName(req.Path)!.Replace("error", "processed"),
                fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(processed)!);
            System.IO.File.Move(req.Path, processed, overwrite: true);
        }

        return Ok(result);
    }

    /// <summary>
    /// Read raw content + parsed fields of a quarantined file for review.
    /// </summary>
    [HttpGet("quarantine/read")]
    public async Task<IActionResult> ReadQuarantineFile([FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound(new { message = "File not found" });

        var inboxRoot = Path.GetFullPath(_config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox"));
        var fullPath  = Path.GetFullPath(path);
        if (!fullPath.StartsWith(inboxRoot))
            return Forbid();

        var raw    = await System.IO.File.ReadAllTextAsync(fullPath);
        var msg    = Dx7Api.Services.Hl7.Hl7Parser.Parse(raw, await GetSegmentIdsAsync());
        var display = FormatHl7Display(raw);
        var fn     = Path.GetFileName(fullPath);
        var reason = fn.StartsWith("dup_") ? "duplicate" : "error";

        // Look up the quarantine reason from the DB using MSH-10 MessageControlId
        string? quarantineReason = null;
        if (!string.IsNullOrEmpty(msg.MessageId))
        {
            var archived = await _db.Hl7Messages.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == TenantId && m.MessageControlId == msg.MessageId);
            quarantineReason = archived?.QuarantineReason;
        }

        // Identify unmapped OBR-4 and OBX-3 codes by checking tenant maps
        var unmappedTestCodes    = new List<string>();
        var unmappedAnalyteCodes = new List<string>();

        if (!string.IsNullOrEmpty(msg.TestCode))
        {
            var testMapped = await _db.TenantTestMaps.AnyAsync(m =>
                m.TenantId == TenantId && m.TenantTestCode == msg.TestCode && m.IsActive);
            if (!testMapped) unmappedTestCodes.Add(msg.TestCode);
        }

        foreach (var obs in msg.Observations)
        {
            var obxCode = !string.IsNullOrEmpty(obs.TestCode) ? obs.TestCode : msg.TestCode;
            if (string.IsNullOrEmpty(obxCode)) continue;
            // Skip section-header OBX rows with empty values — same as processor
            if (string.IsNullOrWhiteSpace(obs.ResultValue.Trim().Trim('"'))) continue;
            var alreadyChecked = unmappedAnalyteCodes.Contains(obxCode);
            if (alreadyChecked) continue;
            var mapped = await _db.TenantAnalyteMaps.AnyAsync(m =>
                m.TenantId == TenantId && m.TenantAnalyteCode == obxCode && m.IsActive);
            if (!mapped) unmappedAnalyteCodes.Add(obxCode);
        }

        var parsed = new
        {
            messageType      = msg.MessageType,
            messageId        = msg.MessageId,
            sendingFacility  = msg.SendingFacility,
            patientName      = msg.PatientName,
            patientId        = msg.PatientId,
            birthdate        = msg.PatientDob,
            gender           = msg.PatientGender,
            accessionId      = msg.AccessionId,
            testCode         = msg.TestCode,
            testName         = msg.TestName,
            observationCount = msg.Observations.Count,
            observations     = msg.Observations.Select(o => new {
                o.TestCode, o.TestName, o.ResultValue, o.ResultUnit, o.ReferenceRange, o.AbnormalFlag, o.ResultStatus
            }).ToList()
        };

        return Ok(new {
            raw    = display,
            parsed,
            reason,
            fileName          = fn,
            quarantineReason,
            unmappedTestCodes,
            unmappedAnalyteCodes
        });
    }


    /// <summary>
    /// Read raw content + parsed fields of a log entry file.
    /// Searches processed/ and error/ subfolders by original filename.
    /// </summary>
    [HttpGet("log/read")]
    public async Task<IActionResult> ReadLogFile([FromQuery] string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return BadRequest(new { message = "fileName is required" });

        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
        if (!Directory.Exists(inboxRoot))
            return NotFound(new { message = "Inbox not configured" });

        // Strip "File: " prefix written by WriteLog
        var baseName = fileName.StartsWith("File: ") ? fileName[6..] : fileName;

        // Search all subfolders for a file ending with _{baseName} or exactly baseName
        var candidates = Directory.GetFiles(inboxRoot, "*.hl7", SearchOption.AllDirectories)
            .Where(f => {
                var fn = Path.GetFileName(f);
                return fn == baseName || fn.EndsWith("_" + baseName);
            })
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
            return NotFound(new { message = $"File '{baseName}' not found in inbox archive." });

        var filePath = candidates[0];
        var raw      = await System.IO.File.ReadAllTextAsync(filePath);
        var msg      = Dx7Api.Services.Hl7.Hl7Parser.Parse(raw, await GetSegmentIdsAsync());
        var display  = FormatHl7Display(raw);
        var fn2      = Path.GetFileName(filePath);
        var folder   = Path.GetFileName(Path.GetDirectoryName(filePath)!);

        // Look up the quarantine reason from the DB using MSH-10 MessageControlId
        string? quarantineReason = null;
        var unmappedTestCodes    = new List<string>();
        var unmappedAnalyteCodes = new List<string>();

        if (!string.IsNullOrEmpty(msg.MessageId))
        {
            var archived = await _db.Hl7Messages.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == TenantId && m.MessageControlId == msg.MessageId);
            quarantineReason = archived?.QuarantineReason;
        }

        // Check unmapped OBR-4
        if (!string.IsNullOrEmpty(msg.TestCode))
        {
            var testMapped = await _db.TenantTestMaps.AnyAsync(m =>
                m.TenantId == TenantId && m.TenantTestCode == msg.TestCode && m.IsActive);
            if (!testMapped) unmappedTestCodes.Add(msg.TestCode);
        }

        // Check unmapped OBX-3
        foreach (var obs in msg.Observations)
        {
            var obxCode = !string.IsNullOrEmpty(obs.TestCode) ? obs.TestCode : msg.TestCode;
            if (string.IsNullOrEmpty(obxCode)) continue;
            // Skip section-header OBX rows with empty values — same as processor
            if (string.IsNullOrWhiteSpace(obs.ResultValue.Trim().Trim('"'))) continue;
            if (unmappedAnalyteCodes.Contains(obxCode)) continue;
            var mapped = await _db.TenantAnalyteMaps.AnyAsync(m =>
                m.TenantId == TenantId && m.TenantAnalyteCode == obxCode && m.IsActive);
            if (!mapped) unmappedAnalyteCodes.Add(obxCode);
        }

        var parsed = new
        {
            messageType      = msg.MessageType,
            messageId        = msg.MessageId,
            sendingFacility  = msg.SendingFacility,
            patientName      = msg.PatientName,
            patientId        = msg.PatientId,
            birthdate        = msg.PatientDob,
            gender           = msg.PatientGender,
            accessionId      = msg.AccessionId,
            testCode         = msg.TestCode,
            testName         = msg.TestName,
            observationCount = msg.Observations.Count,
            observations     = msg.Observations.Select(o => new {
                o.TestCode, o.TestName,
                value         = o.ResultValue,
                units         = o.ResultUnit,
                o.ReferenceRange, o.AbnormalFlag, o.ResultStatus
            }).ToList()
        };

        return Ok(new { raw = display, parsed, folder, fileName = fn2, originalName = baseName,
            quarantineReason, unmappedTestCodes, unmappedAnalyteCodes });
    }


    /// <summary>
    /// Formats raw HL7 for display — ensures each segment starts on a new line.
    /// Handles files where segments are separated by \r, \n, or no separator at all.
    /// </summary>
    private string[]? _segmentIdsCache;
    private async Task<string[]> GetSegmentIdsAsync()
    {
        if (_segmentIdsCache != null) return _segmentIdsCache;
        _segmentIdsCache = await _db.RefData.AsNoTracking()
            .Where(r => r.Category == "Hl7SegmentId" && r.IsActive)
            .OrderBy(r => r.SortOrder)
            .Select(r => r.Code)
            .ToArrayAsync();
        return _segmentIdsCache;
    }

    private static string FormatHl7Display(string raw)
    {
        // Normalize existing line endings first
        var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

        // If already has newlines between segments, just return normalized
        if (normalized.Contains("\n"))
            return normalized;

        // No newlines — inject a newline before each known segment identifier
        var segmentIds = new[] { "MSH", "PID", "PV1", "PV2", "ORC", "OBR", "OBX", "NTE", "SPM", "SAC", "IN1", "GT1", "AL1", "DG1", "FT1" };
        var result = normalized;
        foreach (var seg in segmentIds)
        {
            // Insert newline before each segment (but not at the very start)
            result = System.Text.RegularExpressions.Regex.Replace(result, $@"(?<!^)(?={seg}\|)", "\n");
        }
        return result;
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