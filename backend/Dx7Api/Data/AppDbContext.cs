using Microsoft.EntityFrameworkCore;
using Dx7Api.Models;

namespace Dx7Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Result> Results => Set<Result>();
    public DbSet<MdNote> MdNotes => Set<MdNote>();
    public DbSet<ChairAudit> ChairAudits => Set<ChairAudit>();
    public DbSet<ShiftSchedule> ShiftSchedules => Set<ShiftSchedule>();
    public DbSet<ShiftNurseAssignment> ShiftNurseAssignments => Set<ShiftNurseAssignment>();
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();

    // Set this from middleware to enable global query filters
    public Guid? CurrentTenantId { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global query filters - defense-in-depth tenant isolation
        // Even if a controller forgets TenantId filter, no cross-tenant data leaks
        modelBuilder.Entity<Client>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<User>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Patient>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Session>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<MdNote>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ShiftSchedule>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ShiftNurseAssignment>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<RoleDefinition>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);


        modelBuilder.Entity<Patient>()
            .HasIndex(p => new { p.TenantId, p.ClientId, p.LisPatientId })
            .IsUnique();

        modelBuilder.Entity<Session>()
            .HasIndex(s => new { s.PatientId, s.SessionDate, s.ShiftNumber })
            .IsUnique();

        modelBuilder.Entity<Result>()
            .HasIndex(r => new { r.TenantId, r.PatientId, r.TestCode, r.ResultDate });

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();

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

        modelBuilder.Entity<ShiftNurseAssignment>()
            .HasOne(s => s.AssignedByUser)
            .WithMany()
            .HasForeignKey(s => s.AssignedBy)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShiftNurseAssignment>()
            .HasOne(s => s.NurseUser)
            .WithMany()
            .HasForeignKey(s => s.NurseUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}