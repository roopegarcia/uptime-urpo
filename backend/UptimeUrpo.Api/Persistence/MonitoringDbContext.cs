using Microsoft.EntityFrameworkCore;

namespace UptimeUrpo.Api.Persistence;

public sealed class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : DbContext(options)
{
    public DbSet<MonitoredService> Services => Set<MonitoredService>();
    public DbSet<MonitoringPeriod> Periods => Set<MonitoringPeriod>();
    public DbSet<CheckRecord> Checks => Set<CheckRecord>();
    public DbSet<IncidentRecord> Incidents => Set<IncidentRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<IncidentRecord>(entity =>
        {
            entity.ToTable("incidents", table => table.HasCheckConstraint("CK_Incidents_Resolution",
                "(\"EndedAtUtc\" IS NULL AND \"Resolution\" IS NULL) OR (\"EndedAtUtc\" IS NOT NULL AND \"Resolution\" IS NOT NULL)"));
            entity.Property(incident => incident.ServiceId).HasMaxLength(64);
            entity.Property(incident => incident.Resolution).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<MonitoredService>().WithMany().HasForeignKey(incident => incident.ServiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(incident => incident.ServiceId).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
            entity.HasIndex(incident => new { incident.ServiceId, incident.Id });
            entity.HasIndex(incident => incident.EndedAtUtc);
        });
        model.Entity<MonitoredService>(entity =>
        {
            entity.ToTable("monitored_services");
            entity.HasKey(service => service.Id);
            entity.Property(service => service.Id).HasMaxLength(64);
            entity.Property(service => service.Name).HasMaxLength(80);
            entity.Property(service => service.Url).HasMaxLength(2048);
            entity.Property(service => service.LastFailureCategory).HasConversion<string>().HasMaxLength(32);
        });
        model.Entity<MonitoringPeriod>(entity =>
        {
            entity.ToTable("monitoring_periods");
            entity.Property(period => period.ServiceId).HasMaxLength(64);
            entity.Property(period => period.Url).HasMaxLength(2048);
            entity.HasOne<MonitoredService>().WithMany().HasForeignKey(period => period.ServiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(period => period.ServiceId).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
            entity.HasIndex(period => new { period.ServiceId, period.StartedAtUtc });
        });
        model.Entity<CheckRecord>(entity =>
        {
            entity.ToTable("check_results", table =>
                table.HasCheckConstraint("CK_CheckResults_Duration", "\"DurationMs\" >= 0"));
            entity.Property(check => check.ServiceId).HasMaxLength(64);
            entity.Property(check => check.FailureCategory).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<MonitoredService>().WithMany().HasForeignKey(check => check.ServiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MonitoringPeriod>().WithMany().HasForeignKey(check => check.MonitoringPeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(check => new { check.ServiceId, check.CheckedAtUtc });
            entity.HasIndex(check => check.CompletedAtUtc);
        });
    }
}
