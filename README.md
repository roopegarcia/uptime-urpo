# Uptime-Urpo

A C#/.NET learning and portfolio project building a private, self-hosted uptime monitor for a small set of websites owned by the operator. Development proceeds in explicitly approved milestones.

## Current status

**Milestone 1 complete: first C# API.** The application performs one configured startup HTTP check, stores the observation in memory, and serves a safe JSON response. It includes `/health`, `GET /api/services/{id}`, outbound destination protection, and unit/API/TLS tests. See [the M1 walkthrough and verification record](docs/MILESTONE-1.md).

Scheduled monitoring, health thresholds, PostgreSQL, incident history, the dashboard, deployment, and CI remain planned. The startup result is lost on restart and never refreshed within that process. Nothing has been installed on or deployed to the NUC. See [the roadmap](docs/ROADMAP.md).

Milestone 2 requires approval before background monitoring begins. Each later milestone has the same gate.

## Planned application

One ASP.NET Core application will host a read-only API and background HTTP checker. PostgreSQL will store check results and incidents. A React dashboard will show current health, observed availability, monitoring coverage, response-time history, and incidents.

The first deployment will be private. Targets will be defined in private operator configuration; there will be no public URL submission or administration endpoints. The production application will serve the frontend and API from the same origin.

The selected stack is .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql 10, PostgreSQL 18, and later React / TypeScript / Vite. Testing will use xUnit, ASP.NET Core integration testing, and disposable PostgreSQL containers. Exact package versions will be verified when their milestone introduces them.

## Start here

For a guided walkthrough you can also use to teach the project, read [the tutorial](tutorial.md): Part 1 explains setup; Part 2 follows the actual API code.

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

The API binds to `http://127.0.0.1:5080` by default. In another terminal, run `curl http://127.0.0.1:5080/health`. The service-detail ID comes from your configuration. Committed defaults use `example` with monitoring disabled; this development checkout has an ignored local target configured for the approved live check. See [configuration and sample requests](docs/MILESTONE-1.md).

Docker is not needed for M1 builds or tests. The [prerequisite script](scripts/check-prerequisites.sh) still checks the Docker readiness established in M0; a stopped engine does not block this milestone. Database tests in M3 will require Docker.

## Portfolio claims and limitations

Implemented and locally verified: a Minimal API, one-shot HTTP checking, configuration validation, DNS/IP connection policy, cancellation/error handling, saved-result DTOs, and 84 test cases. One live startup check of the approved owned target returned the expected HTTP 200. Persistence, uptime calculations, scheduling, UI, and deployment remain planned. The API is a local development application, with no built-in authentication; do not expose it beyond loopback. Future milestone records must distinguish implemented, locally verified, deployed, and operationally verified behavior.

A monitor running on the same NUC as its targets cannot independently report NUC power loss, network loss, or its own failure. Missing monitoring data must stay visible as gaps. A successful HTTP response proves only that the configured expectation was met, not that every application feature works. An external observer can complement this project later.

No credentials, real private target configuration, access-layer policies, or private infrastructure addresses belong in the repository.
