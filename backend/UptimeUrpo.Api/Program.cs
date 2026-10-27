using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeUrpo.Api.Contracts;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;
using UptimeUrpo.Api.Endpoints;

// Container readiness probe: no configuration, worker, or second database context starts.
if (args is ["--health-check"])
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready", HttpCompletionOption.ResponseHeadersRead);
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
    {
        Environment.ExitCode = 1;
    }
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Local settings override committed defaults; environment and CLI values win last.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
// Compose grants each process only its own read-only configuration secret.
if (Environment.GetEnvironmentVariable("UPTIME_CONFIG_FILE") is { Length: > 0 } configurationFile)
{
    builder.Configuration.AddJsonFile(configurationFile, optional: false, reloadOnChange: false);
}
builder.Configuration
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
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<IncidentResolution>(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Uptime-Urpo monitoring API";
    document.Info.Version = "v1";
    document.Info.Description = "Private read-only monitoring API. Availability is sample-based; coverage is reported separately. No target URLs or infrastructure credentials are exposed.";
    return Task.CompletedTask;
}));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("database-reads", _ => RateLimitPartition.Get("shared", _ => RateLimiter.CreateChained(
        new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 }),
        new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }))));
    options.OnRejected = async (context, _) =>
        await Results.Problem(statusCode: 429, title: "Monitoring read limit exceeded").ExecuteAsync(context.HttpContext);
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
builder.Services.AddScoped<MonitoringQueries>();
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
app.UseRateLimiter();
app.UseDashboardFiles();
app.MapOpenApi();
app.MapMonitoringQueries();

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok"))).WithSummary("Application liveness; target failures do not fail it.");

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
}).Produces<HealthResponse>().Produces<HealthResponse>(503).WithSummary("Database-aware readiness without private diagnostics.");

app.MapGet("/api/services", (MonitoringState state) => TypedResults.Ok(state.GetAll()))
    .WithSummary("Safe current snapshots for configured services; disabled entries are included.");

app.MapGet("/api/services/{id}", (string id, MonitoringState state) =>
{
    var service = state.Get(id);
    return service is not null
        ? Results.Ok(service)
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Service not found");
}).Produces<ServiceResponse>().ProducesProblem(404).WithSummary("A cached service snapshot; reads do not trigger checks.");

app.Run();

// WebApplicationFactory uses the entry point to start the real API in tests.
public partial class Program;
