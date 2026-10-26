using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeUrpo.Api.Contracts;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Local settings override committed defaults; environment and CLI values win last.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
}

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<FailureCategory>(JsonNamingPolicy.SnakeCaseLower));
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<HealthStatus>(JsonNamingPolicy.SnakeCaseUpper));
});

builder.Services.AddOptions<MonitoringOptions>()
    .BindConfiguration("Monitoring")
    .Validate(options => options.IsValid(),
        "Monitoring requires 0-20 valid targets with unique IDs, concurrency 1-8, and either Targets or legacy Target. See docs/MILESTONE-2.md for target limits.")
    .ValidateOnStart();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .Configure(options => options.ConnectionString = builder.Configuration.GetConnectionString("Monitoring") ?? "")
    .Validate(options => options.IsValid(), "Configure a valid ConnectionStrings:Monitoring and retention limits; see docs/MILESTONE-3.md.")
    .ValidateOnStart();
builder.Services.AddDbContext<MonitoringDbContext>((services, options) => options.UseNpgsql(
    services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
    postgres => postgres.CommandTimeout(5)));
builder.Services.AddScoped<IMonitoringStore, MonitoringStore>();
builder.Services.AddSingleton<PersistenceHealth>();
// Persistence failures are logged by sanitized category at our boundary, not with SQL/host details.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Npgsql", LogLevel.None);
builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
builder.Services.AddSingleton<PublicHttpsConnector>();
builder.Services.AddHttpClient(HttpChecker.ClientName, client =>
    {
        // The checker owns the linked timeout token, so timeout and shutdown stay distinct.
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Uptime-Urpo/0.1");
    })
    .ConfigurePrimaryHttpMessageHandler(services =>
        services.GetRequiredService<PublicHttpsConnector>().CreateHandler())
    .RemoveAllLoggers(); // Default HTTP client logs include destination URLs.
builder.Services.AddTransient<HttpChecker>();
builder.Services.AddSingleton<MonitoringState>();
builder.Services.AddHostedService<MonitoringWorker>();
builder.Services.AddHostedService<RetentionWorker>();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
    return;
}
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));

app.MapGet("/health/ready", async (IMonitoringStore store, PersistenceHealth health, CancellationToken token) =>
{
    try
    {
        var ready = health.IsReady && await store.IsSchemaReadyAsync(token);
        return Results.Json(new HealthResponse(ready ? "ready" : "not_ready"), statusCode: ready ? 200 : 503);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
    catch
    {
        return Results.Json(new HealthResponse("not_ready"), statusCode: 503);
    }
});

app.MapGet("/api/services", (MonitoringState state) => TypedResults.Ok(state.GetAll()));

app.MapGet("/api/services/{id}", (string id, MonitoringState state) =>
{
    var service = state.Get(id);
    return service is not null
        ? Results.Ok(service)
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Service not found");
});

app.Run();

// WebApplicationFactory uses the entry point to start the real API in tests.
public partial class Program;
