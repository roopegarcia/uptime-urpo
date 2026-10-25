using System.Net;
using System.Net.Sockets;

namespace UptimeUrpo.Api.Monitoring;

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new HttpRequestException(HttpRequestError.NameResolutionError,
                "DNS resolution failed.", exception);
        }
    }
}
