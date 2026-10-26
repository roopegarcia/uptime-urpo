# Milestone 1 — First C# API

Historical M1 record: implemented and locally verified before M2 approval. **The current code now uses recurring checks and additional response fields.** Use [the M2 guide](MILESTONE-2.md) for current behavior and configuration. The commands/results below document the earlier stage; the one-shot service and its test names have since been replaced.

## What was built

One ASP.NET Core Minimal API project, an xUnit unit-test project, and an API/TLS integration-test project. No scheduler, database, UI, OpenAPI tooling, or deployment was added.

The host validates one target configuration, performs at most one startup check if enabled, and stores its result in a singleton in memory. `StartupCheckService` is an `IHostedService` startup hook, not a background loop. The server starts listening after that check finishes or reaches its configured timeout. A normal target failure produces a saved failed observation and still lets the API start. Invalid configuration fails startup before any check.

`/health` reports application liveness. `GET /api/services/{id}` returns safe display fields and the saved observation; it never triggers a new check. A failed observation is not yet a DOWN state. Failure thresholds, freshness, and scheduling belong to M2.

## Run locally

From the repository root:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet build Uptime-Urpo.slnx --no-restore
dotnet test Uptime-Urpo.slnx --no-build --no-restore
dotnet run --project backend/UptimeUrpo.Api --no-build --no-launch-profile
```

The default listener is `http://127.0.0.1:5080`. Do not override it to expose this development API to a public or LAN interface: authentication and the deployment access boundary are not implemented yet. Stop a foreground run with Ctrl+C.

In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
# Replace example with the configured target ID when using local settings.
curl --fail http://127.0.0.1:5080/api/services/example
```

A clean checkout has monitoring disabled and target ID `example`. This development checkout has an ignored local target with ID `iltapaska`, so use `/api/services/iltapaska` after a normal local run.

For a predictable disabled demonstration even when local settings exist, run this instead of the normal run command:

```sh
Monitoring__Target__Enabled=false Monitoring__Target__Id=example Monitoring__Target__Name="Example service" \
  dotnet run --project backend/UptimeUrpo.Api --no-build --no-launch-profile
```

Then `/api/services/example` returns:

```json
{
  "id": "example",
  "name": "Example service",
  "enabled": false,
  "lastCheck": null
}
```

## Configure one owned target

Committed `appsettings.json` contains safe disabled defaults. To create private local settings without overwriting an existing file:

```sh
if [ ! -f backend/UptimeUrpo.Api/appsettings.Local.json ]; then
  cp backend/UptimeUrpo.Api/appsettings.Local.example.json backend/UptimeUrpo.Api/appsettings.Local.json
fi
chmod 600 backend/UptimeUrpo.Api/appsettings.Local.json
```

Edit that local file. It uses this shape (sanitized example; disabled until deliberately enabled):

```json
{
  "Monitoring": {
    "Target": {
      "Id": "example",
      "Name": "Example service",
      "Url": "https://example.com/",
      "Enabled": false,
      "ExpectedStatusCode": 200,
      "TimeoutSeconds": 10
    }
  }
}
```

Set the URL to an owned, approved target before enabling it. Every application start with monitoring enabled performs a check. Refreshing an API response performs none. Settings are fixed for that process; restart to apply changes. Avoid rapid repeated restarts against real services.

Configuration precedence, highest first: command-line values, environment variables, `appsettings.Local.json`, then ASP.NET Core's standard configuration sources including committed settings. The application explicitly loads the local file; this filename has no automatic meaning to ASP.NET Core. Environment names use double underscores, such as `Monitoring__Target__Enabled`.

The target ID must be 1–64 lowercase letters/digits separated by single hyphens. The display name must be nonblank, at most 80 characters, and contain no control characters. URLs must use HTTPS, a DNS hostname with a dot, and port 443. Literal IPs, localhost forms, trailing-dot hostnames, credentials, query strings, and fragments are unsupported. Expected final HTTP status is 200–599, and timeout is 1–30 seconds. These restrictions are deliberate M1 defaults; widening them needs review.

Local settings are ignored by Git and excluded from build/publish output. No configuration API exists. There are no credentials or bypass tokens in the sample or local smoke-check configuration.

## HTTP contracts

`GET /health` returns HTTP 200 and `{"status":"ok"}`. It does not initiate a check and does not turn unhealthy because a target failed.

`GET /api/services/{id}` returns HTTP 200 for the configured target, even if its last check failed. Its fields are `id`, `name`, `enabled`, and nullable `lastCheck`. A completed check contains:

- `checkedAtUtc`: UTC request-start timestamp.
- `durationMs`: elapsed time from checker start until response headers or failure, including DNS/connection/TLS work; excludes body download.
- `statusCode`: the actual HTTP status, or null when no response arrived.
- `success`: whether the actual status exactly matched the expectation.
- `failureCategory`: null on success; otherwise `unexpected_status`, `dns`, `connection`, `tls`, `timeout`, `blocked_destination`, `invalid_response`, or `network`.

Unknown IDs return 404 Problem Details. The response never includes target URLs, resolved addresses, response bodies, or exception messages. HTTP 200 on this endpoint means the API successfully returned a snapshot, not that the target passed its check.

The snapshot never refreshes in M1. Its timestamp remains unchanged on repeated reads. Restarting loses the old observation and, if enabled, performs a new check. No uptime percentages or incident claims can be made from this milestone.

## Outbound boundary

Only the configured HTTPS hostname/port is permitted. A new connection resolves that host, rejects an empty or mixed/non-public answer set, then connects directly to an approved IP. There is no second DNS lookup between validation and connection. `SocketsHttpHandler` retains the original HTTPS hostname for SNI and certificate validation.

The IP policy conservatively excludes special-purpose IPv4 ranges and allows only normal global IPv6 space after excluding special/transition ranges. Mapped IPv6 forms are rejected. Some publicly reachable special-purpose exceptions are intentionally unsupported. Review this maintained policy as IANA registries evolve; it is not a general-purpose public URL validator.

Redirects, implicit proxies, and cookies are disabled. No body is downloaded or drained on disposal. Response headers are limited to 16 KiB, request time is bounded, and there are no immediate retries. Trying another already-validated IP after a connection failure is address fallback for the same check, not a second scheduled check. Standard certificate validation stays enabled.

Normal failures are logged by sanitized category without exception text. The HTTP client's default URL-bearing loggers are removed. Unexpected programming faults still fail visibly rather than being disguised as target failures.

## Tests and what they prove

Unit tests cover exact status expectations, failure categorization, body avoidance/disposal, timeout cancellation, shutdown cancellation, unsafe URL forms, address policy, allowlist enforcement, mixed DNS answers, address fallback, IP pinning, and revalidation on a later connection.

API tests use `WebApplicationFactory` with fake outbound responses and explicit test settings. They verify real routing, dependency injection, startup behavior, JSON, disabled targets, 404 Problem Details, configuration validation, sanitized failure responses, and repeated reads without extra requests. They override ignored local settings, so your real target is not used by tests.

TLS tests use an ephemeral loopback server and a temporary test certificate authority trusted only by that test handler. They verify original hostname/SNI, rejection of wrong-host and untrusted certificates, and a redirect remaining unfollowed. Test certificates are not installed in your OS trust store. Only test wiring maps a validated example destination onto the loopback fixture; production has no bypass flag.

Tests require the .NET SDK and local loopback sockets, not Docker, PostgreSQL, or public internet. Initial package restore and the vulnerability lookup require network access.

## Verification record — 2026-10-25

- .NET SDK 10.0.402 / runtime 10.0.13, macOS ARM64.
- Locked restore passed; Debug build passed with zero warnings/errors.
- 71 unit cases and 13 integration cases passed: **84 total**, none skipped.
- NuGet's vulnerability lookup reported no known vulnerable direct/transitive packages from the configured source at verification time. This is not a guarantee about future advisories.
- One live startup check of the approved `https://iltapaska.com/` target returned HTTP 200 with `success: true`, approximately 180 ms to response headers. The result was an external smoke check, not an automated test or a production uptime measurement.
- Local liveness returned 200. Repeated detail reads returned the same saved result; there was one startup-check log entry. Unknown ID returned 404 Problem Details.
- A separate disabled run confirmed environment configuration overrides local settings and performed no target check. The direct application process handled SIGTERM with a graceful shutdown log and exit code 0. The initial `dotnet run` wrapper used for the live probe did not provide graceful-shutdown evidence; only the separate direct-process check establishes it.
- Release publish output was inspected for exclusion of local/development settings. This was an artifact check, not production deployment work.

No NUC service, database, Cloudflare setting, or access policy was changed. No public API was deployed, and no remote or commit was created. Verification processes were stopped afterward; use the run command to start the API yourself. Docker was stopped during this milestone and was not needed.

## What was learned and what is next

Follow [tutorial Part 2](../tutorial.md#part-2--build-the-first-c-api) and [the M1 C# notes](../CSHARP-FOR-ROOPE.md#milestone-1--follow-the-real-code) for the teaching walkthrough.

You can now follow configuration → dependency injection → startup hook → asynchronous HTTP → result record → endpoint JSON, and explain why a target failure is different from API liveness.

M2 was subsequently approved and adds the background loop, multiple targets, bounded concurrency, health thresholds, freshness, and deterministic scheduling tests. See [the M2 record](MILESTONE-2.md).
