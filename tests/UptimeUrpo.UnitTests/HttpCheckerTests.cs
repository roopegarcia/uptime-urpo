using System.Net;
using Microsoft.Extensions.Time.Testing;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.UnitTests;

public sealed class HttpCheckerTests
{
    [Theory]
    [InlineData(200, 200, true)]
    [InlineData(204, 200, false)]
    [InlineData(401, 401, true)]
    [InlineData(500, 200, false)]
    [InlineData(302, 200, false)]
    public async Task EvaluatesTheExactConfiguredStatus(int actual, int expected, bool success)
    {
        var content = new UnreadableContent();
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)actual) { Content = content });
        });
        var checker = CreateChecker(handler);

        var result = await checker.CheckAsync(new TargetOptions { ExpectedStatusCode = expected }, CancellationToken.None);

        Assert.Equal(actual, result.StatusCode);
        Assert.Equal(success, result.Success);
        Assert.Equal(success ? null : FailureCategory.UnexpectedStatus, result.FailureCategory);
        Assert.Equal(TimeSpan.Zero, result.CheckedAtUtc.Offset);
        Assert.True(result.DurationMs >= 0);
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, FailureCategory.Dns)]
    [InlineData(HttpRequestError.ConnectionError, FailureCategory.Connection)]
    [InlineData(HttpRequestError.SecureConnectionError, FailureCategory.Tls)]
    [InlineData(HttpRequestError.InvalidResponse, FailureCategory.InvalidResponse)]
    [InlineData(HttpRequestError.Unknown, FailureCategory.Network)]
    public async Task SanitizesNetworkFailures(HttpRequestError error, FailureCategory expected)
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException(error, "private detail"));

        var result = await CreateChecker(handler).CheckAsync(new TargetOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Equal(expected, result.FailureCategory);
    }

    [Fact]
    public async Task RecognizesBlockedDestinationInsideTransportException()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "transport wrapper", new BlockedDestinationException()));

        var result = await CreateChecker(handler).CheckAsync(new TargetOptions(), CancellationToken.None);

        Assert.Equal(FailureCategory.BlockedDestination, result.FailureCategory);
    }

    [Fact]
    public async Task RecognizesDnsFailureInsideTransportException()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "wrapper", new HttpRequestException(
                HttpRequestError.NameResolutionError, "private DNS detail")));

        var result = await CreateChecker(handler).CheckAsync(new TargetOptions(), CancellationToken.None);

        Assert.Equal(FailureCategory.Dns, result.FailureCategory);
    }

    [Fact]
    public async Task TimeoutCancelsTheRequestAndBecomesAFailedObservation()
    {
        var cancelled = false;
        var handler = new StubHandler(async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled = token.IsCancellationRequested; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var clock = new FakeTimeProvider();
        var check = CreateChecker(handler, clock)
            .CheckAsync(new TargetOptions { TimeoutSeconds = 1 }, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await check;

        Assert.True(cancelled);
        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Equal(FailureCategory.Timeout, result.FailureCategory);
    }

    [Fact]
    public async Task ShutdownCancellationPropagatesInsteadOfBecomingAResult()
    {
        using var shutdown = new CancellationTokenSource();
        var handler = new StubHandler(async (_, token) =>
        {
            shutdown.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateChecker(handler).CheckAsync(new TargetOptions(), shutdown.Token));
    }

    private static HttpChecker CreateChecker(HttpMessageHandler handler, TimeProvider? time = null) =>
        new(new StubClientFactory(handler), time ?? TimeProvider.System);

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }

    private sealed class UnreadableContent : HttpContent
    {
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The checker must not download the response body.");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
