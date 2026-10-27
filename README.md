# Uptime-Urpo

A C#/.NET learning and portfolio project building a private, self-hosted uptime monitor for a small set of websites owned by the operator. Development proceeds in explicitly approved milestones.

## Current status

**Milestone 4 complete: monitoring API.** The application includes bounded history, observed availability/coverage, durable incidents, an overview, request limits, and OpenAPI JSON. Existing secure monitoring, PostgreSQL snapshots, and readiness remain in place. See [the M4 contract and verification guide](docs/MILESTONE-4.md).

The dashboard, deployment, and CI remain planned. Nothing has been installed on or deployed to the NUC. See [the roadmap](docs/ROADMAP.md).

Milestone 5 requires approval before the React dashboard begins. Each later milestone has the same gate.

## Planned application

One ASP.NET Core application hosts a read-only API and background HTTP checker. PostgreSQL stores results, current snapshots, configuration periods, and incidents. A React dashboard will present these capabilities in M5.

The first deployment will be private. Targets will be defined in private operator configuration; there will be no public URL submission or administration endpoints. The production application will serve the frontend and API from the same origin.

The selected stack is .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql 10, PostgreSQL 18, and later React / TypeScript / Vite. Testing will use xUnit, ASP.NET Core integration testing, and disposable PostgreSQL containers. Exact package versions will be verified when their milestone introduces them.

## Start here

For a walkthrough you can also use to teach the project, read [the tutorial](tutorial.md): Parts 1–4 cover the API, worker, and persistence; Part 5 follows query contracts, coverage, and incidents.

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
python3 scripts/dev-db.py migrate # Creates a private local backup first.
python3 scripts/dev-db.py run
```

The API binds to `http://127.0.0.1:5080` by default. In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/health/ready
curl --fail http://127.0.0.1:5080/api/services
curl --fail http://127.0.0.1:5080/api/overview
curl --fail http://127.0.0.1:5080/openapi/v1.json
```

Committed defaults contain no targets and send no requests. Use the ignored local configuration to add owned services; the existing M1 `Monitoring:Target` shape still works. A detail response is available at `/api/services/{id}` using an ID returned by the list endpoint. See [target configuration](docs/MILESTONE-2.md#configuration) and [database setup](docs/MILESTONE-3.md#start-locally).

Each service also has `/statistics`, `/history`, and `/incidents` subroutes. Statistics distinguish sample availability from missing coverage; history returns chart buckets with explicit gaps. See [parameters and formulas](docs/MILESTONE-4.md). Restart an older API process to load these routes; the existing process on port 5080 was preserved during this milestone.

Run all tests with `dotnet test Uptime-Urpo.slnx --no-restore`; Docker is required for disposable PostgreSQL tests. Use `--filter 'Category!=Database'` for fast unit/API/TLS tests without database containers. Tests never use the development database or public targets. A normal application run requires a database connection; the local helper supplies it through the environment.

Local database credentials live in ignored `.local/database.env`; preserve this file with the named volume. `python3 scripts/dev-db.py down` stops only this project's database and keeps the volume. The local database binds only to `127.0.0.1:54330`. Migration and runtime identities are separate; no real credentials belong in Git.

## Portfolio claims and limitations

Implemented and locally verified: secure monitoring, durable PostgreSQL results/incidents, retention, observed availability/coverage, validated read APIs, OpenAPI, cancellation, and resource limits. Real PostgreSQL tests exercise rollback, restart, incidents, statistics, and HTTP boundaries. Local schema was backed up and migrated while preserving history. New native routes await restart of the existing API; no deployment or UI/device validation occurred. The API has no built-in authentication; keep it on loopback and run one monitoring instance per database.

Current verification: 198 passing cases (144 unit, 54 integration), zero-warning Release build, and a migration model matching the checked-in schema changes.

A monitor running on the same NUC as its targets cannot independently report NUC power loss, network loss, or its own failure. Missing monitoring data must stay visible as gaps. A successful HTTP response proves only that the configured expectation was met, not that every application feature works. An external observer can complement this project later.

No credentials, real private target configuration, access-layer policies, or private infrastructure addresses belong in the repository.
