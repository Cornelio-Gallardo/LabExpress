namespace Dx7Api.DTOs;

// ── Auth ─────────────────────────────────────────────────────────────────────
public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string Token,
    UserDto User,
    TenantDto Tenant,
    ClientDto? Client
);

// ── User ─────────────────────────────────────────────────────────────────────
public record UserDto(Guid Id, string Name, string Email, string Role, Guid TenantId, Guid? ClientId);

public record TenantDto(Guid Id, string Name, string PrimaryColor, string? LogoUrl, string? FooterText);

public record ClientDto(Guid Id, string Name, string? LogoUrl, string? Address);

// ── Patients ─────────────────────────────────────────────────────────────────


public record PatientDto(
    Guid Id, string Name, string? LisPatientId, string? PhilhealthNo,
    DateOnly? Birthdate, string? Gender, string? ContactNumber,
    bool IsActive, string ResultStatus, int? DaysSinceLastResult, DateOnly? LastResultDate,
    int ResultDateCount = 0
);

public record CreatePatientRequest(
    string Name, string? LisPatientId, string? PhilhealthNo, DateOnly? Birthdate, string? Gender, string? ContactNumber
);

// ── Sessions ─────────────────────────────────────────────────────────────────
public record SessionDto(
    Guid Id, Guid PatientId, string PatientName,
    DateOnly SessionDate, int ShiftNumber, string? Chair,
    string AssignedByName, DateTime AssignedAt
);

public record CreateSessionRequest(Guid PatientId, DateOnly SessionDate, int ShiftNumber, string? Chair, Guid? ClientId = null);

public record BulkCreateSessionRequest(List<Guid> PatientIds, DateOnly SessionDate, int ShiftNumber, Guid? ClientId = null);

public record UpdateChairRequest(string? Chair);

// ── Results ──────────────────────────────────────────────────────────────────
public record ResultDto(
    Guid Id, string TestCode, string TestName,
    string? ResultValue, string? ResultUnit, string? ReferenceRange,
    string? AbnormalFlag, DateOnly ResultDate, string? SourceLab,
    int DaysSinceResult, string ResultStatus, string? AccessionId
);

public record CreateResultRequest(
    Guid PatientId, string TestCode, string TestName,
    string? ResultValue, string? ResultUnit, string? ReferenceRange,
    string? AbnormalFlag, DateOnly ResultDate, string? SourceLab, string? AccessionId
);

// ── MD Notes ─────────────────────────────────────────────────────────────────
public record MdNoteDto(
    Guid Id, Guid SessionId, string NoteText,
    string MdName, DateTime CreatedAt, DateTime UpdatedAt,
    bool CanEdit
);

public record CreateNoteRequest(Guid SessionId, string NoteText);

public record UpdateNoteRequest(string NoteText);

// ── Export ───────────────────────────────────────────────────────────────────
public record ExportRequest(
    List<Guid> PatientIds,
    DateOnly FromDate,
    DateOnly ToDate,
    List<string>? TestCodes,
    string Format // "pdf" | "csv"
);

// ── Users ─────────────────────────────────────────────────────────────────────
public record UserDetailDto(
    Guid Id, string Name, string Email, string Role,
    Guid TenantId, Guid? ClientId, string? ClientName,
    bool IsActive, DateTime CreatedAt, string? AvatarUrl
);

public record CreateUserRequest(
    string Name, string Email, string Password,
    string Role, Guid? ClientId
);

public record UpdateUserRequest(
    string? Name, string? Email, string? Password,
    string? Role, Guid? ClientId
);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// ── Shift Management ──────────────────────────────────────────────────────────
public record ShiftScheduleDto(
    Guid Id, Guid ClientId, DateOnly ScheduleDate,
    int ShiftNumber, string ShiftLabel, string StartTime, string EndTime,
    int MaxChairs, bool IsActive, string? Notes,
    int PatientCount, int ChairsFilled,
    List<ShiftNurseDto> Nurses
);

public record ShiftNurseDto(
    Guid Id, Guid NurseUserId, string NurseName,
    string NurseEmail, string AssignmentRole, DateTime AssignedAt
);

public record CreateShiftScheduleRequest(
    DateOnly ScheduleDate, int ShiftNumber,
    string ShiftLabel, string StartTime, string EndTime,
    int MaxChairs, string? Notes, Guid? ClientId = null
);

public record UpdateShiftScheduleRequest(
    string? ShiftLabel, string? StartTime, string? EndTime,
    int? MaxChairs, bool? IsActive, string? Notes
);

public record AssignNurseRequest(Guid NurseUserId, string AssignmentRole);

public record BulkShiftItemRequest(int ShiftNumber, string ShiftLabel, string StartTime, string EndTime);
public record BulkShiftRequest(DateOnly FromDate, DateOnly ToDate, int MaxChairs, Guid? ClientId = null, List<BulkShiftItemRequest>? Shifts = null);

// ── Clinics ───────────────────────────────────────────────────────────────────
public record CreateClinicRequest(string Name, string Code, string? Address, string? LogoUrl);
public record UpdateClinicRequest(string? Name, string? Code, string? Address, string? LogoUrl);

// ── Roles ─────────────────────────────────────────────────────────────────────
public record CreateRoleRequest(string RoleKey, string Label, string Description, int SortOrder);
public record UpdateRoleRequest(string? Label, string? Description, int? SortOrder, bool? IsActive);

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);