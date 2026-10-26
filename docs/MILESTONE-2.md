# Milestone 2 — Background monitoring

Status: implemented and locally verified. M3 requires approval before persistence work begins.

## What changed

`MonitoringWorker : BackgroundService` replaces the M1 one-shot startup service. Each enabled target has a sequential async loop, and all loops share a configurable `SemaphoreSlim` concurrency limit. The first check is due immediately, subject to available capacity. The API can respond before it finishes; that target initially reports UNKNOWN.

After each completed attempt, its loop releases the concurrency slot and waits the target's interval before trying again. With a 60-second interval and a 200-ms request, consecutive starts are approximately 60.2 seconds apart, plus any capacity wait. This is **fixed-delay scheduling**, not a promise to start on every wall-clock minute. Missed opportunities are not queued. A paused process resumes with at most one pending attempt per enabled target and still respects the global limit.

The checker now receives the specific `TargetOptions` for each request and has no mutable per-target fields. The shared HTTP handler permits only configured HTTPS hostnames and validates DNS/IP destinations for each new connection. Redirect, proxy, TLS, header-size, and body-avoidance policies from M1 remain in place.

No database, incident tracking, uptime calculation, frontend, deployment, or CI was added. Only the latest observation and current failure streak exist in memory.

## Configuration

Committed settings contain `Monitoring:Targets: []` and send no target requests. Copy the sanitized example only if local settings do not already exist:

```sh
if [ ! -f backend/UptimeUrpo.Api/appsettings.Local.json ]; then
  cp backend/UptimeUrpo.Api/appsettings.Local.example.json backend/UptimeUrpo.Api/appsettings.Local.json
fi
chmod 600 backend/UptimeUrpo.Api/appsettings.Local.json
```

New configuration uses a list. This sanitized example sends no traffic until you replace the URLs with owned, approved services and enable them:

```json
{
  "Monitoring": {
    "MaxConcurrency": 3,
    "Targets": [
      {
        "Id": "website",
        "Name": "Website",
        "Url": "https://example.com/",
        "Enabled": false,
        "ExpectedStatusCode": 200,
        "IntervalSeconds": 60,
        "TimeoutSeconds": 10,
        "FailureThreshold": 3,
        "SlowResponseThresholdMs": 2000
      },
      {
        "Id": "status-page",
        "Name": "Status page",
        "Url": "https://status.example.com/",
        "Enabled": false
      }
    ]
  }
}
```

Omitted per-target values use the defaults shown on the first entry. IDs must be unique. Up to 20 configured targets are permitted, including disabled entries. `MaxConcurrency` defaults to 3 and permits 1–8.

Per-target limits:

- `IntervalSeconds`: 5–86,400; default 60.
- `TimeoutSeconds`: 1–30; default 10; must be strictly shorter than the interval. This avoids configuring normal request timeouts to consume the entire freshness window.
- `FailureThreshold`: 1–10; default 3. Choosing 1 explicitly opts into DOWN after one failed check.
- `SlowResponseThresholdMs`: 1–30,000; default 2,000.
- Existing ID/name/URL/status restrictions from M1 still apply: safe ID, nonblank name, HTTPS DNS hostname on port 443, no credentials/query/fragment, and expected status 200–599.

The existing M1 `Monitoring:Target` object remains supported and produces a migration warning without printing its values. Its new settings use defaults, so existing local configuration works without editing. To migrate, move that object into `Targets: [ ... ]` and remove the old `Target` property. A nonempty `Targets` list together with `Target` is rejected; the application never silently merges conflicting shapes. This milestone did not rewrite the operator's local settings.

Configuration is fixed for the process lifetime. Restart after editing. Environment variables still override local JSON and command-line values override environment variables. New list settings use keys such as `Monitoring__Targets__0__Enabled`. Legacy variables such as `Monitoring__Target__Enabled` apply only to the legacy object. Do not mix them with a nonempty list. Use a complete local file for list changes: .NET configuration overlays array entries by index, rather than treating each source as a wholesale replacement.

Local settings stay ignored by Git and excluded from build/publish output. No public configuration endpoint exists.

## Health rules

Freshness is evaluated when a snapshot is read, using monotonic elapsed time since the last **completed** observation. UTC timestamps remain available for display. A wall-clock correction does not make a fresh result stale or extend its life.

Rules, in order:

1. **UNKNOWN:** disabled, no completed result, or more than twice the target interval has elapsed since completion.
2. **UP:** fresh successful expected response with duration at or below the latency threshold.
3. **DEGRADED:** fresh successful response above the latency threshold, or a fresh failed check below the failure threshold.
4. **DOWN:** fresh failed checks reaching the configured consecutive-failure threshold.

One success resets the failure streak, even if slow; a slow success recovers DOWN to DEGRADED. A stale gap resets the streak before a new result is recorded. Disabled targets never run. Failure counters saturate at the configured threshold; stale snapshots report zero current consecutive failures but preserve the last observation. Threshold equality is deliberate: exactly 2,000 ms is UP with the default slow threshold; exactly two intervals old is still fresh, and anything older is UNKNOWN.

The last observation is not rewritten when it becomes stale. It remains visible so callers can distinguish a historical success/failure from current UNKNOWN. UNKNOWN does not establish recovery or downtime. There are no incidents yet.

## API additions

- `GET /api/services` returns an array of current snapshots, including disabled services; no targets gives `[]`.
- `GET /api/services/{id}` keeps the original safe fields and adds `health`, `consecutiveFailures`, and nullable `lastCompletedAtUtc`.
- `GET /health` remains application liveness. Target failures do not fail it.

Example completed snapshot (illustrative values):

```json
{
  "id": "website",
  "name": "Website",
  "enabled": true,
  "health": "UP",
  "consecutiveFailures": 0,
  "lastCompletedAtUtc": "2026-10-25T12:00:00.200+00:00",
  "lastCheck": {
    "checkedAtUtc": "2026-10-25T12:00:00+00:00",
    "durationMs": 200,
    "statusCode": 200,
    "success": true,
    "failureCategory": null
  }
}
```

The request-start time stays inside `lastCheck`; completion time describes when the observation became available. Snapshots are copied under a short `lock` so concurrent reads and worker writes see consistent fields. No lock is held during network work or an `await`.

Reads never initiate checks. Repeated reads may nevertheless differ now because the worker runs independently. Unknown IDs still return 404 Problem Details. DTOs expose neither URLs nor internal network/exception details. This API still has no authentication and should remain on its default loopback listener.

## Failures, cancellation, and logs

Expected HTTP/network failures become categorized results, release their slot, and allow the next normal interval to run. There are no immediate retries. A cancelled host stops active requests, semaphore waits, and interval delays; it does not record a fabricated timeout or DOWN observation. `BackgroundService.StopAsync` waits for the loops to finish before their shared semaphore is disposed.

An unexpected checker exception logs only the safe service ID and exception type. It leaves a gap rather than fabricating target downtime, releases the slot, and tries again after the normal interval. Repeated internal faults produce recurring error logs and eventually stale UNKNOWN data; investigate them rather than treating them as website failures.

Standard HTTP-client URL logging remains disabled. Expected completion logs contain ID, success, status, and category. No response body, resolved IP, URL, or raw exception message is logged by the worker.

## Run and verify

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet build Uptime-Urpo.slnx --no-restore
dotnet test Uptime-Urpo.slnx --no-build --no-restore
dotnet run --project backend/UptimeUrpo.Api --no-build --no-launch-profile
```

In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/api/services
# Use one of the configured IDs from the list response.
curl --fail http://127.0.0.1:5080/api/services/iltapaska
```

With an enabled owned target, inspect its snapshot again after at least one configured interval plus request time. `lastCompletedAtUtc` should advance. A clean checkout has no targets; configure one before expecting an observation. If port 5080 is already occupied by an earlier local instance, stop that instance yourself or choose a separate loopback port with `--urls http://127.0.0.1:5081`.

For failure/recovery demonstrations, use controlled tests instead of taking down a real service:

```sh
dotnet test tests/UptimeUrpo.UnitTests --no-build --no-restore --filter FullyQualifiedName~MonitoringWorkerTests
dotnet test tests/UptimeUrpo.UnitTests --no-build --no-restore --filter FullyQualifiedName~MonitoringStateTests
dotnet test tests/UptimeUrpo.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~ScheduledFailuresReachDownAndASuccessRecovers
```

`FakeTimeProvider` advances scheduling, request timeout, and freshness without waiting real minutes. Tests use small bounded waits only to let asynchronous continuations finish, not to simulate the monitoring interval. Existing local TLS tests still use disposable loopback sockets/certificates. No automated check needs public internet or Docker; restore and advisory lookup need network access.

## Verification record — 2026-10-25

- .NET SDK 10.0.402, runtime 10.0.13, macOS ARM64.
- Locked restore and Release build passed with zero warnings/errors. Formatting verification, shell syntax, local Markdown links/anchors, and Git whitespace checks passed.
- 102 unit cases and 24 integration cases passed: **126 total**, none skipped.
- NuGet's configured advisory source reported no known vulnerable direct/transitive packages at verification time. The new fake-time package is test-only; this does not guarantee the absence of future advisories.
- Controlled-time scenarios cover independent intervals, global concurrency and queued progress, no overlap, long pauses without catch-up bursts, disabled targets, per-target timeout, cancellation while active/queued/sleeping, and recovery after an internal checker fault.
- Health tests cover failure thresholds, slow and fast recovery, stale boundaries, gap reset, custom thresholds, completion-based freshness, and wall-clock adjustments.
- API tests cover pending-first-check liveness, list/detail JSON, periodic DOWN/recovery, stale results, disabled targets, legacy settings, invalid settings, and no requests triggered by reads. Destination tests cover multiple configured hosts retaining DNS/IP policy; original TLS tests still pass.
- A separate local process used the existing ignored legacy target on a temporary loopback port. Two scheduled checks of the approved owned `iltapaska.com` target returned HTTP 200 and UP, at approximately 371 ms and 171 ms. Completion timestamps advanced by approximately 60.17 seconds. Liveness responded, two completion logs were recorded, and SIGTERM produced graceful shutdown with exit code 0.

The live smoke process was stopped afterward. The application's default listener and the operator's existing local settings were not changed. No NUC services, Cloudflare rules, database resources, or deployments were touched. Builds/tests and a local live-target smoke check do not establish deployed reliability.

## Learning and next milestone

Read [tutorial Part 3](../tutorial.md#part-3--run-checks-in-the-background) and [the M2 C# notes](../CSHARP-FOR-ROOPE.md#milestone-2--asynchronous-lifecycle-and-shared-state).

The main new concepts are hosted lifecycle, cooperative cancellation, asynchronous concurrency, shared-state locking, and controllable time. Restart deliberately loses all current observations and failure streaks. M3 will introduce PostgreSQL, EF Core, migrations, persistence, retention, and database integration tests after approval.
