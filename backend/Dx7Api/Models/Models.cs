using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dx7Api.Models;

// ── §3.1 Tenant ───────────────────────────────────────────────────────────────
public class Tenant
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Code { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? FooterText { get; set; }
    public string PrimaryColor { get; set; } = "0D7377";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

// ── §3.2 Client (Dialysis Clinic) ────────────────────────────────────────────
public class Client
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Code { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    public ICollection<Patient> Patients { get; set; } = new List<Patient>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}

// ── §8 Users & Auth ───────────────────────────────────────────────────────────
public enum UserRole { sysad, pl_admin, clinic_admin, charge_nurse, shift_nurse, md }

public class User
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? ClientId { get; set; }
    [Required, MaxLength(200)] public string Email { get; set; } = "";
    [Required] public string PasswordHash { get; set; } = "";
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.shift_nurse;
    public bool IsActive { get; set; } = true;
    public string? AvatarUrl { get; set; }
    [MaxLength(50)]  public string? ExternalProvider { get; set; }
    [MaxLength(200)] public string? ExternalProviderId { get; set; }
    [MaxLength(200)] public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client? Client { get; set; }
}

// ── §3.3 Patient ──────────────────────────────────────────────────────────────
// Created automatically from HL7. Corrections must be made in LIS.
public class Patient
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    [MaxLength(100)] public string? LisPatientId { get; set; }   // PID-3
    [MaxLength(30)]  public string? PhilhealthNo { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = ""; // PID-5
    public DateOnly? Birthdate { get; set; }                          // PID-7
    [MaxLength(10)] public string? Gender { get; set; }               // PID-8 M|F|O
    [MaxLength(30)] public string? ContactNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client Client { get; set; } = null!;
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<LabOrder> Orders { get; set; } = new List<LabOrder>();
}

// ── §7.1 Session ──────────────────────────────────────────────────────────────
public class Session
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public Guid PatientId { get; set; }
    public DateOnly SessionDate { get; set; }
    public int ShiftNumber { get; set; }
    [MaxLength(50)] public string ShiftLabel { get; set; } = "";
    [MaxLength(20)] public string? Chair { get; set; }
    public Guid AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client Client { get; set; } = null!;
    [ForeignKey("PatientId")] public Patient Patient { get; set; } = null!;
    [ForeignKey("AssignedBy")] public User AssignedByUser { get; set; } = null!;
    public ICollection<MdNote> MdNotes { get; set; } = new List<MdNote>();
    public ICollection<ChairAudit> ChairAudits { get; set; } = new List<ChairAudit>();
}

// ── §2.1 HL7_Message ──────────────────────────────────────────────────────────
// Raw HL7 archive. Every downstream record traces back to exactly one row here.
public class Hl7Message
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [Required, MaxLength(100)] public string MessageControlId { get; set; } = ""; // MSH-10
    public string RawPayload { get; set; } = "";   // complete HL7 — never discard
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public bool ProcessedFlag { get; set; } = false;
    public bool QuarantineFlag { get; set; } = false;
    [MaxLength(500)] public string? QuarantineReason { get; set; }
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
}

// ── §4.1 Order ────────────────────────────────────────────────────────────────
// One row per OBR segment — the ordered test.
public class LabOrder
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }                            // referring clinic
    public Guid PatientId { get; set; }
    [Required, MaxLength(100)] public string AccessionNumber { get; set; } = ""; // OBR-3
    public Guid SourceHl7MessageId { get; set; }                  // provenance — required
    public DateTime? ReleasedAt { get; set; }                     // result release timestamp
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")]            public Tenant     Tenant     { get; set; } = null!;
    [ForeignKey("ClientId")]            public Client     Client     { get; set; } = null!;
    [ForeignKey("PatientId")]           public Patient    Patient    { get; set; } = null!;
    [ForeignKey("SourceHl7MessageId")]  public Hl7Message SourceMsg  { get; set; } = null!;
    public ICollection<ResultHeader> ResultHeaders { get; set; } = new List<ResultHeader>();
}

// ── §4.2 ResultHeader ─────────────────────────────────────────────────────────
// Test-level grouping — one per OBR, groups all OBX analytes for that test.
public class ResultHeader
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SourceHl7MessageId { get; set; }                  // provenance — required
    [MaxLength(50)] public string? SxaTestId { get; set; }        // FK → SxaTestCatalog
    [MaxLength(200)] public string? SpecimenType { get; set; }
    public DateTime? CollectionDatetime { get; set; }             // OBR-7
    public DateTime? ResultDatetime { get; set; }                 // OBR-22
    [ForeignKey("OrderId")]             public LabOrder      Order     { get; set; } = null!;
    [ForeignKey("TenantId")]            public Tenant        Tenant    { get; set; } = null!;
    [ForeignKey("SourceHl7MessageId")]  public Hl7Message    SourceMsg { get; set; } = null!;
    [ForeignKey("SxaTestId")]           public SxaTestCatalog? SxaTest { get; set; }
    public ICollection<ResultValue> ResultValues { get; set; } = new List<ResultValue>();
}

// ── §4.3 ResultValue ──────────────────────────────────────────────────────────
// One row per OBX segment. Display value is raw pass-through — no transformation.
public class ResultValue
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResultHeaderId { get; set; }                      // FK → ResultHeader
    public Guid TenantId { get; set; }                            // CDM global rule #1
    [MaxLength(50)] public string? AnalyteCode { get; set; }      // FK → SxaAnalyte
    public string DisplayValue { get; set; } = "";                // OBX-5 as received — pass-through
    public decimal? ValueNumeric { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }             // OBX-6
    public decimal? ReferenceRangeLow { get; set; }
    public decimal? ReferenceRangeHigh { get; set; }
    public string? ReferenceRangeRaw { get; set; }                // raw string for display
    [MaxLength(5)] public string? AbnormalFlag { get; set; }      // OBX-8 raw pass-through only
    public string? RawHl7Segment { get; set; }                    // complete OBX — traceability
    // CDM Amendment 1 §10.4 — special value flags
    public bool NoSpecimen    { get; set; } = false;              // OBX-5 was '*'  — no specimen received
    public bool NotCalculated { get; set; } = false;              // OBX-5 was '---' — calculated field indeterminate
    // CDM Amendment 1 §11.2 — schema version for ingestion traceability
    [MaxLength(30)] public string SchemaVersion { get; set; } = "DX7_CDM_1.0_A1";
    [ForeignKey("ResultHeaderId")] public ResultHeader ResultHeader { get; set; } = null!;
    [ForeignKey("TenantId")]       public Tenant       Tenant      { get; set; } = null!;
    [ForeignKey("AnalyteCode")]    public SxaAnalyte?  Analyte     { get; set; }
}

// ── §5.1 SXA_Test_Catalog ────────────────────────────────────────────────────
public enum SxaResultType { SINGLE, PANEL }
public class SxaTestCatalog
{
    [Key, MaxLength(50)] public string SxaTestId { get; set; } = "";
    [Required, MaxLength(200)] public string CanonicalName { get; set; } = "";
    [MaxLength(50)] public string Category { get; set; } = "";
    public SxaResultType ResultType { get; set; } = SxaResultType.SINGLE;
    public bool ActiveFlag { get; set; } = true;
}

// ── §5.2 SXA_Analyte ─────────────────────────────────────────────────────────
public enum SxaAnalyteResultType { NUMERIC, TEXT, ENUM }
public class SxaAnalyte
{
    [Key, MaxLength(50)] public string AnalyteCode { get; set; } = "";
    [Required, MaxLength(200)] public string DisplayName { get; set; } = "";
    [MaxLength(20)] public string? DefaultUnit { get; set; }
    public SxaAnalyteResultType ResultType { get; set; } = SxaAnalyteResultType.NUMERIC;
}

// ── §6.1 TenantTestMap (OBR-4 → SXA_Test_Catalog) ───────────────────────────
public class TenantTestMap
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [Required, MaxLength(50)] public string TenantTestCode { get; set; } = "";
    [Required, MaxLength(50)] public string SxaTestId { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [ForeignKey("TenantId")]  public Tenant         Tenant  { get; set; } = null!;
    [ForeignKey("SxaTestId")] public SxaTestCatalog SxaTest { get; set; } = null!;
}

// ── §6.2 TenantAnalyteMap (OBX-3 → SXA_Analyte) ────────────────────────────
public class TenantAnalyteMap
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [Required, MaxLength(50)] public string TenantAnalyteCode { get; set; } = "";
    [Required, MaxLength(50)] public string AnalyteCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [ForeignKey("TenantId")]     public Tenant      Tenant  { get; set; } = null!;
    [ForeignKey("AnalyteCode")]  public SxaAnalyte  Analyte { get; set; } = null!;
}

// ── Result (flat table — kept for seeded/manual data and backward compat) ─────
// New HL7 ingestion writes through LabOrder → ResultHeader → ResultValue.
// This table remains for manually entered or seeded results.
public class Result
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? Hl7MessageId { get; set; }
    [MaxLength(200)] public string? SourceMessageId { get; set; }
    [MaxLength(50)]  public string? SxaTestId { get; set; }
    [MaxLength(50)]  public string? AnalyteCode { get; set; }
    [MaxLength(100)] public string? AccessionId { get; set; }
    [Required, MaxLength(50)]  public string TestCode { get; set; } = "";
    [Required, MaxLength(200)] public string TestName { get; set; } = "";
    public string? DisplayValue { get; set; }
    public string? ResultValue { get; set; }
    public decimal? ValueNumeric { get; set; }
    [MaxLength(50)] public string? ResultUnit { get; set; }
    public decimal? ReferenceRangeLow { get; set; }
    public decimal? ReferenceRangeHigh { get; set; }
    public string? ReferenceRange { get; set; }
    [MaxLength(5)] public string? AbnormalFlag { get; set; }
    public string? RawHl7Segment { get; set; }
    public DateOnly ResultDate { get; set; }
    public TimeOnly? ResultTime { get; set; }
    [MaxLength(100)] public string? SourceLab { get; set; }
    [MaxLength(20)] public string ResultStatus { get; set; } = "final";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")]     public Tenant     Tenant        { get; set; } = null!;
    [ForeignKey("PatientId")]    public Patient    Patient       { get; set; } = null!;
    [ForeignKey("Hl7MessageId")] public Hl7Message? Hl7MessageRef { get; set; }
}

// ── §7.2 Chair_Audit ─────────────────────────────────────────────────────────
public class ChairAudit
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public string? ChairOld { get; set; }
    public string? ChairNew { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")]  public Tenant  Tenant        { get; set; } = null!;
    [ForeignKey("SessionId")] public Session Session       { get; set; } = null!;
    [ForeignKey("ChangedBy")] public User    ChangedByUser { get; set; } = null!;
}

// ── §7.3 MD_Notes ────────────────────────────────────────────────────────────
public class MdNote
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid MdUserId { get; set; }
    [Required] public string NoteText { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")]  public Tenant  Tenant  { get; set; } = null!;
    [ForeignKey("SessionId")] public Session Session { get; set; } = null!;
    [ForeignKey("MdUserId")]  public User    MdUser  { get; set; } = null!;
}

// ── RoleDefinition (Dx7 operational) ─────────────────────────────────────────
public class RoleDefinition
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(50)]  public string RoleKey     { get; set; } = "";
    [MaxLength(100)] public string Label       { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
}

// ── ShiftSchedule (Dx7 operational) ──────────────────────────────────────────
public class ShiftSchedule
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public DateOnly ScheduleDate { get; set; }
    public int ShiftNumber { get; set; }
    [MaxLength(100)] public string ShiftLabel { get; set; } = "";
    [MaxLength(10)]  public string? StartTime { get; set; }
    [MaxLength(10)]  public string? EndTime   { get; set; }
    public int MaxChairs { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client Client { get; set; } = null!;
    public ICollection<ShiftNurseAssignment> NurseAssignments { get; set; } = new List<ShiftNurseAssignment>();
}

// ── AuditLog (Appendix B §4 — append-only audit trail) ───────────────────────
public class AuditLog
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(50)]  public string Action   { get; set; } = ""; // CREATE UPDATE DELETE DEACTIVATE ACTIVATE LOGIN
    [MaxLength(100)] public string Entity   { get; set; } = ""; // User Patient Session MdNote etc.
    public Guid? EntityId { get; set; }
    public string? Before { get; set; }   // JSON snapshot
    public string? After  { get; set; }   // JSON snapshot
    [MaxLength(500)] public string? Notes  { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
}

// ── LabNote (NTE segments from HL7) ──────────────────────────────────────────
public class LabNote
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ResultHeaderId { get; set; }
    [Required] public string NoteText { get; set; } = "";
    public int SortOrder { get; set; } = 0;
    [ForeignKey("TenantId")]       public Tenant       Tenant       { get; set; } = null!;
    [ForeignKey("ResultHeaderId")] public ResultHeader ResultHeader { get; set; } = null!;
}

// ── RefData (system-level reference / lookup values) ─────────────────────────
// Stores all status codes, flag values, and label strings that would otherwise
// be hardcoded throughout the codebase. Categories: Hl7Status, ResultStatus,
// AbnormalFlag, Gender, AuditAction, OBXSkipKeyword, UserStatus.
public class RefData
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(50)]  public string Category  { get; set; } = ""; // Hl7Status, ResultStatus, …
    [Required, MaxLength(50)]  public string Code      { get; set; } = ""; // processed, error, H, L, …
    [Required, MaxLength(100)] public string Label     { get; set; } = ""; // "Processed", "High", …
    public string? Description { get; set; }
    public int     SortOrder   { get; set; } = 0;
    public bool    IsActive    { get; set; } = true;
}

// ── ShiftNurseAssignment ──────────────────────────────────────────────────────
public class ShiftNurseAssignment
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ShiftScheduleId { get; set; }
    public Guid NurseUserId { get; set; }
    [MaxLength(50)] public string? AssignmentRole { get; set; }
    public Guid AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")]        public Tenant        Tenant        { get; set; } = null!;
    [ForeignKey("ShiftScheduleId")] public ShiftSchedule ShiftSchedule { get; set; } = null!;
    [ForeignKey("NurseUserId")]     public User          NurseUser     { get; set; } = null!;
}