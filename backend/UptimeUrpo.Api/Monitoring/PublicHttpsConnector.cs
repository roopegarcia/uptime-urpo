using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace UptimeUrpo.Api.Monitoring;

public sealed class BlockedDestinationException : Exception
{
    public BlockedDestinationException() : base("Destination rejected by monitoring policy.") { }
}

public sealed class PublicHttpsConnector
{
    private readonly HashSet<string> _approvedHosts;
    private readonly IDnsResolver _resolver;
    private readonly Func<IPEndPoint, CancellationToken, ValueTask<Stream>> _connect;

    public PublicHttpsConnector(IOptions<MonitoringOptions> options, IDnsResolver resolver)
        : this(options.Value.GetTargets(), resolver, ConnectSocketAsync) { }

    // A narrow test seam verifies address pinning without opening a real socket.
    internal PublicHttpsConnector(TargetOptions target, IDnsResolver resolver,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
        : this([target], resolver, connect) { }

    internal PublicHttpsConnector(IEnumerable<TargetOptions> targets, IDnsResolver resolver,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
    {
        _approvedHosts = targets.Select(target => new Uri(target.Url).IdnHost)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _resolver = resolver;
        _connect = connect;
    }

    public SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        MaxConnectionsPerServer = 1,
        MaxResponseHeadersLength = 16,
        MaxResponseDrainSize = 0,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = (context, token) => ConnectAsync(context.DnsEndPoint, token)
        // HTTPS/TLS and original-host certificate validation remain with SocketsHttpHandler.
    };

    internal async ValueTask<Stream> ConnectAsync(DnsEndPoint destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (destination.Port != 443 || !_approvedHosts.Contains(destination.Host))
        {
            throw new BlockedDestinationException();
        }

        var addresses = await _resolver.ResolveAsync(destination.Host, cancellationToken);
        // Reject mixed public/private answers before connecting to any address.
        if (addresses.Length == 0 || addresses.Any(address => !PublicAddressPolicy.IsPublic(address)))
        {
            throw new BlockedDestinationException();
        }

        foreach (var address in addresses.Distinct())
        {
            try
            {
                // Connect to the validated IP, never resolve the hostname a second time.
                return await _connect(new IPEndPoint(address, 443), cancellationToken);
            }
            catch (SocketException)
            {
                // Try the next already-validated address within the same request timeout.
            }
        }

        throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection failed.");
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPEndPoint destination, CancellationToken token)
    {
        var socket = new Socket(destination.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(destination, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
