# Milestone 4 — Monitoring API

Status: implemented and locally verified. Dashboard work remains M5 and requires approval.

## Contract decisions

- Keep cached `GET /api/services` and `/api/services/{id}` compatible. Add `/api/services/{id}/statistics`, `/history`, `/incidents`, and `/api/overview` as database-backed reads.
- Statistics use fixed trailing 24-hour, seven-day, and 30-day windows, with one captured UTC end. Observed availability is successful completed checks / all completed checks. No observations means null, never 100%.
- Coverage uses interval-sized nominal slots anchored to each enabled configuration period. Count every slot intersecting the requested enabled window, including partial edge slots; a slot is observed if at least one check starts inside that window/period/slot. Duplicate samples cannot inflate coverage. Disabled and pre-enrollment time has no expected slots. Missing data never enters the uptime denominator. Fixed-delay scheduling can drift across nominal slot boundaries; coverage measures sampling density, not a time-based SLA.
- History accepts offset-bearing ISO-8601 `from`/`to`, a maximum 30-day range inside retention, and optional `bucketMinutes` in 1, 5, 15, 60, 360, 1440. Omitted buckets auto-select the smallest interval giving at most 1,000 points. Default range is the trailing 24 hours. Buckets are anchored at `from`, with a clipped final bucket. Empty points remain present; timing aggregates use only actual HTTP responses, not DNS/connection failures.
- Incidents open at the configured failure threshold, retaining the pending streak's first failure and the confirmation time. Success closes them. Gaps/disabled monitoring remain explicit, not recovery. URL/status-expectation changes end the prior incident as `configuration_changed`. Old M3 incidents are not fabricated; an existing failed M3 snapshot seeds only the latest known failure time.
- Incident pages use positive `beforeId` and `limit` 1–100 (default 20), descending ID order, and a next cursor. Current duration includes any explicitly flagged gaps and is not a claim of continuous downtime.
- Bad queries return 400 Problem Details; unknown/unconfigured IDs return 404; unavailable storage returns sanitized 503 for database-backed reads. Reads never trigger monitoring. Database reads are limited to four concurrent requests and 60 requests/minute, with no queue, returning 429 on rejection.
- OpenAPI JSON is served at `/openapi/v1.json`, with no dashboard/UI dependency. SQL aggregates are parameterized and performed in PostgreSQL; multi-window reads use repeatable-read transactions for a consistent history snapshot.
- Local migration requires a logical backup first. No production/NUC operation is included.

## Run and inspect

The SDK remains 10.0.403 / runtime 10.0.14. Microsoft.AspNetCore.OpenApi 10.0.14 is the new runtime dependency; the official package/release feeds were checked. From the repository root, after stopping the previous API in its own terminal:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet tool restore
python3 scripts/dev-db.py up
python3 scripts/dev-db.py migrate
python3 scripts/dev-db.py run
```

The migration helper now creates a private custom-format `pg_dump` backup in `.local/backups` first. A failed backup prevents migration. `python3 scripts/dev-db.py backup` makes a standalone backup. Files are mode 0600 and ignored by Git; they contain private monitoring data. Repeated migration commands also create backups, so manage their retention deliberately. A readable archive inventory is not a completed restore drill; production restore procedures remain M6 work.

M4 applied the additive local migration after backup and retained existing history. The user's process on port 5080 was preserved, so **restart it to load the new routes**. Do not start a second monitoring instance against the same database.

Use an ID from `/api/services`; this checkout's existing target uses `iltapaska`:

```sh
curl --fail http://127.0.0.1:5080/api/services
curl --fail http://127.0.0.1:5080/api/services/iltapaska/statistics
curl --fail http://127.0.0.1:5080/api/services/iltapaska/history
curl --fail 'http://127.0.0.1:5080/api/services/iltapaska/incidents?limit=20'
curl --fail http://127.0.0.1:5080/api/overview
curl --fail http://127.0.0.1:5080/openapi/v1.json
```

These are read-only routes. No CRUD, check-trigger endpoint, or dashboard was added. Keep the local API on loopback; resource limits do not provide authentication.

## Statistics and history details

Statistics return `serviceId`, `asOfUtc`, three `windows` (`24h`, `7d`, `30d`), and nullable `currentIncident`. Windows are elapsed UTC durations of 24, 168, and 720 hours, not local calendar days. Durably saved checks are selected by request-start timestamp in `[from, to)`.

Each window reports completed/successful/failed counts, `observedAvailabilityPercent`, expected/observed/missing opportunities, and `coveragePercent`. Percentages retain backend precision. Slow matching responses count as successful; even the first failed check reduces availability before DOWN is confirmed. Failed database writes do not create invented samples.

For example, two successful samples in two of eight nominal slots mean 100% observed availability and 25% coverage. That does not establish eight minutes of continuous uptime. Two samples in the same slot both enter the availability denominator but fill only one coverage opportunity.

History `from`/`to` require seconds and either `Z` or an explicit offset; inputs normalize to UTC. Default end is now and default start is one day before the end. The range must be ordered, at most 30 days long, within raw-history retention, and not in the future. Encode `+` offsets in URLs, for example using curl's `--data-urlencode`.

Automatic bucket selection gives at most 1,000 points: typically five minutes for 24 hours, 15 minutes for seven days, and 60 minutes for 30 days. Explicit `bucketMinutes` must be one of 1, 5, 15, 60, 360, or 1440 and still meet the point limit.

History returns the resolved range/bucket size and `points`. Each point has a clipped range, enabled seconds, completed/successful/failed counts, HTTP response count, and nullable mean/min/max response-header duration. Unexpected HTTP statuses have response timings; DNS/TLS/connection failures without a response do not. Empty buckets remain with null timings.

Point `state` is `observed` when any result exists, `missing` for enabled time with no result, or `not_monitored` when there is no enabled time or result. An observed bucket does not imply complete coverage inside it. This endpoint provides chart aggregates, not raw-row export.

PostgreSQL performs grouping through parameterized `SqlQuery` calls. No unbounded raw-result list crosses into the application. Multi-query reads use repeatable-read transactions for consistent historical data. Overview combines that with captured current cached health; it does not claim one atomic instant across cache and database. No large-data load benchmark has been performed.

## Incidents and compatibility

`MonitoredService.FirstFailureAtUtc` preserves the pending streak across restart. Reaching the threshold inserts an incident with that time and its confirmation time. A partial unique index enforces one open incident per service. The history row, snapshot/streak, and incident transition share one EF transaction before publication to memory.

A success closes an incident as `recovered`, even if its latency leaves current health DEGRADED. A stale gap does not close it. Disabled or removed monitoring preserves it and flags uncertainty. Re-enabling the same target can later confirm recovery. URL/status-expectation changes end it as `configuration_changed`, explicitly not a recovery claim. Other monitoring-setting changes flag gaps without asserting recovery.

`hasMonitoringGaps` records stale gaps and configuration interruptions; reads also flag currently stale or clock-uncertain open incidents. Shorter missing slots remain visible in coverage. `durationSeconds` spans the tracked first failure to its end or request's `asOfUtc`, clamped at zero for clock anomalies. It includes flagged gaps and is not continuous confirmed downtime.

The upgrade does not fabricate historical incidents. An already failed M3 snapshot seeds only its latest known failure timestamp, which can understate an older streak; new tracked streaks retain their first failure. Existing check history is preserved.

Incident pages accept positive `beforeId` and `limit` 1–100, default 20. They return newest IDs first and `nextBeforeId`, null on the last page. New insertions do not shift older pages. Entries include ID, first failure, confirmation/end, nullable resolution, gap flag, and duration. Unconfigured/retired IDs remain unavailable through current API routes.

Closed incidents expire strictly 365 days after their end, in bounded batches. Open incidents never expire. Raw-history retention remains 90 days by default.

## Overview, errors, and documentation

Overview returns current health/counts and configured services with windows/current incidents. Enabled-service precedence is DOWN, DEGRADED, UNKNOWN, UP; no enabled service means UNKNOWN. Disabled services appear separately and do not reduce enabled-service health.

Cached list/detail responses stay compatible. New database-backed reads return sanitized 503 when storage is unready or unavailable, including wrapped EF/Npgsql transient exceptions. Cancellation reaches SQL and releases its transaction/concurrency permit. Unrelated programming faults remain generic 500 Problem Details.

Bad queries return 400 Problem Details without echoing values; unknown IDs return 404. Analytics routes share four concurrent requests and a fixed 60-request/minute limit, with no queue; rejection returns 429 Problem Details. Liveness and cached snapshots are outside that limiter. Range/bucket/page bounds and the database command timeout limit individual work.

OpenAPI JSON describes route/query contracts, DTOs, limits, and error responses at `/openapi/v1.json`. It does not expose configuration/entity URLs. No Swagger UI or other frontend dependency was introduced.

## Test and verification record — 2026-10-27

- **198 cases passed:** 144 unit and 54 integration cases, none skipped; 30 integration cases use real PostgreSQL.
- Locked restore, Release build (zero warnings/errors), migration/model consistency, formatting, JSON/syntax, documentation links, and Git whitespace checks passed.
- NuGet reported no known vulnerable direct/transitive dependencies from the configured advisory source at verification time; future advisories may change that result.
- Release publishing succeeded and excluded private local settings and generated database credentials. Staged files were also checked against those generated values.
- Existing native liveness returned 200, while new OpenAPI/statistics routes returned 404 on the preserved old process, confirming the need to restart it. No second native monitor was started.

```sh
dotnet test Uptime-Urpo.slnx --no-restore
dotnet test tests/UptimeUrpo.IntegrationTests --no-build --no-restore --filter 'FullyQualifiedName~Analytics|FullyQualifiedName~Incident|FullyQualifiedName~Upgrade'
```

Tests use disposable PostgreSQL, controlled clocks, and fake outbound HTTP. They cover distinct-slot/sample denominators, duplicates, no observations, disabled time, interval changes, boundaries, gap buckets/timing, incident lifecycle/restart/gaps/configuration, rollback, uniqueness, retention, stable pagination, validation, sanitized errors, OpenAPI, actual SQL cancellation, concurrency rejection, and rate limits.

The local database's logical backup passed archive-header, permission, and readable table-inventory checks. The additive migration was applied successfully; both migration IDs and retained history were confirmed through SQL. The running user API was preserved; native verification of new routes awaits restart. Full HTTP behavior was verified through ASP.NET Core's in-memory server backed by real disposable PostgreSQL. No NUC, public deployment, frontend, or physical-device testing occurred.

References: [ASP.NET Core OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0), [EF SQL query parameters](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries), [built-in rate limiters](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

Read [tutorial Part 5](../tutorial.md#part-5--turn-history-into-a-useful-read-only-api) and [the M4 C# notes](../CSHARP-FOR-ROOPE.md#milestone-4--query-contracts-and-durable-incidents). M5 is the React dashboard and requires approval.
