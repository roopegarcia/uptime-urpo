using System.Text.Json;
using System.Text.Json.Serialization;
using UptimeUrpo.Api.Contracts;
using UptimeUrpo.Api.Monitoring;

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

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));

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
