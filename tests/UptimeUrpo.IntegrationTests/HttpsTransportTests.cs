using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.IntegrationTests;

public sealed class HttpsTransportTests
{
    [Fact]
    public async Task PreservesTlsHostAndDoesNotFollowRedirects()
    {
        await using var server = new HttpsFixture("example.com");
        var calls = 0;
        var connector = new PublicHttpsConnector(new TargetOptions(), new PublicResolver(), async (_, token) =>
        {
            // Only this test seam redirects the socket to a disposable loopback TLS fixture.
            calls++;
            return await server.ConnectAsync(token);
        });
        using var handler = connector.CreateHandler();
        handler.SslOptions.CertificateChainPolicy = server.TrustPolicy;
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        using var response = await client.GetAsync("https://example.com/health", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, calls);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("GET /health HTTP/1.1", request);
        Assert.Contains("Host: example.com", request);
        Assert.Equal("example.com", server.SniHost);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsWrongHostnameOrUntrustedCertificate(bool trustCertificate)
    {
        await using var server = new HttpsFixture(trustCertificate ? "wrong.example" : "example.com");
        var connector = new PublicHttpsConnector(new TargetOptions(), new PublicResolver(), server.ConnectAsync);
        using var handler = connector.CreateHandler();
        if (trustCertificate) handler.SslOptions.CertificateChainPolicy = server.TrustPolicy;
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.com/"));

        Assert.Equal(HttpRequestError.SecureConnectionError, exception.HttpRequestError);
    }

    private sealed class PublicResolver : IDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken token) =>
            Task.FromResult<IPAddress[]>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class HttpsFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
        private readonly RSA _rootKey = RSA.Create(2048);
        private readonly RSA _leafKey = RSA.Create(2048);
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _certificate;
        private readonly Task _server;
        private readonly TaskCompletionSource<string> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> Request => _request.Task;
        public string? SniHost { get; private set; }
        public X509ChainPolicy TrustPolicy { get; }

        public HttpsFixture(string hostname)
        {
            var now = DateTimeOffset.UtcNow;
            var rootRequest = new CertificateRequest("CN=Uptime-Urpo test root", _rootKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            _root = rootRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));

            var leafRequest = new CertificateRequest($"CN={hostname}", _leafKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(hostname);
            leafRequest.CertificateExtensions.Add(names.Build());
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var signed = leafRequest.Create(_root, now.AddMinutes(-1), now.AddHours(1), RandomNumberGenerator.GetBytes(16));
            _certificate = signed.CopyWithPrivateKey(_leafKey);
            TrustPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true
            };
            TrustPolicy.CustomTrustStore.Add(_root);
            _listener.Start();
            _server = ServeAsync();
        }

        public async ValueTask<Stream> ConnectAsync(IPEndPoint ignored, CancellationToken token)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync((IPEndPoint)_listener.LocalEndpoint, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }

        public async ValueTask<Stream> ConnectAsync(CancellationToken token) =>
            await ConnectAsync((IPEndPoint)_listener.LocalEndpoint, token);

        private async Task ServeAsync()
        {
            try
            {
                using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var ssl = new SslStream(socket.GetStream());
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (_, hostname) =>
                    {
                        SniHost = hostname;
                        return _certificate;
                    },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, _stop.Token);
                var headers = new StringBuilder();
                using var reader = new StreamReader(ssl, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    headers.AppendLine(line);
                _request.TrySetResult(headers.ToString());
                var response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 302 Found\r\nLocation: https://127.0.0.1/private\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await ssl.WriteAsync(response, _stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or AuthenticationException or IOException)
            {
                _request.TrySetCanceled();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _server; }
            finally
            {
                _certificate.Dispose();
                _root.Dispose();
                _leafKey.Dispose();
                _rootKey.Dispose();
                _stop.Dispose();
            }
        }
    }
}
