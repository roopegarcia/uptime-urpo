namespace UptimeUrpo.Api.Monitoring;

public sealed class HttpChecker
{
    public const string ClientName = "monitoring";
    private readonly IHttpClientFactory _clients;
    private readonly TimeProvider _time;

    public HttpChecker(IHttpClientFactory clients, TimeProvider time)
    {
        _clients = clients;
        _time = time;
    }

    public async Task<CheckResult> CheckAsync(TargetOptions target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checkedAt = _time.GetUtcNow();
        var started = _time.GetTimestamp();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(target.TimeoutSeconds), _time);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var client = _clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token);
            cancellationToken.ThrowIfCancellationRequested();
            var status = (int)response.StatusCode;
            var success = status == target.ExpectedStatusCode;
            return new CheckResult(checkedAt, _time.GetElapsedTime(started).TotalMilliseconds, status,
                success, success ? null : FailureCategory.UnexpectedStatus);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown is not evidence that the monitored site failed.
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new CheckResult(checkedAt, _time.GetElapsedTime(started).TotalMilliseconds, null, false, FailureCategory.Timeout);
        }
        catch (HttpRequestException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CheckResult(checkedAt, _time.GetElapsedTime(started).TotalMilliseconds, null, false, Classify(exception));
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
