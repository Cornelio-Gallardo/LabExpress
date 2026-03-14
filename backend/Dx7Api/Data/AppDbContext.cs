using Microsoft.EntityFrameworkCore;
using Dx7Api.Models;

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

    // Set from middleware for global query filters
    public Guid? CurrentTenantId { get; set; }

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