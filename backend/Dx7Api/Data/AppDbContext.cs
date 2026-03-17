using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Dx7Api.Models;
using Dx7Api.Services;

namespace Dx7Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Identity & ops ────────────────────────────────────────────────────────
    public DbSet<Tenant>          Tenants          => Set<Tenant>();
    public DbSet<Client>          Clients          => Set<Client>();
    public DbSet<User>            Users            => Set<User>();
    public DbSet<Patient>         Patients         => Set<Patient>();
    public DbSet<Session>         Sessions         => Set<Session>();
    public DbSet<MdNote>          MdNotes          => Set<MdNote>();
    public DbSet<ChairAudit>      ChairAudits      => Set<ChairAudit>();
    public DbSet<RoleDefinition>  RoleDefinitions  => Set<RoleDefinition>();

    // ── CDM §2.1 HL7 archive ─────────────────────────────────────────────────
    public DbSet<Hl7Message>      Hl7Messages      => Set<Hl7Message>();

    // ── CDM §4 Order / Result layer ──────────────────────────────────────────
    public DbSet<LabOrder>        Orders           => Set<LabOrder>();
    public DbSet<ResultHeader>    ResultHeaders    => Set<ResultHeader>();
    public DbSet<ResultValue>     ResultValues     => Set<ResultValue>();

    // ── CDM §5 SXA catalog ───────────────────────────────────────────────────
    public DbSet<SxaTestCatalog>  SxaTests         => Set<SxaTestCatalog>();
    public DbSet<SxaAnalyte>      SxaAnalytes      => Set<SxaAnalyte>();

    // ── CDM §6 Normalization maps ─────────────────────────────────────────────
    public DbSet<TenantTestMap>       TenantTestMaps       => Set<TenantTestMap>();
    public DbSet<TenantAnalyteMap>    TenantAnalyteMaps    => Set<TenantAnalyteMap>();
    public DbSet<ShiftSchedule>       ShiftSchedules       => Set<ShiftSchedule>();
    public DbSet<ShiftNurseAssignment> ShiftNurseAssignments => Set<ShiftNurseAssignment>();

    // ── Flat Result table (manual / seeded data backward compat) ─────────────
    public DbSet<Result>          Results          => Set<Result>();

    // ── Audit & Lab Notes ──────────────────────────────────────────────────────
    public DbSet<AuditLog>        AuditLogs        => Set<AuditLog>();
    public DbSet<LabNote>         LabNotes         => Set<LabNote>();

    // ── System reference data (statuses, flags, labels) ───────────────────────
    public DbSet<RefData>         RefData          => Set<RefData>();

    // Set from middleware for global query filters and audit
    public Guid? CurrentTenantId { get; set; }
    public Guid? CurrentUserId   { get; set; }

    // Appendix B §4 — automatically audit every write via ChangeTracker
    private static readonly HashSet<Type> AuditedTypes = new()
    {
        typeof(User), typeof(Patient), typeof(Session), typeof(MdNote),
        typeof(LabOrder), typeof(ResultHeader), typeof(ResultValue),
        typeof(ShiftSchedule), typeof(ShiftNurseAssignment), typeof(RoleDefinition)
    };

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (CurrentTenantId.HasValue)
        {
            var entries = ChangeTracker.Entries()
                .Where(e => AuditedTypes.Contains(e.Entity.GetType())
                         && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            foreach (var entry in entries)
            {
                var action = entry.State switch
                {
                    EntityState.Added    => "CREATE",
                    EntityState.Deleted  => "DELETE",
                    _                    => "UPDATE"
                };

                // Check for activation/deactivation
                if (entry.State == EntityState.Modified
                    && entry.Properties.Any(p => p.Metadata.Name == "IsActive" && p.IsModified))
                {
                    var isActive = (bool?)entry.CurrentValues["IsActive"];
                    action = isActive == true ? "ACTIVATE" : "DEACTIVATE";
                }

                // Try to get EntityId
                Guid? entityId = null;
                var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
                if (idProp?.CurrentValue is Guid gid) entityId = gid;

                AuditLogs.Add(new AuditLog
                {
                    TenantId  = CurrentTenantId.Value,
                    UserId    = CurrentUserId,
                    Action    = action,
                    Entity    = entry.Entity.GetType().Name,
                    EntityId  = entityId,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        return await base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Explicit table name mappings (class name ≠ SQL table name) ────────
        modelBuilder.Entity<SxaTestCatalog>().ToTable("SxaTestCatalog");  // SQL has no trailing 's'
        modelBuilder.Entity<LabOrder>()      .ToTable("LabOrders");
        modelBuilder.Entity<ResultHeader>()  .ToTable("ResultHeaders");
        modelBuilder.Entity<ResultValue>()   .ToTable("ResultValues");

        // ── Global tenant isolation filters ───────────────────────────────────
        modelBuilder.Entity<Client>()          .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<User>()            .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Patient>()         .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Session>()         .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<MdNote>()          .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<RoleDefinition>()  .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Hl7Message>()      .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<LabOrder>()        .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ResultHeader>()    .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ResultValue>()     .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TenantTestMap>()   .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TenantAnalyteMap>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ShiftSchedule>()   .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ShiftNurseAssignment>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Result>()          .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuditLog>()        .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<LabNote>()         .HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);

        // ── Unique indexes ────────────────────────────────────────────────────
        // §2.1 — prevents HL7 retransmission duplicates
        modelBuilder.Entity<Hl7Message>()
            .HasIndex(h => new { h.TenantId, h.MessageControlId })
            .IsUnique();

        // §3.3 — duplicate patient detection on ingestion
        modelBuilder.Entity<Patient>()
            .HasIndex(p => new { p.TenantId, p.ClientId, p.LisPatientId })
            .IsUnique();

        // §7.1 — one session per patient per shift
        modelBuilder.Entity<Session>()
            .HasIndex(s => new { s.PatientId, s.SessionDate, s.ShiftNumber })
            .IsUnique();

        // §6.1 / §6.2 — map lookup keys
        modelBuilder.Entity<TenantTestMap>()
            .HasIndex(m => new { m.TenantId, m.TenantTestCode })
            .IsUnique();

        modelBuilder.Entity<TenantAnalyteMap>()
            .HasIndex(m => new { m.TenantId, m.TenantAnalyteCode })
            .IsUnique();

        // Results performance index
        modelBuilder.Entity<Result>()
            .HasIndex(r => new { r.TenantId, r.PatientId, r.TestCode, r.ResultDate });

        // AuditLog — query by entity
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.TenantId, a.Entity, a.EntityId });

        // LabNote → ResultHeader FK (cascade)
        modelBuilder.Entity<LabNote>()
            .HasOne(n => n.ResultHeader)
            .WithMany()
            .HasForeignKey(n => n.ResultHeaderId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Enum → string conversions ─────────────────────────────────────────
        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<SxaTestCatalog>()
            .Property(t => t.ResultType)
            .HasConversion<string>();

        modelBuilder.Entity<SxaAnalyte>()
            .Property(a => a.ResultType)
            .HasConversion<string>();

        // ── CDM §2.1 — raw_payload encrypted at rest (AES-256-GCM) ──────────
        if (Hl7Crypto.IsConfigured)
        {
            var hl7Converter = new ValueConverter<string, string>(
                v => Hl7Crypto.Encrypt(v),
                v => Hl7Crypto.Decrypt(v));
            modelBuilder.Entity<Hl7Message>()
                .Property(m => m.RawPayload)
                .HasConversion(hl7Converter);
        }

        // ── DateTime UTC normalization — Npgsql v6+ requires Kind=Utc ───────
        // Applies to every DateTime / DateTime? property across all entities.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullableConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue
                ? (v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utcNullableConverter);
            }
        }

        // ── String PKs — no auto-generate ────────────────────────────────────
        modelBuilder.Entity<SxaTestCatalog>()
            .Property(t => t.SxaTestId)
            .ValueGeneratedNever();

        modelBuilder.Entity<SxaAnalyte>()
            .Property(a => a.AnalyteCode)
            .ValueGeneratedNever();

        // ── FK delete behaviors ───────────────────────────────────────────────
        modelBuilder.Entity<Session>()
            .HasOne(s => s.AssignedByUser)
            .WithMany()
            .HasForeignKey(s => s.AssignedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ChairAudit>()
            .HasOne(c => c.ChangedByUser)
            .WithMany()
            .HasForeignKey(c => c.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MdNote>()
            .HasOne(n => n.MdUser)
            .WithMany()
            .HasForeignKey(n => n.MdUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Result>()
            .HasOne(r => r.Hl7MessageRef)
            .WithMany()
            .HasForeignKey(r => r.Hl7MessageId)
            .OnDelete(DeleteBehavior.SetNull);

        // ResultHeader → Hl7Message (restrict — provenance must not be lost)
        modelBuilder.Entity<ResultHeader>()
            .HasOne(rh => rh.SourceMsg)
            .WithMany()
            .HasForeignKey(rh => rh.SourceHl7MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // ResultHeader → SxaTestCatalog (nullable — can be null if not yet mapped)
        modelBuilder.Entity<ResultHeader>()
            .HasOne(rh => rh.SxaTest)
            .WithMany()
            .HasForeignKey(rh => rh.SxaTestId)
            .OnDelete(DeleteBehavior.SetNull);

        // LabOrder → Hl7Message (restrict)
        modelBuilder.Entity<LabOrder>()
            .HasOne(o => o.SourceMsg)
            .WithMany()
            .HasForeignKey(o => o.SourceHl7MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // ResultValue → SxaAnalyte (nullable)
        modelBuilder.Entity<ResultValue>()
            .HasOne(rv => rv.Analyte)
            .WithMany()
            .HasForeignKey(rv => rv.AnalyteCode)
            .OnDelete(DeleteBehavior.SetNull);
    }
}