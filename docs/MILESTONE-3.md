# Milestone 3 — PostgreSQL persistence

Historical M3 record: completed before M4 approval. M4 now adds incidents, query APIs, OpenAPI, and automatic local backups before migration. See [the current M4 guide](MILESTONE-4.md); the descriptions below record the earlier stage.

## What changed

The application now requires PostgreSQL. A check is saved before its observation is published to the in-memory API snapshot. Startup synchronizes configuration periods and restores the last durable snapshot. There is no production in-memory fallback, automatic startup migration, database retry queue, or history HTTP endpoint.

The three tables introduced here are:

- `monitored_services`: current configuration and a durable latest snapshot/failure streak.
- `monitoring_periods`: historical URL/settings/enabled periods, with at most one open period per service.
- `check_results`: raw observations, linked to a service and configuration period, indexed by service/request-start time and by completion time.

Incidents and their transitions remain M4 work. We deliberately avoid creating an unused incident table before that behavior exists.

## Start locally

Docker Desktop must be running. From the repository root:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet tool restore
python3 scripts/dev-db.py up
python3 scripts/dev-db.py migrate
python3 scripts/dev-db.py run
```

`up` generates random credentials once in ignored `.local/database.env` with file mode 0600, then starts only the `uptime-urpo-dev` Compose project. The database is exposed only on `127.0.0.1:54330`, not all network interfaces. Its named volume is `uptime-urpo-dev_database`; PostgreSQL 18 stores data under the mounted `/var/lib/postgresql` directory.

`migrate` supplies the migration-role connection string through the child process environment and runs the explicit application command `--migrate=true`. That command applies EF migrations and exits without starting monitoring. The helper then removes the runtime role's write access to the EF migration-history table. Repeating the command applies only outstanding migrations.

`run` supplies the runtime-role connection string through the environment. It does not print credentials or rewrite the existing private target settings. The default API listener remains `http://127.0.0.1:5080`. Ctrl+C stops a foreground run. A plain `dotnet run` now needs its own valid `ConnectionStrings__Monitoring`; the helper is the recommended local route.

In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/health/ready
curl --fail http://127.0.0.1:5080/api/services
```

Committed configuration still contains no targets. Existing ignored M1/M2 target configuration is preserved and supported. With an enabled owned target, the worker saves new checks normally. To inspect local database status or stop it without deleting data:

```sh
python3 scripts/dev-db.py status
python3 scripts/dev-db.py down
```

`down` does not remove the named volume or credential file. Keep them together. Generating new passwords does not change roles inside an already initialized volume. Never delete the credential file to fix authentication; preserve it and diagnose the mismatch. The helper has no volume-reset command. Manually removing this project's volume with `docker compose down --volumes` would permanently delete its local monitoring history; that is outside routine shutdown and was not done here.

`UPTIME_DB_PORT` can select a different local port, but use the same value for `up`, `migrate`, and `run`. `ASPNETCORE_URLS=http://127.0.0.1:5081 python3 scripts/dev-db.py run` selects another loopback API port. Do not use a LAN/public listener before the planned authenticated deployment boundary exists.

## Credentials and database permissions

The local database has separate identities:

- `postgres`: bootstrap administrator, used only inside the local database setup commands.
- `uptime_urpo_migrator`: owns the application database/schema objects and applies migrations.
- `uptime_urpo_runtime`: can connect and perform required application-table DML/sequence operations, but cannot create schema objects, roles, or databases. It can read, but not write, EF's migration-history table after the migration helper completes.

Neither application role is a superuser. Future production migration credentials must remain separate from runtime credentials; this local helper is not a production deployment system. `.local/database.env`, real connection strings, backups, and private target settings must not be committed or shared.

The runtime reads `ConnectionStrings:Monitoring`; there is no committed password. Optional `Database` settings are `RetentionDays` (30–365, default 90) and `RetentionBatchSize` (1–5,000, default 500). Missing/invalid database configuration fails startup with a sanitized validation message.

## EF Core, migrations, and compatible versions

EF Core, its relational package, the design package, and local `dotnet-ef` tool are pinned to 10.0.14. Npgsql's EF provider is 10.0.3 and declares a compatible EF 10 range. Explicit EF pins prevent the design dependency and provider minimum from selecting conflicting relational assembly patches. Testcontainers.PostgreSql 4.16.0 is test-only. Lockfiles record the resolved graph; build and real database tests verified it.

`MonitoringDbContext` maps ordinary C# entity classes. Each persistence operation gets a scoped context; simultaneous checks never share one. Sensitive-data logging is not enabled, and framework SQL/connection log categories are suppressed in normal application logging. Our boundaries log sanitized exception types instead of database/host details.

The checked-in `InitialMonitoring` migration contains the schema; its designer/model snapshot describes the EF model, not actual database contents. For future approved schema changes:

```sh
dotnet tool restore
dotnet ef migrations add DescriptiveChangeName --project backend/UptimeUrpo.Api --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project backend/UptimeUrpo.Api
```

These are authoring/review commands, not instructions to add a second migration during M3. Inspect generated SQL before application. The migration's `Down` method drops application tables; it is not a data-preserving rollback. Production backup-before-migration and restore procedures remain M6 work.

References: [Npgsql EF 10 compatibility](https://www.npgsql.org/efcore/release-notes/10.0.html), [Microsoft migration guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), [Testcontainers PostgreSQL module](https://dotnet.testcontainers.org/modules/postgres/).

## Save, restore, and database failures

The worker prepares an observation without changing shared state, then creates an async service scope and saves through `MonitoringStore`. One EF `SaveChangesAsync` transaction inserts the history row and updates the durable latest snapshot/failure streak. Only after that succeeds does the worker publish the prepared observation to memory.

If saving fails, the old published snapshot stays unchanged. Readiness becomes unhealthy and the unsaved observation is discarded; the next normal interval can recover. There is no unbounded queue or immediate replay. As usual, an interrupted connection can make commit acknowledgement uncertain; the monitor does not automatically retry the same insert or invent a result. Initialization failures retry every five seconds and prevent target requests until schema/configuration synchronization succeeds.

At restart, configuration is synchronized and the durable latest observations are loaded. UTC completion times bridge process lifetimes; fresh observations reconstruct a monotonic age baseline. Old or future-dated observations restore as UNKNOWN with a reset streak. Within the new process, freshness again uses monotonic elapsed time. Cross-process freshness necessarily depends on reasonably correct UTC clocks.

State restoration preserves a fresh failure streak, so restarting does not automatically clear repeated confirmed failures. It does not claim monitoring continued while the process was stopped. A long restart gap becomes UNKNOWN and breaks the streak. Keep exactly one monitoring application instance against a database; distributed coordination remains out of scope.

The latest snapshot is stored separately from raw history. Retention may delete all old raw rows while preserving the last known snapshot for display; that snapshot still becomes UNKNOWN according to freshness rules.

## Configuration periods and history queries

The stable service ID identifies its history. Display-name changes update only the current name. Changes to URL, enabled state, interval, timeout, status expectation, or health thresholds close the old period and open another, clearing incompatible current state. Disabled periods are recorded explicitly. Removing a target retires it and closes its period without deleting history; reintroducing it opens a new period. IDs must not be casually reused for unrelated services.

Configuration remains operator-managed and fixed until restart. Periods record settings observed at startup, not an invented exact timestamp for an edit made while the application was stopped. The last enabled period remains open across ordinary shutdown: that gap represents missing monitoring opportunities for future coverage calculations.

`MonitoringStore.ReadHistoryAsync` demonstrates an indexed, no-tracking LINQ query over `[start, end)` request-start timestamps, newest first, capped at 1,000 results. It is used by database tests; no `/history` endpoint is exposed yet. PostgreSQL timestamps use `timestamp with time zone` and application DTOs remain UTC.

## Retention and readiness

`RetentionWorker` attempts one bounded delete batch per minute, outside API requests. The default deletes raw rows whose completion is strictly older than 90 days, at most 500 per pass. Rows exactly at the cutoff remain. Current snapshots, service identity, and configuration periods are retained. Incident retention has no implementation until incidents arrive in M4.

`GET /health` stays process liveness. `GET /health/ready` returns 200 `{"status":"ready"}` only after initialization, with no unresolved latest storage-write failure, and when the schema/database read succeeds. Otherwise it returns 503 `{"status":"not_ready"}` without diagnostics. A target HTTP 500 does not fail readiness if its result was saved correctly.

Retention failures produce a safe error log and retry at the next pass. Inspect logs and database status; a SELECT-only readiness probe does not prove every maintenance permission. Ordinary check writes/retention and migration credentials remain deliberately separate.

## Verification commands

```sh
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet test Uptime-Urpo.slnx --no-restore
dotnet test tests/UptimeUrpo.IntegrationTests --no-build --no-restore --filter 'Category=Database'
# Fast unit/API/TLS checks without PostgreSQL containers:
dotnet test Uptime-Urpo.slnx --no-build --no-restore --filter 'Category!=Database'
```

The full suite requires a reachable Docker engine and the PostgreSQL image. Database tests create a disposable PostgreSQL 18.6 container with a loopback-only random port and a separate database per scenario. The fixture uses migration and restricted runtime roles. It does not use the local development volume or real monitoring targets and fails explicitly if Docker is unavailable.

## Verification record — 2026-10-26

- M2 was reviewed, retested (126 cases), and committed locally as `6b47c09` before this milestone.
- The official release feed changed since M0/M2. M3 updated the pin to SDK 10.0.403/runtime 10.0.14, verified the ARM64 archive checksum, preserved the older SDK, and repeated validation on the servicing update.
- M3 tests passed: 105 unit cases and 33 integration cases, **138 total**, including nine real PostgreSQL scenarios. None were skipped.
- Locked restore, local-tool restore, Release build (zero warnings/errors), EF migration/model consistency, formatting, shell/Python syntax, documentation links, and Git whitespace checks passed. NuGet reported no known vulnerable direct/transitive packages at verification time; this is not a promise about future advisories.
- Release publishing excluded local settings and generated database passwords. A missing-credential protection check refused to generate a replacement file for the existing volume. Private files remain ignored by Git.
- Database scenarios covered empty-database/repeated migrations, model/migration consistency, runtime DDL denial, durable ordered/bounded UTC history, atomic rollback, configuration periods/retirement, bounded retention/cutoff, snapshot and failure-streak restoration, write-permission failure/recovery, and unreachable storage with live API/no target requests.
- The local helper created an isolated database volume and private credentials, applied migrations, and confirmed runtime/migration roles are not superusers and cannot create roles/databases. Port 54330 is bound only to loopback. Existing unrelated local database services were not changed.
- A native local helper run before the SDK servicing update returned ready, performed one approved owned-target HTTP 200 / UP check (about 180 ms), and saved one history row confirmed through SQL after shutdown. The API logged graceful shutdown; its smoke process was stopped afterward. The full automated suite, including real PostgreSQL and local TLS scenarios, was subsequently repeated successfully on SDK 10.0.403/runtime 10.0.14.

The local development database is left running for continued use; `python3 scripts/dev-db.py down` stops it while preserving data. No NUC, Cloudflare, production database, or public deployment was touched. No push was performed.

Read [tutorial Part 4](../tutorial.md#part-4--keep-monitoring-history-in-postgresql) and [the M3 C# notes](../CSHARP-FOR-ROOPE.md#milestone-3--entities-contexts-and-transactions) for the teaching walkthrough. M4 adds the read-only history/uptime/incident API only after approval.
