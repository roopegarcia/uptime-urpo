using System.Text.RegularExpressions;

namespace UptimeUrpo.Api.Monitoring;

public sealed class TargetOptions
{
    public string Id { get; set; } = "example";
    public string Name { get; set; } = "Example service";
    public string Url { get; set; } = "https://example.com/";
    public bool Enabled { get; set; }
    public int ExpectedStatusCode { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 10;

    public bool IsValid()
    {
        return Id is { Length: >= 1 and <= 64 }
            && Regex.IsMatch(Id, "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)
            && !string.IsNullOrWhiteSpace(Name)
            && Name.Length <= 80
            && !Name.Any(char.IsControl)
            && ExpectedStatusCode is >= 200 and <= 599
            && TimeoutSeconds is >= 1 and <= 30
            && Url is { Length: <= 2048 }
            && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Port == 443
            && uri.HostNameType == UriHostNameType.Dns
            && uri.IdnHost.Contains('.')
            && !uri.IdnHost.EndsWith('.')
            && !uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0;
    }
}
