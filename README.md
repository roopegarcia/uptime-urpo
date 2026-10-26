# Uptime-Urpo

A C#/.NET learning and portfolio project building a private, self-hosted uptime monitor for a small set of websites owned by the operator. Development proceeds in explicitly approved milestones.

## Current status

**Milestone 3 complete: PostgreSQL persistence.** The application checks configured services automatically, stores observations and current state in PostgreSQL, and restores snapshots after restart. It includes health/freshness rules, bounded retention, configuration periods, liveness/readiness, and unit/API/TLS/database tests. See [the M3 walkthrough and verification record](docs/MILESTONE-3.md).

The history HTTP API, incidents, uptime statistics, dashboard, deployment, and CI remain planned. Nothing has been installed on or deployed to the NUC. See [the roadmap](docs/ROADMAP.md).

Milestone 4 requires approval before the monitoring API expansion begins. Each later milestone has the same gate.

## Planned application

One ASP.NET Core application hosts a read-only API and background HTTP checker. PostgreSQL stores results, current snapshots, and configuration periods; incidents will follow. A React dashboard will show current health, observed availability, monitoring coverage, response-time history, and incidents.

The first deployment will be private. Targets will be defined in private operator configuration; there will be no public URL submission or administration endpoints. The production application will serve the frontend and API from the same origin.

The selected stack is .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql 10, PostgreSQL 18, and later React / TypeScript / Vite. Testing will use xUnit, ASP.NET Core integration testing, and disposable PostgreSQL containers. Exact package versions will be verified when their milestone introduces them.

## Start here

For a guided walkthrough you can also use to teach the project, read [the tutorial](tutorial.md): Parts 1–3 explain the first API and worker; Part 4 follows EF Core, PostgreSQL, migrations, and durable state.

1. Follow [local development setup](docs/LOCAL-DEVELOPMENT.md).
2. Read [architecture and operational decisions](docs/ARCHITECTURE.md).
3. Read [C# for Roope](CSHARP-FOR-ROOPE.md) before Milestone 1.
4. Review [milestones and acceptance criteria](docs/ROADMAP.md).

With Docker Desktop running and the SDK available, start locally:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet tool restore
python3 scripts/dev-db.py up
python3 scripts/dev-db.py migrate
python3 scripts/dev-db.py run
```

The API binds to `http://127.0.0.1:5080` by default. In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/health/ready
curl --fail http://127.0.0.1:5080/api/services
```

Committed defaults contain no targets and send no requests. Use the ignored local configuration to add owned services; the existing M1 `Monitoring:Target` shape still works. A detail response is available at `/api/services/{id}` using an ID returned by the list endpoint. See [target configuration](docs/MILESTONE-2.md#configuration) and [database setup](docs/MILESTONE-3.md#start-locally).

Run all tests with `dotnet test Uptime-Urpo.slnx --no-restore`; Docker is required for disposable PostgreSQL tests. Use `--filter 'Category!=Database'` for fast unit/API/TLS tests without database containers. Tests never use the development database or public targets. A normal application run requires a database connection; the local helper supplies it through the environment.

Local database credentials live in ignored `.local/database.env`; preserve this file with the named volume. `python3 scripts/dev-db.py down` stops only this project's database and keeps the volume. The local database binds only to `127.0.0.1:54330`. Migration and runtime identities are separate; no real credentials belong in Git.

## Portfolio claims and limitations

Implemented and locally verified: a Minimal API, secure recurring checks, bounded concurrency, health states, EF Core migrations, durable history/snapshots, retention, database readiness, and 138 test cases. Local PostgreSQL tests proved restart continuity, transactional rollback, and write-failure recovery. One M3 live owned-target check returned HTTP 200 and was confirmed saved through SQL. Uptime calculations, incident/history HTTP APIs, UI, and deployment remain planned. The API has no built-in authentication; keep it on loopback and run only one monitoring instance per database.

A monitor running on the same NUC as its targets cannot independently report NUC power loss, network loss, or its own failure. Missing monitoring data must stay visible as gaps. A successful HTTP response proves only that the configured expectation was met, not that every application feature works. An external observer can complement this project later.

No credentials, real private target configuration, access-layer policies, or private infrastructure addresses belong in the repository.
