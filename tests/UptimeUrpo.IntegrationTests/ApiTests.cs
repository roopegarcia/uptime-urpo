using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.IntegrationTests;

public sealed class ApiTests
{
    [Fact]
    public async Task ReadsReturnTheSavedStartupResultWithoutMoreChecks()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var first = await client.GetStringAsync("/api/services/test-service");
        var second = await client.GetStringAsync("/api/services/test-service");
        using var json = JsonDocument.Parse(first);
        var service = json.RootElement;
        Assert.Equal("test-service", service.GetProperty("id").GetString());
        Assert.Equal("Test service", service.GetProperty("name").GetString());
        Assert.True(service.GetProperty("enabled").GetBoolean());
        var check = service.GetProperty("lastCheck");
        Assert.Equal(200, check.GetProperty("statusCode").GetInt32());
        Assert.True(check.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, check.GetProperty("failureCategory").ValueKind);
        Assert.True(check.GetProperty("durationMs").GetDouble() >= 0);
        Assert.Equal(TimeSpan.Zero, check.GetProperty("checkedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(first, second);
        Assert.Equal(1, factory.Handler.Calls);
        Assert.False(service.TryGetProperty("url", out _));
        Assert.DoesNotContain("example.com", first);
    }

    [Fact]
    public async Task TargetFailureDoesNotFailApplicationLiveness()
    {
        using var factory = new ApiFactory(status: 503);
        using var client = factory.CreateClient();

        Assert.Equal("{\"status\":\"ok\"}", await client.GetStringAsync("/health"));
        using var response = await client.GetAsync("/api/services/test-service");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var check = json.RootElement.GetProperty("lastCheck");
        Assert.False(check.GetProperty("success").GetBoolean());
        Assert.Equal("unexpected_status", check.GetProperty("failureCategory").GetString());
        Assert.Equal(503, check.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task DisabledMonitoringMakesNoOutboundRequest()
    {
        using var factory = new ApiFactory(enabled: false);
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services/test-service"));

        Assert.False(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lastCheck").ValueKind);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task UnknownIdReturnsProblemDetails()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/services/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(1, factory.Handler.Calls);
    }

    [Fact]
    public async Task NetworkDetailsAreNotExposedInResponses()
    {
        using var factory = new ApiFactory(networkFailure: true);
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/api/services/test-service");
        using var json = JsonDocument.Parse(body);
        var check = json.RootElement.GetProperty("lastCheck");
        Assert.Equal("dns", check.GetProperty("failureCategory").GetString());
        Assert.Equal(JsonValueKind.Null, check.GetProperty("statusCode").ValueKind);
        Assert.DoesNotContain("private-network-detail", body);
        Assert.DoesNotContain("example.com", body);
    }

    [Theory]
    [InlineData("url")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("timeout")]
    [InlineData("status")]
    public void InvalidConfigurationFailsBeforeChecking(string field)
    {
        using var factory = new ApiFactory(invalidField: field);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(0, factory.Handler.Calls);
    }

    private sealed class ApiFactory(bool enabled = true, int status = 200,
        bool networkFailure = false, string? invalidField = null) : WebApplicationFactory<Program>
    {
        public RecordingHandler Handler { get; } = new(status, networkFailure);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                // Override even an operator's ignored local config; tests never use real targets.
                services.PostConfigure<TargetOptions>(target =>
                {
                    target.Id = "test-service";
                    target.Name = "Test service";
                    target.Url = "https://example.com/";
                    target.Enabled = enabled;
                    target.ExpectedStatusCode = 200;
                    target.TimeoutSeconds = 1;
                    switch (invalidField)
                    {
                        case "url": target.Url = "https://127.0.0.1/"; break;
                        case "id": target.Id = "bad id"; break;
                        case "name": target.Name = "bad\nname"; break;
                        case "timeout": target.TimeoutSeconds = 0; break;
                        case "status": target.ExpectedStatusCode = 999; break;
                    }
                });
                services.AddHttpClient(HttpChecker.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    private sealed class RecordingHandler(int status, bool networkFailure) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (networkFailure)
                throw new HttpRequestException(HttpRequestError.NameResolutionError, "private-network-detail");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        }
    }
}
