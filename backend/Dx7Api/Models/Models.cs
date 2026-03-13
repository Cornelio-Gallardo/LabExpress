using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dx7Api.Models;

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
    [MaxLength(200)] public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client? Client { get; set; }
}

public class Patient
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    [MaxLength(100)] public string? LisPatientId { get; set; }
    [MaxLength(30)] public string? PhilhealthNo { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public DateOnly? Birthdate { get; set; }
    [MaxLength(10)] public string? Gender { get; set; }
    [MaxLength(30)] public string? ContactNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client Client { get; set; } = null!;
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<Result> Results { get; set; } = new List<Result>();
}

public class Session
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public Guid PatientId { get; set; }
    public DateOnly SessionDate { get; set; }
    public int ShiftNumber { get; set; }
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

public class Result
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PatientId { get; set; }
    [MaxLength(100)] public string? AccessionId { get; set; }
    [Required, MaxLength(50)] public string TestCode { get; set; } = "";
    [Required, MaxLength(200)] public string TestName { get; set; } = "";
    public string? ResultValue { get; set; }
    [MaxLength(50)] public string? ResultUnit { get; set; }
    public string? ReferenceRange { get; set; }
    [MaxLength(5)] public string? AbnormalFlag { get; set; }
    public DateOnly ResultDate { get; set; }
    public TimeOnly? ResultTime { get; set; }
    [MaxLength(200)] public string? SourceMessageId { get; set; }
    [MaxLength(100)] public string? SourceLab { get; set; }
    [MaxLength(20)] public string ResultStatus { get; set; } = "final"; // pending, final, corrected
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("PatientId")] public Patient Patient { get; set; } = null!;
}

public class MdNote
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid MdUserId { get; set; }
    [Required] public string NoteText { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("SessionId")] public Session Session { get; set; } = null!;
    [ForeignKey("MdUserId")] public User MdUser { get; set; } = null!;
}

public class ChairAudit
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string? ChairOld { get; set; }
    public string? ChairNew { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("SessionId")] public Session Session { get; set; } = null!;
    [ForeignKey("ChangedBy")] public User ChangedByUser { get; set; } = null!;
}

// ── ShiftSchedule ─────────────────────────────────────────────────────────────
public class ShiftSchedule
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public DateOnly ScheduleDate { get; set; }
    public int ShiftNumber { get; set; }        // 1-4
    [MaxLength(50)] public string ShiftLabel { get; set; } = "";
    [MaxLength(20)] public string StartTime { get; set; } = "";
    [MaxLength(20)] public string EndTime { get; set; } = "";
    public int MaxChairs { get; set; } = 20;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ClientId")] public Client Client { get; set; } = null!;
    public ICollection<ShiftNurseAssignment> NurseAssignments { get; set; } = new List<ShiftNurseAssignment>();
}

// ── ShiftNurseAssignment ──────────────────────────────────────────────────────
public class ShiftNurseAssignment
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ShiftScheduleId { get; set; }
    public Guid NurseUserId { get; set; }
    [MaxLength(50)] public string AssignmentRole { get; set; } = "shift_nurse"; // charge_nurse / shift_nurse
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid AssignedBy { get; set; }

    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
    [ForeignKey("ShiftScheduleId")] public ShiftSchedule ShiftSchedule { get; set; } = null!;
    [ForeignKey("NurseUserId")] public User NurseUser { get; set; } = null!;
    [ForeignKey("AssignedBy")] public User AssignedByUser { get; set; } = null!;
}

// ── Role ─────────────────────────────────────────────────────────────────────
public class RoleDefinition
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(50)] public string RoleKey { get; set; } = "";
    [MaxLength(100)] public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [ForeignKey("TenantId")] public Tenant Tenant { get; set; } = null!;
}