using System.Net;
using System.Net.Sockets;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.UnitTests;

public sealed class DestinationPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.9")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("2001::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("3fff::1")]
    public void RejectsNonPublicAndTransitionAddresses(string address) =>
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("100.128.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    public void AcceptsPublicAddresses(string address) =>
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com/?key=secret")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://localhost/")]
    [InlineData("https://test.localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://2130706433/")]
    [InlineData("https://0x7f000001/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://example.com./")]
    [InlineData("not a URL")]
    public void RejectsUnsafeOrUnsupportedConfiguration(string url) =>
        Assert.False(new TargetOptions { Url = url }.IsValid());

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://EXAMPLE.com/health")]
    [InlineData("https://xn--bcher-kva.example/")]
    public void AcceptsCanonicalHttpsHostConfiguration(string url) =>
        Assert.True(new TargetOptions { Url = url }.IsValid());

    [Fact]
    public async Task PinsTheValidatedIpAndRevalidatesEveryNewConnection()
    {
        var resolver = new StubResolver();
        var connections = new List<IPEndPoint>();
        var connector = new PublicHttpsConnector(new TargetOptions(), resolver, (endpoint, _) =>
        {
            connections.Add(endpoint);
            return ValueTask.FromResult<Stream>(new MemoryStream());
        });

        using var stream = await connector.ConnectAsync(new DnsEndPoint("example.com", 443), CancellationToken.None);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), Assert.Single(connections).Address);
        Assert.Equal(1, resolver.Calls);

        resolver.Addresses = [IPAddress.Loopback];
        await Assert.ThrowsAsync<BlockedDestinationException>(async () =>
            await connector.ConnectAsync(new DnsEndPoint("example.com", 443), CancellationToken.None));
        Assert.Equal(2, resolver.Calls);
        Assert.Single(connections); // Rebound private IP was never dialed.
    }

    [Fact]
    public async Task RejectsMixedDnsAnswersBeforeOpeningAnySocket()
    {
        var resolver = new StubResolver { Addresses = [IPAddress.Parse("1.1.1.1"), IPAddress.Loopback] };
        var connector = new PublicHttpsConnector(new TargetOptions(), resolver, (_, _) =>
            throw new InvalidOperationException("Must not open a socket."));

        await Assert.ThrowsAsync<BlockedDestinationException>(async () =>
            await connector.ConnectAsync(new DnsEndPoint("example.com", 443), CancellationToken.None));
    }

    [Theory]
    [InlineData("other.example", 443)]
    [InlineData("example.com", 80)]
    public async Task RejectsDestinationsOutsideTheConfiguredAllowlist(string host, int port)
    {
        var resolver = new StubResolver();
        var connector = new PublicHttpsConnector(new TargetOptions(), resolver, (_, _) =>
            throw new InvalidOperationException("Must not open a socket."));

        await Assert.ThrowsAsync<BlockedDestinationException>(async () =>
            await connector.ConnectAsync(new DnsEndPoint(host, port), CancellationToken.None));
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task FallsBackOnlyToAnotherAlreadyValidatedAddress()
    {
        var resolver = new StubResolver { Addresses = [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("8.8.8.8")] };
        var attempts = new List<IPEndPoint>();
        var connector = new PublicHttpsConnector(new TargetOptions(), resolver, (endpoint, _) =>
        {
            attempts.Add(endpoint);
            if (attempts.Count == 1) throw new SocketException((int)SocketError.ConnectionRefused);
            return ValueTask.FromResult<Stream>(new MemoryStream());
        });

        using var stream = await connector.ConnectAsync(new DnsEndPoint("example.com", 443), CancellationToken.None);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), attempts[1].Address);
        Assert.Equal(1, resolver.Calls);
    }

    private sealed class StubResolver : IDnsResolver
    {
        public IPAddress[] Addresses { get; set; } = [IPAddress.Parse("1.1.1.1")];
        public int Calls { get; private set; }
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Addresses);
        }
    }
}
