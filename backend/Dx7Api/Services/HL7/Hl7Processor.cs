using Dx7Api.Data;
using Dx7Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Services.Hl7;

/// <summary>
/// Processes HL7 messages per Dx7 CDM v1.0 §9 traceability chain:
///
///   HL7_Message → LabOrder → ResultHeader → ResultValue
///
/// Pipeline (ORU^R01):
///   1. Archive raw payload → Hl7Message       (§2.1)
///   2. Duplicate check on MSH-10 UNIQUE index  (§2.1)
///   3. Resolve/create patient from PID          (§3.3)
///   4. Create LabOrder from OBR                 (§4.1)
///   5. Map OBR-4 → SXA_Test — quarantine whole message if unmapped (§6.1/6.3)
///   6. Create ResultHeader                      (§4.2)
///   7. For each OBX: map OBX-3 → SXA_Analyte  (§6.2/6.3)
///      - unmapped OBX quarantined individually, others persist
///   8. Create ResultValue per OBX              (§4.3)
/// </summary>
public class Hl7Processor(AppDbContext db, ILogger<Hl7Processor> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<Hl7Processor> _logger = logger;

    private static readonly string[] DiscardKeywords =
        ["COMPLETED", "PENDING", "DIFF", "DIFFERENTIAL COUNT", "SEE NOTE"];

    public async Task<Hl7ProcessResult> ProcessAsync(Hl7Message msg, Guid tenantId, string rawPayload = "")
    {
        var result = new Hl7ProcessResult
        {
            MessageId   = msg.MessageId,
            MessageType = msg.MessageType,
            PatientId   = msg.PatientId,
            AccessionId = msg.AccessionId
        };

        Models.Hl7Message? hl7Archive = null;
        try
        {
            // ── Step 1: Archive raw HL7 (CDM §2.1) ──────────────────────────
            var existingMsg = await _db.Hl7Messages
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.MessageControlId == msg.MessageId);

            if (existingMsg != null)
            {
                result.Status = "duplicate";
                result.Notes  = $"MSH-10 '{msg.MessageId}' already processed — §2.1 UNIQUE constraint.";
                _logger.LogInformation("HL7 duplicate MSH-10 {MsgId}", msg.MessageId);
                return result;
            }

            hl7Archive = new Models.Hl7Message
            {
                TenantId         = tenantId,
                MessageControlId = msg.MessageId,
                RawPayload       = rawPayload,
                ReceivedAt       = DateTime.UtcNow,
                ProcessedFlag    = false,
                QuarantineFlag   = false
            };
            _db.Hl7Messages.Add(hl7Archive);
            await _db.SaveChangesAsync();

            // ── Step 2: Resolve patient (CDM §3.3) ──────────────────────────
            var (patient, _) = await ResolveOrCreatePatientAsync(msg, tenantId);
            if (patient == null)
            {
                hl7Archive.QuarantineFlag   = true;
                hl7Archive.QuarantineReason = "No patient identity (no PID-3 or PID-5).";
                await _db.SaveChangesAsync();
                result.Status = "error";
                result.Notes  = hl7Archive.QuarantineReason;
                return result;
            }
            result.PatientName = patient.Name;

            var obsDateTime = msg.ObservationDateTime != default
                ? DateTime.SpecifyKind(msg.ObservationDateTime, DateTimeKind.Utc)
                : DateTime.UtcNow;

            // ── ORM^O01 — incoming order, write LabOrder as pending ──────────
            if (msg.IsOrder)
            {
                var orderExists = await _db.Orders.AnyAsync(o =>
                    o.TenantId        == tenantId &&
                    o.PatientId       == patient.Id &&
                    o.AccessionNumber == msg.AccessionId);

                if (!orderExists)
                {
                    _db.Orders.Add(new LabOrder
                    {
                        TenantId           = tenantId,
                        ClientId           = patient.ClientId,
                        PatientId          = patient.Id,
                        AccessionNumber    = msg.AccessionId,
                        SourceHl7MessageId = hl7Archive.Id,
                        ReleasedAt         = null,
                        CreatedAt          = DateTime.UtcNow,
                    });
                    hl7Archive.ProcessedFlag = true;
                    await _db.SaveChangesAsync();
                    result.Status       = "order_saved";
                    result.ResultsSaved = 1;
                    result.Notes        = $"Order created: accession {msg.AccessionId}";
                }
                else
                {
                    hl7Archive.QuarantineFlag   = true;
                    hl7Archive.QuarantineReason = $"Duplicate ORM: accession {msg.AccessionId} already exists.";
                    await _db.SaveChangesAsync();
                    result.Status = "duplicate";
                    result.Notes  = hl7Archive.QuarantineReason;
                }
                return result;
            }

            // ── ORU^R01 — result message ─────────────────────────────────────
            if (msg.IsResult)
            {
                if (msg.Observations.Count == 0)
                {
                    hl7Archive.QuarantineFlag   = true;
                    hl7Archive.QuarantineReason = "ORU has no OBX segments.";
                    await _db.SaveChangesAsync();
                    result.Status = "skipped";
                    result.Notes  = hl7Archive.QuarantineReason;
                    return result;
                }

                // ── Step 3: Map OBR-4 → SXA_Test (CDM §6.1 / §6.3) ─────────
                // CDM §6.3: OBR-4 cannot resolve → quarantine whole message.
                // Do NOT create ResultHeader. No fallback. No auto-guessing.
                var testMap = await ResolveTestMap(msg.TestCode, tenantId);
                if (testMap == null)
                {
                    hl7Archive.QuarantineFlag   = true;
                    hl7Archive.QuarantineReason = $"OBR-4 '{msg.TestCode}' unmapped — no TenantTestMap entry. Add mapping before reprocessing.";
                    await _db.SaveChangesAsync();
                    result.Status = "quarantined";
                    result.Notes  = hl7Archive.QuarantineReason;
                    _logger.LogWarning("HL7 {MsgId}: OBR-4 '{Code}' unmapped — message quarantined per CDM §6.3", msg.MessageId, msg.TestCode);
                    return result;
                }

                // ── Step 4: Create/find LabOrder (CDM §4.1) ─────────────────
                var order = await _db.Orders.FirstOrDefaultAsync(o =>
                    o.TenantId        == tenantId &&
                    o.PatientId       == patient.Id &&
                    o.AccessionNumber == msg.AccessionId);

                if (order == null)
                {
                    order = new LabOrder
                    {
                        TenantId           = tenantId,
                        ClientId           = patient.ClientId,
                        PatientId          = patient.Id,
                        AccessionNumber    = msg.AccessionId,
                        SourceHl7MessageId = hl7Archive.Id,
                        ReleasedAt         = obsDateTime,
                        CreatedAt          = DateTime.UtcNow,
                    };
                    _db.Orders.Add(order);
                    await _db.SaveChangesAsync();
                }
                else if (order.ReleasedAt == null)
                {
                    // Fulfil the pending ORM order
                    order.ReleasedAt = obsDateTime;
                    await _db.SaveChangesAsync();
                }

                // ── Step 5: Create ResultHeader (CDM §4.2) ───────────────────
                var header = new ResultHeader
                {
                    OrderId            = order.Id,
                    TenantId           = tenantId,
                    SourceHl7MessageId = hl7Archive.Id,
                    SxaTestId          = testMap?.SxaTestId,
                    CollectionDatetime = msg.ObservationDateTime != default ? DateTime.SpecifyKind(msg.ObservationDateTime, DateTimeKind.Utc) : null,
                    ResultDatetime     = obsDateTime,
                };
                _db.ResultHeaders.Add(header);
                await _db.SaveChangesAsync();

                // ── Step 5b: Persist NTE lab notes (if any) ──────────────────
                for (int i = 0; i < msg.Notes.Count; i++)
                {
                    _db.LabNotes.Add(new LabNote {
                        TenantId       = tenantId,
                        ResultHeaderId = header.Id,
                        NoteText       = msg.Notes[i],
                        SortOrder      = i
                    });
                }
                if (msg.Notes.Count > 0) await _db.SaveChangesAsync();

                // ── Step 6: Create ResultValue per OBX (CDM §4.3) ───────────
                int saved       = 0;
                int quarantined = 0;

                foreach (var obs in msg.Observations)
                {
                    var obxCode = !string.IsNullOrEmpty(obs.TestCode) ? obs.TestCode : msg.TestCode;

                    // §10.2 row 4 + §10.3.3 — discard blank/quoted-empty structural OBX (e.g. DIFF header)
                    var cleanValue = obs.ResultValue.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(cleanValue)) continue;

                    // §10.3.3 — discard COMPLETED and other placeholder OBX rows
                    if (DiscardKeywords.Any(k => cleanValue.Equals(k, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // §10.2 — classify special value patterns before analyte map lookup
                    bool noSpecimen    = cleanValue == "*";
                    bool notCalculated = cleanValue.Length > 0 && cleanValue.All(c => c == '-');

                    // §10.3.1 — unknown non-numeric: quarantine ENTIRE message, stop processing
                    if (!noSpecimen && !notCalculated)
                    {
                        bool isNumeric = decimal.TryParse(cleanValue,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out _);
                        if (!isNumeric)
                        {
                            hl7Archive.QuarantineFlag   = true;
                            hl7Archive.QuarantineReason = $"OBX-5 unknown non-numeric value '{cleanValue}' in OBX-3 '{obxCode}' — message quarantined per CDM §10.3.1.";
                            await _db.SaveChangesAsync();
                            result.Status = "quarantined";
                            result.Notes  = hl7Archive.QuarantineReason;
                            _logger.LogWarning("HL7 {MsgId}: unknown non-numeric OBX-5 '{Val}' — message quarantined per CDM §10.3.1", msg.MessageId, cleanValue);
                            return result;
                        }
                    }

                    // Map OBX-3 → SXA_Analyte (CDM §6.2)
                    var analyteMap = await ResolveAnalyteMap(obxCode, tenantId);
                    if (analyteMap == null)
                    {
                        // §6.3: quarantine this OBX only, others persist
                        quarantined++;
                        hl7Archive.QuarantineFlag   = true;
                        hl7Archive.QuarantineReason = (hl7Archive.QuarantineReason ?? "") +
                            $" OBX-3 '{obxCode}' unmapped.";
                        _logger.LogWarning("HL7 {MsgId}: OBX-3 '{Code}' unmapped — analyte quarantined", msg.MessageId, obxCode);
                        continue;
                    }

                    // §10.3.2 — for '*' and '---': store null values, set flags; do not omit the row
                    decimal? numeric      = null;
                    string?  displayValue = null;
                    if (!noSpecimen && !notCalculated)
                    {
                        displayValue = cleanValue;
                        decimal.TryParse(cleanValue,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed);
                        numeric = parsed;
                    }

                    (decimal? low, decimal? high) = ParseRefRange(obs.ReferenceRange);

                    _db.ResultValues.Add(new ResultValue
                    {
                        ResultHeaderId     = header.Id,
                        TenantId           = tenantId,
                        AnalyteCode        = analyteMap.AnalyteCode,
                        DisplayValue       = displayValue ?? "",
                        ValueNumeric       = numeric,
                        Unit               = obs.ResultUnit,
                        ReferenceRangeLow  = low,
                        ReferenceRangeHigh = high,
                        ReferenceRangeRaw  = obs.ReferenceRange,
                        AbnormalFlag       = obs.AbnormalFlag is "H" or "L" or "N" ? obs.AbnormalFlag : null,
                        RawHl7Segment      = obs.RawSegment,
                        NoSpecimen         = noSpecimen,
                        NotCalculated      = notCalculated,
                        SchemaVersion      = "DX7_CDM_1.0_A1",
                    });
                    saved++;
                }

                if (quarantined == 0) hl7Archive.ProcessedFlag = true;
                await _db.SaveChangesAsync();

                result.Status       = quarantined > 0 && saved == 0 ? "error" : "processed";
                result.ResultsSaved = saved;
                result.Notes        = $"Saved {saved} ResultValue(s)" +
                                      (quarantined > 0 ? $", quarantined {quarantined} unmapped analyte(s)" : "");
                _logger.LogInformation("HL7 {MsgId}: {Notes}", msg.MessageId, result.Notes);
                return result;
            }

            result.Status = "unknown";
            result.Notes  = $"Unhandled message type: {msg.MessageType}";
            hl7Archive.QuarantineFlag   = true;
            hl7Archive.QuarantineReason = result.Notes;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            result.Status = "error";
            // Surface inner exception — EF wraps DB errors in outer exception
            var inner = ex.InnerException?.InnerException?.Message
                     ?? ex.InnerException?.Message
                     ?? ex.Message;
            result.Notes = inner;
            inner = inner.Length > 497 ? inner[..497] + "..." : inner;
            _logger.LogError(ex, "HL7 {MsgId}: Processing error — {Inner}", msg.MessageId, inner);

            // Mark the archived record quarantined so it doesn't sit as a ghost empty record
            if (hl7Archive != null)
            {
                try
                {
                    _db.ChangeTracker.Clear();
                    var archived = await _db.Hl7Messages.FindAsync(hl7Archive.Id);
                    if (archived != null)
                    {
                        archived.QuarantineFlag   = true;
                        archived.QuarantineReason = inner;
                        await _db.SaveChangesAsync();
                    }
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "HL7 {MsgId}: Failed to mark record quarantined after error", msg.MessageId);
                }
            }
        }

        return result;
    }

    // ── Map lookups ───────────────────────────────────────────────────────────

    private async Task<TenantTestMap?> ResolveTestMap(string obr4Code, Guid tenantId)
    {
        if (string.IsNullOrEmpty(obr4Code)) return null;
        var code = obr4Code.Split('^')[0].Trim();
        return await _db.TenantTestMaps
            .Include(m => m.SxaTest)
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.TenantTestCode == code && m.IsActive);
    }

    private async Task<TenantAnalyteMap?> ResolveAnalyteMap(string obx3Code, Guid tenantId)
    {
        if (string.IsNullOrEmpty(obx3Code)) return null;
        var code = obx3Code.Split('^')[0].Trim();
        return await _db.TenantAnalyteMaps
            .Include(m => m.Analyte)
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.TenantAnalyteCode == code && m.IsActive);
    }

    private static (decimal? low, decimal? high) ParseRefRange(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return (null, null);

        // Handle "< 3.4" format (upper bound only)
        if (raw.TrimStart().StartsWith('<'))
        {
            var part = raw.Replace("<", "").Trim();
            return decimal.TryParse(part, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var hi) ? (null, hi) : (null, null);
        }

        // Handle "X.XX - Y.YY" with spaces, or "X.XX-Y.YY" without
        // Split on " - " first, then fall back to "-"
        string[] parts;
        if (raw.Contains(" - "))
            parts = raw.Split([" - "], StringSplitOptions.None);
        else
            parts = raw.Split('-');

        if (parts.Length == 2 &&
            decimal.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var lo) &&
            decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var hi2))
            return (lo, hi2);

        return (null, null);
    }

    // ── Patient resolution (CDM §3.3) ─────────────────────────────────────────

    private async Task<(Patient? patient, bool wasCreated)> ResolveOrCreatePatientAsync(
        Hl7Message msg, Guid tenantId)
    {
        if (!string.IsNullOrEmpty(msg.PatientId))
        {
            var byId = await _db.Patients.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId && p.LisPatientId == msg.PatientId && p.IsActive);
            if (byId != null) return (byId, false);
        }

        if (!string.IsNullOrEmpty(msg.PatientName))
        {
            var patientNameLower = msg.PatientName.ToLower();
            var byName = await _db.Patients.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.Name.ToLower() == patientNameLower &&
                p.IsActive);
            if (byName != null)
            {
                if (string.IsNullOrEmpty(byName.LisPatientId) && !string.IsNullOrEmpty(msg.PatientId))
                { byName.LisPatientId = msg.PatientId; await _db.SaveChangesAsync(); }
                return (byName, false);
            }
        }

        if (string.IsNullOrEmpty(msg.PatientId) && string.IsNullOrEmpty(msg.PatientName))
            return (null, false);

        DateOnly? dob = null;
        if (!string.IsNullOrEmpty(msg.PatientDob) && msg.PatientDob.Length >= 8)
            if (DateOnly.TryParseExact(msg.PatientDob[..8], "yyyyMMdd", out var parsedDob))
                dob = parsedDob;

        var gender = msg.PatientGender?.ToUpper() switch { "M" => "M", "F" => "F", _ => null };
        var name   = !string.IsNullOrEmpty(msg.PatientName) ? msg.PatientName : msg.PatientId;

        var defaultClient = await _db.Clients
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (defaultClient == null) return (null, false);

        var newPatient = new Patient
        {
            TenantId     = tenantId,
            ClientId     = defaultClient.Id,
            LisPatientId = msg.PatientId,
            Name         = name,
            Birthdate    = dob,
            Gender       = gender,
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow,
        };
        _db.Patients.Add(newPatient);
        try
        {
            await _db.SaveChangesAsync();
            return (newPatient, true);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("23505") == true ||
                  ex.InnerException?.InnerException?.Message.Contains("23505") == true)
        {
            // Race condition: another concurrent file inserted the same patient first.
            // Clear the failed tracked entity and re-fetch the existing record.
            _db.ChangeTracker.Clear();
            var existing = await _db.Patients.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId && p.LisPatientId == msg.PatientId && p.IsActive);
            return (existing, false);
        }
    }
}

public class Hl7ProcessResult
{
    public string MessageId     { get; set; } = "";
    public string MessageType   { get; set; } = "";
    public string PatientId     { get; set; } = "";
    public string PatientName   { get; set; } = "";
    public string AccessionId   { get; set; } = "";
    public string Status        { get; set; } = "";
    public string Notes         { get; set; } = "";
    public int ResultsSaved     { get; set; }
}