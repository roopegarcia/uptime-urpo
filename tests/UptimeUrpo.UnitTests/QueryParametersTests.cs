using UptimeUrpo.Api.Endpoints;

namespace UptimeUrpo.UnitTests;

public sealed class QueryParametersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(7, 15)]
    [InlineData(30, 60)]
    public void SelectsBoundedBucketSizes(int days, int expectedMinutes)
    {
        var parameters = new HistoryParameters { From = Now.AddDays(-days).ToString("O") };
        Assert.True(parameters.TryResolve(Now, 90, out var window, out _));
        Assert.Equal(expectedMinutes, window.BucketMinutes);
        Assert.True(Math.Ceiling((window.ToUtc - window.FromUtc).TotalMinutes / window.BucketMinutes) <= 1000);
    }

    [Fact]
    public void OffsetTimestampsNormalizeToUtc()
    {
        var parameters = new HistoryParameters { From = "2026-10-27T13:00:00+02:00", To = "2026-10-27T14:00:00+02:00" };
        Assert.True(parameters.TryResolve(Now, 90, out var window, out _));
        Assert.Equal(Now.AddHours(-1), window.FromUtc);
        Assert.Equal(Now, window.ToUtc);
    }

    [Theory]
    [InlineData("2026-10-27")]
    [InlineData("2026-10-27T11:00:00")]
    [InlineData("2026-02-30T11:00:00Z")]
    [InlineData("27/10/2026T11:00:00Z")]
    [InlineData("2026-10-27T11:00:00+99:00")]
    [InlineData("secret-invalid-value")]
    [InlineData("0001-01-01T00:00:00Z")]
    public void MalformedOrUnretainedEndReturnsValidationWithoutThrowing(string value)
    {
        var parameters = new HistoryParameters { To = value };
        Assert.False(parameters.TryResolve(Now, 90, out _, out var errors));
        Assert.NotEmpty(errors);
        Assert.DoesNotContain(value, string.Join(" ", errors.Values.SelectMany(messages => messages)));
    }

    [Theory]
    [InlineData("2026-10-27T12:00:01Z", null)]
    [InlineData("2026-10-27T12:00:00Z", "2026-10-27T11:00:00Z")]
    [InlineData("2026-09-01T00:00:00Z", null)]
    [InlineData("2025-10-27T00:00:00Z", "2025-10-28T00:00:00Z")]
    public void RejectsFutureReversedOversizedAndExpiredRanges(string from, string? to) =>
        Assert.False(new HistoryParameters { From = from, To = to }.TryResolve(Now, 90, out _, out _));

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("garbage")]
    [InlineData("1")]
    public void RejectsUnsupportedOrOverlyDenseBuckets(string bucket) =>
        Assert.False(new HistoryParameters { BucketMinutes = bucket }.TryResolve(Now, 90, out _, out _));

    [Theory]
    [InlineData("0", null)]
    [InlineData("101", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData(null, "0")]
    [InlineData(null, "-1")]
    [InlineData(null, "9223372036854775808")]
    public void RejectsBadIncidentPagination(string? limit, string? before) =>
        Assert.False(new IncidentParameters { Limit = limit, BeforeId = before }.TryResolve(out _, out _, out _));

    [Fact]
    public void IncidentPaginationHasSafeDefaultsAndPositiveCursor()
    {
        Assert.True(new IncidentParameters().TryResolve(out var before, out var limit, out _));
        Assert.Null(before);
        Assert.Equal(20, limit);
        Assert.True(new IncidentParameters { Limit = "100", BeforeId = "42" }.TryResolve(out before, out limit, out _));
        Assert.Equal(42, before);
        Assert.Equal(100, limit);
    }
}
