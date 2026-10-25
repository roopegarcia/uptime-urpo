using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace UptimeUrpo.Api.Monitoring;

public sealed class HttpChecker
{
    public const string ClientName = "monitoring";
    private readonly IHttpClientFactory _clients;
    private readonly TargetOptions _target;
    private readonly TimeProvider _time;

    public HttpChecker(IHttpClientFactory clients, IOptions<TargetOptions> options, TimeProvider time)
    {
        _clients = clients;
        _target = options.Value;
        _time = time;
    }

    public async Task<CheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checkedAt = _time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_target.TimeoutSeconds));

        try
        {
            using var client = _clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, _target.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            var success = status == _target.ExpectedStatusCode;
            return new CheckResult(checkedAt, stopwatch.Elapsed.TotalMilliseconds, status,
                success, success ? null : FailureCategory.UnexpectedStatus);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown is not evidence that the monitored site failed.
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new CheckResult(checkedAt, stopwatch.Elapsed.TotalMilliseconds, null, false, FailureCategory.Timeout);
        }
        catch (HttpRequestException exception)
        {
            return new CheckResult(checkedAt, stopwatch.Elapsed.TotalMilliseconds, null, false, Classify(exception));
        }
    }

    private static FailureCategory Classify(HttpRequestException exception)
    {
        // ConnectCallback exceptions can be nested inside an HttpRequestException.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is BlockedDestinationException)
            {
                return FailureCategory.BlockedDestination;
            }

            if (current is HttpRequestException requestException)
            {
                switch (requestException.HttpRequestError)
                {
                    case HttpRequestError.NameResolutionError: return FailureCategory.Dns;
                    case HttpRequestError.SecureConnectionError: return FailureCategory.Tls;
                    case HttpRequestError.InvalidResponse: return FailureCategory.InvalidResponse;
                }
            }
        }

        return exception.HttpRequestError == HttpRequestError.ConnectionError
            ? FailureCategory.Connection
            : FailureCategory.Network;
    }
}
