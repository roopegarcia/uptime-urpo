using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UptimeUrpo.Api.Persistence;

// Allows migration generation without starting the web host or the monitoring worker.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MonitoringDbContext>
{
    public MonitoringDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Monitoring")
            ?? "Host=127.0.0.1;Port=54330;Database=uptime_urpo;Username=uptime_urpo_migrator";
        return new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(connection).Options);
    }
}
