using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
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
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<FailureCategory>(JsonNamingPolicy.SnakeCaseLower)));

builder.Services.AddOptions<TargetOptions>()
    .BindConfiguration("Monitoring:Target")
    .Validate(target => target.IsValid(),
        "Monitoring:Target requires a safe ID/name, an HTTPS DNS URL on port 443 without credentials/query/fragment, an expected status from 200 to 599, and a timeout from 1 to 30 seconds.")
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
builder.Services.AddSingleton<StartupCheckState>();
builder.Services.AddHostedService<StartupCheckService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));

app.MapGet("/api/services/{id}", (string id, IOptions<TargetOptions> options, StartupCheckState state) =>
{
    var target = options.Value;
    return id == target.Id
        ? Results.Ok(new ServiceResponse(target.Id, target.Name, target.Enabled, state.LastCheck))
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Service not found");
});

app.Run();

// WebApplicationFactory uses the entry point to start the real API in tests.
public partial class Program;
