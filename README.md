# Uptime-Urpo

A C#/.NET learning and portfolio project building a private, self-hosted uptime monitor for a small set of websites owned by the operator. Development proceeds in explicitly approved milestones.

## Current status

**Milestone 2 complete: background monitoring.** The application checks configured services automatically, limits concurrent work, and reports UP, DOWN, DEGRADED, or UNKNOWN. It includes `/health`, `GET /api/services`, and service-detail snapshots, with in-memory state and unit/API/TLS tests. See [the M2 walkthrough and verification record](docs/MILESTONE-2.md).

PostgreSQL, durable history, incidents, uptime statistics, the dashboard, deployment, and CI remain planned. Results and failure streaks are lost on restart. Nothing has been installed on or deployed to the NUC. See [the roadmap](docs/ROADMAP.md).

Milestone 3 requires approval before PostgreSQL work begins. Each later milestone has the same gate.

## Planned application

One ASP.NET Core application hosts a read-only API and background HTTP checker. PostgreSQL will store check results and incidents. A React dashboard will show current health, observed availability, monitoring coverage, response-time history, and incidents.

The first deployment will be private. Targets will be defined in private operator configuration; there will be no public URL submission or administration endpoints. The production application will serve the frontend and API from the same origin.

The selected stack is .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql 10, PostgreSQL 18, and later React / TypeScript / Vite. Testing will use xUnit, ASP.NET Core integration testing, and disposable PostgreSQL containers. Exact package versions will be verified when their milestone introduces them.

## Start here

For a guided walkthrough you can also use to teach the project, read [the tutorial](tutorial.md): Parts 1–2 explain setup and the first API; Part 3 follows the current worker, health states, and controlled-time tests.

1. Follow [local development setup](docs/LOCAL-DEVELOPMENT.md).
2. Read [architecture and operational decisions](docs/ARCHITECTURE.md).
3. Read [C# for Roope](CSHARP-FOR-ROOPE.md) before Milestone 1.
4. Review [milestones and acceptance criteria](docs/ROADMAP.md).

With the SDK available in your shell, build and test:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet build Uptime-Urpo.slnx --no-restore
dotnet test Uptime-Urpo.slnx --no-build --no-restore
dotnet run --project backend/UptimeUrpo.Api --no-build --no-launch-profile
```

The API binds to `http://127.0.0.1:5080` by default. In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/api/services
```

Committed defaults contain no targets and send no requests. Use the ignored local configuration to add owned services; the existing M1 `Monitoring:Target` shape still works. A detail response is available at `/api/services/{id}` using an ID returned by the list endpoint. See [configuration, defaults, and migration](docs/MILESTONE-2.md#configuration).

Docker is not needed for M1–M2 builds or tests. The [prerequisite script](scripts/check-prerequisites.sh) still checks the Docker readiness established in M0; a stopped engine does not block this milestone. Database tests in M3 will require Docker.

## Portfolio claims and limitations

Implemented and locally verified: a Minimal API, recurring multi-target HTTP checking, bounded concurrency, configuration validation, DNS/IP connection policy, cancellation/error handling, health transitions, stale results, and 126 test cases. Two live scheduled checks of the approved owned target returned HTTP 200. Persistence, uptime calculations, UI, and deployment remain planned. The API is a local development application, with no built-in authentication; keep it on loopback. Future milestone records must distinguish implemented, locally verified, deployed, and operationally verified behavior.

A monitor running on the same NUC as its targets cannot independently report NUC power loss, network loss, or its own failure. Missing monitoring data must stay visible as gaps. A successful HTTP response proves only that the configured expectation was met, not that every application feature works. An external observer can complement this project later.

No credentials, real private target configuration, access-layer policies, or private infrastructure addresses belong in the repository.
