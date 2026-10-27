using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.IntegrationTests;

internal sealed class AnalyticsApiFactory(MonitoringScenario scenario, string? connection = null) : WebApplicationFactory<Program>
{
    public PersistenceHealth Health { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            // API tests seed the real database themselves. No scheduled HTTP requests run.
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                && (service.ImplementationType == typeof(MonitoringWorker) || service.ImplementationType == typeof(RetentionWorker))).ToArray())
                services.Remove(descriptor);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(scenario.Clock);
            services.RemoveAll<PersistenceHealth>();
            Health.SetReady(true);
            services.AddSingleton(Health);
            services.PostConfigure<DatabaseOptions>(options => options.ConnectionString = connection ?? scenario.Database.Runtime);
            services.PostConfigure<MonitoringOptions>(options =>
            {
                options.Target = null;
                options.Targets = scenario.Options.Targets;
                options.MaxConcurrency = 3;
            });
        });
    }

    public void Restore(MonitoringScenario source)
    {
        var snapshots = source.State.GetAll().Select(service => new PersistedSnapshot(service.Id, service.LastCheck,
            service.LastCompletedAtUtc, service.ConsecutiveFailures)).ToArray();
        Services.GetRequiredService<MonitoringState>().Restore(snapshots);
    }
}
