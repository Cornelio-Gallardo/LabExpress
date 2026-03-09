using Dx7Api.Data;
using Dx7Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Services.Hl7;

/// <summary>
/// Processes HL7 messages and saves to database.
/// ORM^O01 → saves pending order record
/// ORU^R01 → saves final result values (updates existing pending if found)
/// </summary>
public class Hl7Processor
{
    private readonly AppDbContext _db;
    private readonly ILogger<Hl7Processor> _logger;

    public Hl7Processor(AppDbContext db, ILogger<Hl7Processor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Hl7ProcessResult> ProcessAsync(Hl7Message msg, Guid tenantId)
    {
        var result = new Hl7ProcessResult
        {
            MessageId   = msg.MessageId,
            MessageType = msg.MessageType,
            PatientId   = msg.PatientId,
            AccessionId = msg.AccessionId
        };

        try
        {
            // 1. Resolve or auto-create patient
            var (patient, wasCreated) = await ResolveOrCreatePatientAsync(msg, tenantId);
            if (patient == null)
            {
                result.Status = "error";
                result.Notes  = $"Could not resolve or create patient from HL7 message (no PID data).";
                _logger.LogWarning("HL7 {MsgId}: Patient could not be resolved", msg.MessageId);
                return result;
            }
            if (wasCreated)
                _logger.LogInformation("HL7 {MsgId}: Auto-created patient {Name} (LIS: {Pid})",
                    msg.MessageId, patient.Name, patient.LisPatientId);

            result.PatientName = patient.Name;

            var resultDate = DateOnly.FromDateTime(
                msg.ObservationDateTime != default ? msg.ObservationDateTime : DateTime.UtcNow);

            // ── ORM^O01 — Order message ───────────────────────────────────────
            if (msg.IsOrder)
            {
                // Save a pending record for the ordered test
                // This appears in the patient's results as "Pending"
                var exists = await _db.Results.AnyAsync(r =>
                    r.TenantId   == tenantId &&
                    r.PatientId  == patient.Id &&
                    r.AccessionId == msg.AccessionId &&
                    r.TestCode   == msg.TestCode);

                if (!exists && !string.IsNullOrEmpty(msg.TestCode))
                {
                    _db.Results.Add(new Result
                    {
                        TenantId      = tenantId,
                        PatientId     = patient.Id,
                        TestCode      = msg.TestCode,
                        TestName      = msg.TestName,
                        ResultValue   = null,           // no value yet
                        ResultStatus  = "pending",
                        ResultDate    = resultDate,
                        SourceLab     = msg.SendingFacility,
                        AccessionId   = msg.AccessionId,
                        SourceMessageId = msg.MessageId,
                    });

                    await _db.SaveChangesAsync();
                    result.Status      = "order_saved";
                    result.ResultsSaved = 1;
                    result.Notes       = $"Pending order saved: {msg.TestName} (Acc: {msg.AccessionId})";
                }
                else
                {
                    result.Status = "duplicate";
                    result.Notes  = $"Order already exists for accession {msg.AccessionId} / {msg.TestCode}";
                }

                _logger.LogInformation("HL7 {MsgId}: ORM {Status} — {Patient} / {Test}",
                    msg.MessageId, result.Status, patient.Name, msg.TestName);
                return result;
            }

            // ── ORU^R01 — Result message ──────────────────────────────────────
            if (msg.IsResult)
            {
                if (msg.Observations.Count == 0)
                {
                    // ORU with no OBX — treat the OBR test itself as final
                    result.Status = "skipped";
                    result.Notes  = "ORU has no OBX segments";
                    return result;
                }

                int saved    = 0;
                int updated  = 0;
                int skipped  = 0;

                foreach (var obs in msg.Observations)
                {
                    var testCode = !string.IsNullOrEmpty(obs.TestCode) ? obs.TestCode : msg.TestCode;
                    var testName = !string.IsNullOrEmpty(obs.TestName) ? obs.TestName : msg.TestName;
                    var obsDate  = obs.ObservationDateTime != default
                        ? DateOnly.FromDateTime(obs.ObservationDateTime)
                        : resultDate;

                    if (string.IsNullOrWhiteSpace(obs.ResultValue)) { skipped++; continue; }

                    var status = obs.ResultStatus switch
                    {
                        "F" => "final",
                        "C" => "corrected",
                        "P" => "preliminary",
                        _   => "final"
                    };

                    // Check if a pending order record exists for this accession+test → update it
                    var existing = await _db.Results.FirstOrDefaultAsync(r =>
                        r.TenantId    == tenantId &&
                        r.PatientId   == patient.Id &&
                        r.AccessionId == msg.AccessionId &&
                        r.TestCode    == testCode);

                    if (existing != null)
                    {
                        // Update pending → final
                        existing.ResultValue   = obs.ResultValue;
                        existing.ResultUnit    = obs.ResultUnit;
                        existing.ReferenceRange = obs.ReferenceRange;
                        existing.AbnormalFlag  = obs.AbnormalFlag;
                        existing.ResultDate    = obsDate;
                        existing.ResultStatus  = status;
                        existing.UpdatedAt     = DateTime.UtcNow;
                        updated++;
                    }
                    else
                    {
                        // New result (no prior order)
                        _db.Results.Add(new Result
                        {
                            TenantId        = tenantId,
                            PatientId       = patient.Id,
                            TestCode        = testCode,
                            TestName        = testName,
                            ResultValue     = obs.ResultValue,
                            ResultUnit      = obs.ResultUnit,
                            ReferenceRange  = obs.ReferenceRange,
                            AbnormalFlag    = obs.AbnormalFlag,
                            ResultDate      = obsDate,
                            ResultStatus    = status,
                            SourceLab       = msg.SourceLab.Length > 0 ? msg.SourceLab : msg.SendingFacility,
                            AccessionId     = msg.AccessionId,
                            SourceMessageId = msg.MessageId,
                        });
                        saved++;
                    }
                }

                await _db.SaveChangesAsync();
                result.Status      = "processed";
                result.ResultsSaved = saved + updated;
                result.Notes       = $"Saved {saved} new, updated {updated} pending, skipped {skipped}";
                _logger.LogInformation("HL7 {MsgId}: ORU saved {Count} results for {Patient}",
                    msg.MessageId, saved + updated, patient.Name);
                return result;
            }

            result.Status = "unknown";
            result.Notes  = $"Unhandled message type: {msg.MessageType}";
        }
        catch (Exception ex)
        {
            result.Status = "error";
            result.Notes  = ex.Message;
            _logger.LogError(ex, "HL7 {MsgId}: Processing error", msg.MessageId);
        }

        return result;
    }

    private async Task<(Patient? patient, bool wasCreated)> ResolveOrCreatePatientAsync(
        Hl7Message msg, Guid tenantId)
    {
        // 1. Match by LIS Patient ID
        if (!string.IsNullOrEmpty(msg.PatientId))
        {
            var byId = await _db.Patients.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId && p.LisPatientId == msg.PatientId && p.IsActive);
            if (byId != null) return (byId, false);
        }

        // 2. Match by full name
        if (!string.IsNullOrEmpty(msg.PatientName))
        {
            var byName = await _db.Patients.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.Name.ToLower() == msg.PatientName.ToLower() &&
                p.IsActive);
            if (byName != null)
            {
                // Update LIS ID if missing
                if (string.IsNullOrEmpty(byName.LisPatientId) && !string.IsNullOrEmpty(msg.PatientId))
                {
                    byName.LisPatientId = msg.PatientId;
                    await _db.SaveChangesAsync();
                }
                return (byName, false);
            }
        }

        // 3. Auto-create from HL7 PID data
        if (string.IsNullOrEmpty(msg.PatientId) && string.IsNullOrEmpty(msg.PatientName))
            return (null, false);

        // Parse birthdate
        DateOnly? dob = null;
        if (!string.IsNullOrEmpty(msg.PatientDob) && msg.PatientDob.Length >= 8)
        {
            if (DateOnly.TryParseExact(msg.PatientDob[..8], "yyyyMMdd", out var parsedDob))
                dob = parsedDob;
        }

        // Parse gender
        var gender = msg.PatientGender?.ToUpper() switch {
            "M" => "M", "F" => "F", _ => null
        };

        // Build full name: "LASTNAME, Firstname"
        var name = !string.IsNullOrEmpty(msg.PatientName)
            ? msg.PatientName
            : msg.PatientId; // fallback to MR number if no name

        // Get default client for this tenant (first active clinic)
        var defaultClient = await _db.Clients
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (defaultClient == null)
        {
            _logger.LogWarning("HL7: No active client/clinic found for tenant {TenantId}", tenantId);
            return (null, false);
        }

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
        await _db.SaveChangesAsync();

        return (newPatient, true);
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