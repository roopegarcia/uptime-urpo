# Uptime-Urpo

A C#/.NET learning and portfolio project building a private, self-hosted uptime monitor for a small set of websites owned by the operator. Development proceeds in explicitly approved milestones.

## Current status

**Milestone 5 complete: the React dashboard.** Overview, service details, interactive response charts, incidents, availability/coverage, themes, and responsive controls now use the read-only monitoring API. See [the M5 walkthrough and evidence](docs/MILESTONE-5.md).

Deployment and CI remain planned. Nothing has been installed on or deployed to the NUC. [Milestone 6](docs/ROADMAP.md#m6--docker-and-nuc-operations) needs approval and an inspection of the actual host/access setup first.

## Planned application

One ASP.NET Core application hosts a read-only API and background HTTP checker. PostgreSQL stores results, current snapshots, configuration periods, and incidents. The React dashboard presents current health, history, coverage, and incidents.

The first deployment will be private. Targets will be defined in private operator configuration; there will be no public URL submission or administration endpoints. The production application will serve the frontend and API from the same origin.

The selected stack is .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql 10, PostgreSQL 18, and React / TypeScript / Vite. Testing will use xUnit, ASP.NET Core integration testing, and disposable PostgreSQL containers. Exact package versions will be verified when their milestone introduces them.

## Start here

For a walkthrough you can also use to teach the project, read [the tutorial](tutorial.md): Parts 1–4 cover the API, worker, and persistence; Part 5 follows query contracts, coverage, and incidents; Part 6 builds and teaches the dashboard.

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

Start the UI in a second terminal from the repository root (Node 26.9.0 / npm 12.2.0 were verified):

```sh
npm --prefix frontend ci
npm --prefix frontend run dev
```

Open **http://127.0.0.1:5173/**. Vite proxies reads to the running API. For same-origin serving, build with `npm --prefix frontend run build`, then restart the API and open port 5080. [M5 explains builds, publishing, and UI verification](docs/MILESTONE-5.md).

Committed defaults contain no targets and send no requests. Use the ignored local configuration to add owned services; the existing M1 `Monitoring:Target` shape still works. A detail response is available at `/api/services/{id}` using an ID returned by the list endpoint. See [target configuration](docs/MILESTONE-2.md#configuration) and [database setup](docs/MILESTONE-3.md#start-locally).

Each service also has `/statistics`, `/history`, and `/incidents` subroutes. Statistics distinguish sample availability from missing coverage; history returns chart buckets with explicit gaps. See [parameters and formulas](docs/MILESTONE-4.md). The running local API was verified through Vite in M5. Restart it to load newly built backend/static-serving changes.

Run all tests with `dotnet test Uptime-Urpo.slnx --no-restore`; Docker is required for disposable PostgreSQL tests. Use `--filter 'Category!=Database'` for fast unit/API/TLS tests without database containers. Tests never use the development database or public targets. A normal application run requires a database connection; the local helper supplies it through the environment.

Local database credentials live in ignored `.local/database.env`; preserve this file with the named volume. `python3 scripts/dev-db.py down` stops only this project's database and keeps the volume. The local database binds only to `127.0.0.1:54330`. Migration and runtime identities are separate; no real credentials belong in Git.

## Portfolio claims and limitations

Implemented and locally verified: secure monitoring, durable PostgreSQL results/incidents, retention, observed availability/coverage, validated read APIs, OpenAPI, and the React dashboard. Real PostgreSQL tests cover rollback, restart, incidents, statistics, and HTTP boundaries. Chrome checks used real local data and isolated synthetic failure scenarios, with desktop/mobile viewport layouts and keyboard/pointer chart controls. Physical-device touch and deployment remain unverified. The API has no built-in authentication; keep it on loopback and run one monitoring instance per database.

Current verification: 25 frontend tests and 199 backend cases (144 unit, 55 integration), frontend production build, and backend Release publish. See the dated [M5 record](docs/MILESTONE-5.md#verification-record--2026-10-27) for limits and commands.

A monitor running on the same NUC as its targets cannot independently report NUC power loss, network loss, or its own failure. Missing monitoring data must stay visible as gaps. A successful HTTP response proves only that the configured expectation was met, not that every application feature works. An external observer can complement this project later.

No credentials, real private target configuration, access-layer policies, or private infrastructure addresses belong in the repository.
