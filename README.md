# Uptime-Urpo

A C#/.NET learning and portfolio project building a private, self-hosted uptime monitor for a small set of websites owned by the operator. Development proceeds in explicitly approved milestones.

## Current status

**Milestone 7 delivery verified: GitHub-validated artifact on the private NUC.** The application serves its dashboard/API in one non-root container alongside an isolated PostgreSQL container. Backup-before-migration releases, daily backup scheduling, a separate-database restore drill, restart continuity, and SSH-only access are implemented and verified. The current image is `7ba4406`, promoted from the verified GitHub artifact. See [M7 evidence](docs/MILESTONE-7.md), [M6 operations](docs/MILESTONE-6.md), and [deployment/recovery commands](docs/DEPLOYMENT.md).

To open the deployed dashboard, keep an SSH tunnel running:

```sh
ssh -N -L 127.0.0.1:5081:127.0.0.1:8108 roope@nuc
```

Open **http://127.0.0.1:5081/**. This uses the NUC application; Vite and a local API are unnecessary. The existing SSH alias needs the explicit `roope` account. No public hostname or Cloudflare rule was added. Daily backup scheduling is installed; off-host copies are currently manual.

Manual hosted validation, candidate preparation, exact-artifact promotion, history continuity, private access, and a restore of the new backup into a separate database all passed. A separate push run exposed a startup-test race; its test-only fix awaits a fresh hosted result before final M7 acceptance. The selected route keeps CI on GitHub and deployment on this Mac over SSH. See [CI and delivery](docs/CI-AND-DELIVERY.md) and [M7 evidence](docs/MILESTONE-7.md). [The roadmap](docs/ROADMAP.md) keeps later work distinct.

## Application

One ASP.NET Core application hosts a read-only API and background HTTP checker. PostgreSQL stores results, current snapshots, configuration periods, and incidents. The React dashboard presents current health, history, coverage, and incidents.

The first deployment is private through an authenticated SSH tunnel. Targets are defined in private operator configuration; there are no public URL submission or administration endpoints. The production application serves the frontend and API from the same origin.

The selected stack is .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql 10, PostgreSQL 18, and React / TypeScript / Vite. Tests use xUnit, ASP.NET Core integration testing, and disposable PostgreSQL containers. Package versions and container digests are pinned in the repository.

## Start here

For a walkthrough you can also use to teach the project, read [the tutorial](tutorial.md): Parts 1–4 cover the API, worker, and persistence; Part 5 follows query contracts, coverage, and incidents; Part 6 builds the dashboard; Part 7 deploys it privately and teaches backup/recovery; Part 8 adds hosted validation, verified delivery, and recovery gates.

1. Follow [local development setup](docs/LOCAL-DEVELOPMENT.md).
2. Read [architecture and operational decisions](docs/ARCHITECTURE.md).
3. Read [C# for Roope](CSHARP-FOR-ROOPE.md) before Milestone 1.
4. Review [milestones and acceptance criteria](docs/ROADMAP.md).

For native local development, with Docker Desktop running and the SDK available:

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

Implemented and locally verified: secure monitoring, durable PostgreSQL results/incidents, retention, availability/coverage, bounded APIs/OpenAPI, the React dashboard, and container/recovery tooling. Deployed and operationally checked: native AMD64 readiness, real target checks, loopback/SSH access, database isolation, graceful restart continuity, backup restoration into a separate database, and an off-host recovery copy. These checks do not establish a long-running production reliability history.

Current verification: 199 backend cases (144 unit, 55 integration), 25 frontend tests, and 24 operations/artifact/gate tests; production image builds and real container/recovery checks. Physical-device touch, a full host reboot, full main-database replacement, an unattended daily backup, and automatic off-host replication remain unverified or unimplemented as detailed in [M6](docs/MILESTONE-6.md). The API has no built-in authentication; keep its origin private and run one monitoring instance per database.

A monitor running on the same NUC as its targets cannot independently report NUC power loss, network loss, or its own failure. Missing monitoring data must stay visible as gaps. A successful HTTP response proves only that the configured expectation was met, not that every application feature works. An external observer can complement this project later.

No credentials, real private target configuration, access-layer policies, or private infrastructure addresses belong in the repository.
