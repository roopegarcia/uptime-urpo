using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace UptimeUrpo.Api.Endpoints;

public sealed record HistoryWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, int BucketMinutes);

public sealed class HistoryParameters
{
    [FromQuery(Name = "from")] public string? From { get; set; }
    [FromQuery(Name = "to")] public string? To { get; set; }
    [FromQuery(Name = "bucketMinutes")] public string? BucketMinutes { get; set; }
    private static readonly int[] Buckets = [1, 5, 15, 60, 360, 1440];

    public bool TryResolve(DateTimeOffset now, int retentionDays, out HistoryWindow window, out Dictionary<string, string[]> errors)
    {
        errors = [];
        var to = now;
        if (To is not null)
        {
            if (TryTimestamp(To, out var parsed)) to = parsed;
            else errors["to"] = ["Use an ISO-8601 timestamp with Z or an explicit offset."];
        }
        var from = to >= DateTimeOffset.MinValue.AddDays(1) ? to.AddDays(-1) : DateTimeOffset.MinValue;
        if (From is not null && !TryTimestamp(From, out from)) errors["from"] = ["Use an ISO-8601 timestamp with Z or an explicit offset."];
        if (errors.Count == 0)
        {
            if (from >= to || to - from > TimeSpan.FromDays(30)) errors["from"] = ["Use an ordered range of at most 30 days."];
            if (to > now) errors["to"] = ["The end cannot be in the future."];
            if (from < now.AddDays(-retentionDays)) errors["from"] = ["The start must be inside the raw-history retention window."];
        }
        var bucket = Buckets.FirstOrDefault(minutes => Math.Ceiling((to - from).TotalMinutes / minutes) <= 1000);
        if (BucketMinutes is not null && (!int.TryParse(BucketMinutes, NumberStyles.None, CultureInfo.InvariantCulture, out bucket)
                || !Buckets.Contains(bucket)))
            errors["bucketMinutes"] = ["Choose 1, 5, 15, 60, 360, or 1440 minutes."];
        else if (bucket <= 0 || Math.Ceiling((to - from).TotalMinutes / bucket) > 1000)
            errors["bucketMinutes"] = ["Choose a larger bucket to return at most 1000 points."];
        window = new HistoryWindow(from, to, bucket);
        return errors.Count == 0;
    }

    private static bool TryTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value.Length > 40 || !Regex.IsMatch(value,
            @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp)) return false;
        timestamp = timestamp.ToUniversalTime();
        return true;
    }
}

public sealed class IncidentParameters
{
    [FromQuery(Name = "beforeId")] public string? BeforeId { get; set; }
    [FromQuery(Name = "limit")] public string? Limit { get; set; }

    public bool TryResolve(out long? beforeId, out int limit, out Dictionary<string, string[]> errors)
    {
        errors = [];
        beforeId = null;
        limit = 20;
        if (BeforeId is not null)
        {
            if (!long.TryParse(BeforeId, NumberStyles.None, CultureInfo.InvariantCulture, out var cursor) || cursor <= 0)
                errors["beforeId"] = ["Use a positive incident ID."];
            else beforeId = cursor;
        }
        if (Limit is not null && (!int.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
            errors["limit"] = ["Use a page size from 1 to 100."];
        return errors.Count == 0;
    }
}
