namespace Dx7Api.DTOs;

// ── Auth ─────────────────────────────────────────────────────────────────────
public record LoginRequest(string Email, string Password);
public record ExternalLoginRequest(string Provider, string Token);

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
    DateOnly SessionDate, int ShiftNumber, string ShiftLabel, string? Chair,
    string AssignedByName, DateTime AssignedAt
);

public record CreateSessionRequest(Guid PatientId, DateOnly SessionDate, string ShiftLabel, string? Chair, Guid? ClientId = null, int ShiftNumber = 0);

public record BulkCreateSessionRequest(List<Guid> PatientIds, DateOnly SessionDate, int ShiftNumber, Guid? ClientId = null);

public record UpdateChairRequest(string? Chair);

// ── Results — CDM §4 full chain ───────────────────────────────────────────────
// ResultValueDto — one per OBX. Carries canonical identity (§4.3).
public record ResultValueDto(
    Guid   Id,
    Guid   ResultHeaderId,
    string? AnalyteCode,           // FK → SXA_Analyte
    string? AnalyteDisplayName,    // from SXA_Analyte.DisplayName
    string  DisplayValue,          // OBX-5 pass-through — no transformation
    decimal? ValueNumeric,
    string? Unit,                  // OBX-6
    decimal? ReferenceRangeLow,
    decimal? ReferenceRangeHigh,
    string? ReferenceRangeRaw,
    string? AbnormalFlag,          // OBX-8 raw pass-through only
    string? RawHl7Segment          // complete OBX — traceability
);

// ResultHeaderDto — one per OBR (§4.2).
public record ResultHeaderDto(
    Guid    Id,
    Guid    OrderId,
    string? SxaTestId,             // FK → SXA_Test_Catalog
    string? SxaTestName,           // from SXA_Test_Catalog.CanonicalName
    string? SpecimenType,
    DateTime? CollectionDatetime,
    DateTime? ResultDatetime,
    Guid    SourceHl7MessageId,
    List<ResultValueDto> Values
);

// LabOrderDto — one per order/accession (§4.1).
public record LabOrderDto(
    Guid    Id,
    string  AccessionNumber,       // OBR-3
    DateTime? ReleasedAt,
    DateTime  CreatedAt,
    Guid    SourceHl7MessageId,
    List<ResultHeaderDto> Headers
);

// ResultDto — flat projection used by compare/current/history endpoints.
// Reads from ResultValues through the chain. Backward-compatible shape.
public record ResultDto(
    Guid    Id,
    string  TestCode,              // analyte_code (SXA canonical)
    string  TestName,              // analyte display_name
    string? ResultValue,           // display_value pass-through
    string? ResultUnit,
    string? ReferenceRange,        // raw reference range string
    string? AbnormalFlag,          // OBX-8 raw
    DateOnly ResultDate,
    string? SourceLab,
    int     DaysSinceResult,
    string  ResultStatus,
    string? AccessionId,
    // CDM additions
    string? AnalyteCode,           // SXA analyte code
    string? SxaTestId,             // SXA test id
    string? SxaTestName,           // SXA test canonical name (panel label)
    Guid?   ResultHeaderId,        // traceability → ResultHeader
    Guid?   OrderId,               // traceability → Order
    Guid?   Hl7MessageId           // traceability → HL7_Message
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

// ── Clinics ───────────────────────────────────────────────────────────────────
public record CreateClinicRequest(string Name, string Code, string? Address, string? LogoUrl);
public record UpdateClinicRequest(string? Name, string? Code, string? Address, string? LogoUrl);

// ── Roles ─────────────────────────────────────────────────────────────────────
public record CreateRoleRequest(string RoleKey, string Label, string Description, int SortOrder);
public record UpdateRoleRequest(string? Label, string? Description, int? SortOrder, bool? IsActive);

// ── Tenant / Branding ─────────────────────────────────────────────────────────
public record TenantDetailDto(Guid Id, string Name, string Code, string PrimaryColor, string? LogoUrl, string? FooterText, bool IsActive);
public record UpdateTenantBrandingRequest(string? PrimaryColor, string? LogoUrl, string? FooterText);
public record UpdateClinicBrandingRequest(string? LogoUrl, string? PrimaryColor);

// ── Code Mappings ─────────────────────────────────────────────────────────────
public record SxaTestDto(string SxaTestId, string CanonicalName, string Category);
public record SxaAnalyteDto(string AnalyteCode, string DisplayName, string? DefaultUnit);
public record TestMapDto(Guid Id, string TenantTestCode, string SxaTestId, string CanonicalName, bool IsActive);
public record AnalyteMapDto(Guid Id, string TenantAnalyteCode, string AnalyteCode, string DisplayName, bool IsActive);
public record CreateTestMapRequest(string TenantTestCode, string SxaTestId);
public record CreateAnalyteMapRequest(string TenantAnalyteCode, string AnalyteCode);

// ── Longitudinal ─────────────────────────────────────────────────────────────
public record LongitudinalValueDto(string Date, string? Accession, string DisplayValue, string? AbnormalFlag, Guid ResultHeaderId);
public record LongitudinalRowDto(string AnalyteCode, string AnalyteName, string? Unit, string? ReferenceRange, List<LongitudinalValueDto> Values);

// ── Audit Defense ─────────────────────────────────────────────────────────────
public record UrRDto(string PatientName, string? LisPatientId, string Date, string? AccessionPre, string? AccessionPost, decimal? BunPre, decimal? BunPost, decimal? Urr, decimal? KtV);

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record ReprocessRequest(string Path);

// ── Shifts ────────────────────────────────────────────────────────────────────
public record ShiftNurseDto(
    Guid Id, Guid NurseUserId, string Name,
    string Email, string? AssignmentRole, DateTime AssignedAt
);

public record ShiftScheduleDto(
    Guid Id, Guid ClientId, DateOnly ScheduleDate,
    int ShiftNumber, string ShiftLabel, string? StartTime, string? EndTime,
    int MaxChairs, bool IsActive, string? Notes,
    int PatientCount, int FilledChairs,
    List<ShiftNurseDto> NurseAssignments
);

public record CreateShiftScheduleRequest(
    DateOnly ScheduleDate, int ShiftNumber, string ShiftLabel,
    string? StartTime, string? EndTime, int MaxChairs,
    string? Notes, Guid? ClientId
);

public record UpdateShiftScheduleRequest(
    string? ShiftLabel, string? StartTime, string? EndTime,
    int? MaxChairs, bool? IsActive, string? Notes
);

public record AssignNurseRequest(Guid NurseUserId, string? AssignmentRole);

public record BulkShiftRequest(
    DateOnly FromDate, DateOnly ToDate, int MaxChairs,
    Guid? ClientId,
    List<BulkShiftItem>? Shifts
);

public record BulkShiftItem(int ShiftNumber, string ShiftLabel, string StartTime, string EndTime);

// ── Export ────────────────────────────────────────────────────────────────────
public record ShiftPdfRequest(List<Guid> SessionIds);