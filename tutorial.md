# Building Uptime-Urpo: learn it, then teach it

Uptime-Urpo is a C#/.NET learning project: a private uptime monitor for websites owned by its operator. This tutorial follows the project as it is built, explaining what changed, why it changed, and how to verify it.

Each part corresponds to one milestone. **Parts 1–7 are complete. Parts 8–9 are planned.** The monitoring backend now has a React dashboard. Earlier parts record earlier stages; Part 5 teaches the read API, and Part 6 connects it to a real interface. Part 7 records the private NUC deployment, operational checks, and recovery limits.

Use this tutorial for the step-by-step story. Use [C# for Roope](CSHARP-FOR-ROOPE.md) for language explanations, [the architecture](docs/ARCHITECTURE.md) for decisions, and [the roadmap](docs/ROADMAP.md) for acceptance criteria.

## Part 1 — Give the project a foundation

**Milestone 0 · Complete**

### What we built

We prepared the development environment and documented how the application will work. The repository now has:

- A README explaining the purpose and current status.
- An architecture document describing the planned components and their responsibilities.
- A roadmap dividing the work into small, verifiable milestones.
- A local setup guide with installation and verification instructions.
- A C# learning guide that connects new concepts to JavaScript, TypeScript, and PHP.
- `global.json` to select the .NET SDK.
- A prerequisite-check script and `.gitignore`.

On the development Mac, we installed the ARM64 .NET SDK in `~/.dotnet`, verified the downloaded archive's checksum, started Docker Desktop, and checked that the engine responded. No NUC services were changed.

This part produced a reproducible starting point. At the end of Part 1 there was no API, scheduler, database, or dashboard; Part 2 adds the first API.

### Step 1: Understand the problem before choosing classes

An uptime monitor periodically sends a request to a website and compares the response with an expectation. For example, receiving HTTP 500 when HTTP 200 was expected is a failed check, even though the server replied.

Our planned flow is:

```text
Scheduled check → HTTP request → evaluate response → save result
Private dashboard → read API → saved results
```

The scheduler decides **when** to check. The checker performs the request. Persistence keeps the evidence. The API and dashboard present that evidence.

These responsibilities will live in one ASP.NET Core application initially. A few owned websites do not require separate worker services or a message queue. Keeping the flow in one backend project also makes it easier to read and explain.

**Explain it to someone else:** “We separated responsibilities in the code, while keeping deployment simple with one application and one database.”

### Step 2: Separate C#, .NET, and ASP.NET Core

These names refer to different pieces:

- **C#** is the programming language used to express the application logic.
- **.NET** provides the runtime and libraries that execute and support the program.
- **The .NET SDK** includes the compiler and tools used to create, build, and test projects.
- **ASP.NET Core** provides web application features such as routing, HTTP handling, dependency injection, and hosting.

As a rough comparison, JavaScript is a language, Node.js is a runtime, and Express supplies web application features. The boundaries are not identical, but the comparison helps explain why installing only a runtime is insufficient for development.

We installed the SDK because this machine needs to compile and test C# code. The production container will eventually need the application runtime, rather than the full development SDK.

**Check your understanding:** If a machine can execute an existing .NET application, does that guarantee it can compile one? No: it might have only a runtime installed.

### Step 3: Understand the SDK selection

Open [global.json](global.json):

The example below records M0's initial pin. M3 later updates the actual file to SDK 10.0.403/runtime 10.0.14; use the current setup guide when installing tooling.

```json
{
  "sdk": {
    "version": "10.0.402",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

This file tells the .NET CLI which installed SDK to select when commands run inside this repository. It does not install that SDK.

`latestPatch` allows installed servicing patches within the selected SDK feature band. It does not silently switch the project to a different major release or SDK feature band. `allowPrerelease: false` excludes preview SDKs from selection.

The SDK number is different from the runtime number. In our setup, SDK `10.0.402` includes .NET and ASP.NET Core runtime `10.0.13`.

**Explain it to someone else:** “The SDK pin makes tool selection predictable; we still need to install and maintain the tool itself.”

### Step 4: Make the installed SDK available to your shell

In a terminal opened at the repository root, run:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
dotnet --info
dotnet --list-runtimes
```

`DOTNET_ROOT` points to the user-local installation. Adding that directory to `PATH` lets the shell find the `dotnet` executable. These exports apply to the current shell session; they do not edit your shell profile.

On the verified M0 setup, `dotnet --version` selected `10.0.402`, and the runtime list included `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` version `10.0.13`. Later servicing updates may change the reported patch versions.

If the command is missing or cannot resolve the requested SDK, follow [the setup guide](docs/LOCAL-DEVELOPMENT.md). Do not create an application just to check whether the SDK is installed.

### Step 5: Distinguish Docker's command-line tools from its engine

Run:

```sh
docker --version
docker compose version
docker info --format '{{.OSType}} {{.Architecture}}'
```

The first two commands check command-line tooling. The third contacts the engine that actually runs containers. Having a `docker` command does not guarantee that the engine is available.

On this Mac, Docker Desktop supplies a Linux engine. If the engine is stopped, start Docker Desktop and wait for it to finish starting:

```sh
open -a Docker
```

Then rerun the engine check. At M0 verification, it reported `linux aarch64`, matching Linux containers on the Apple Silicon development machine. The Intel NUC has a different architecture, so production compatibility needs its own verification later.

We have not created a project container. Docker becomes necessary for the database work in Part 4; checking it now removes a future setup surprise.

### Step 6: Run the repository's prerequisite check

```sh
sh scripts/check-prerequisites.sh
```

Read [the script](scripts/check-prerequisites.sh) alongside its output. It:

1. Locates the repository root so SDK selection uses this project's `global.json`.
2. Finds `dotnet` on PATH, through `DOTNET_ROOT`, or in `~/.dotnet`.
3. Checks SDK selection and lists runtimes.
4. Checks Docker CLI, Compose, and engine availability.
5. Reports Node/npm availability for the later frontend milestone.
6. Exits unsuccessfully if a required prerequisite is missing.

The prerequisite script's current success line is:

```text
PASS: development prerequisites available. Application tests must be run separately.
```

This is an environment check. It does not establish that an API, database query, or deployment works.

### Step 7: Learn the repository boundaries

The current files describe and verify the starting point. The architecture document shows future directories, but those directories will be created when their milestone needs them.

Read [.gitignore](.gitignore). It excludes generated output, local configuration, secrets, and backups. Ignoring a file helps prevent accidental commits; it does not validate whether other files contain sensitive information. Review changes before staging them.

Read the first five concepts in [C# for Roope](CSHARP-FOR-ROOPE.md): types and nullability; classes, records, and interfaces; async tasks and cancellation; dependency injection; and the application host/request pipeline. Its M0 snippets are introductory previews; the M1 section now links the actual implementation.

### Step 8: Explain what monitoring can prove

Before writing the checker, practise these distinctions:

- A matching HTTP response establishes that the configured response expectation was met. It does not test every feature of the website.
- One failed request is evidence of a failed check. Repeated failures will be required before the service becomes DOWN.
- A missing check is unknown data. It must not automatically become a success or a failure.
- A monitor on the same NUC cannot independently report that the NUC lost power or connectivity.

These limitations will shape the implementation. The full security and reliability decisions are in [the architecture document](docs/ARCHITECTURE.md).

### What was verified

During M0, we verified the SDK archive checksum, SDK selection and architecture, runtime availability, and Docker engine connectivity. We also checked shell syntax, local documentation links, and whitespace.

The dated evidence is in [the setup guide's verification record](docs/LOCAL-DEVELOPMENT.md#milestone-0-verification-record--2026-10-25). Those results describe that verification session; rerun the prerequisite script to check your current environment.

There were no application tests or deployment checks in Part 1 because the application did not exist at that stage.

### Teach this part

For a short lesson, walk someone through these five actions:

1. State the monitoring problem and draw the two flows from Step 1.
2. Explain the difference between language, SDK, runtime, and web framework.
3. Open `global.json` and explain selection versus installation.
4. Run the prerequisite script and interpret its output.
5. Identify one thing the setup proves and one thing it does not prove.

Ask the learner:

- Why can the Docker CLI work while containers cannot run? **The engine may be stopped or unreachable.**
- Does `global.json` download an SDK? **No; it selects a compatible installed SDK.**
- Is a missing monitoring result confirmed downtime? **No; it is missing evidence.**
- What application feature did Part 1 deliver? **None yet; it delivered the environment and documented design needed to begin responsibly.**

**Ready for Part 2:** the prerequisite check passes, the component responsibilities are understandable, and Milestone 1 has received approval. Completing this tutorial does not itself authorize the next milestone.

## Part 2 — Build the first C# API

**Milestone 1 · Complete**

This part records the one-shot API built in M1. M2 has since replaced the startup hook and added recurring checks and response fields. Read Part 3 for current behavior; the commands and examples here illustrate the earlier stage.

### What we built

We added an ASP.NET Core application that performs one configured startup HTTP check and returns its saved observation through a read-only API. We also added tests for HTTP evaluation, cancellation, destination security, API behavior, and TLS.

The actual flow is:

```text
Program.cs registers dependencies
  → host calls StartupCheckService.StartAsync
  → HttpChecker sends one request using IHttpClientFactory
  → PublicHttpsConnector validates DNS answers and opens the connection
  → StartupCheckState saves a CheckResult
  → GET /api/services/{id} returns the saved result
```

There is no timer, background loop, database, or dashboard. Refreshing the API response does not recheck the website. Restarting loses the observation and, if enabled, performs a new check.

### What you need first

Have the .NET SDK selected by `global.json` available. Work from this existing checkout; the commands below do not ask you to recreate its projects. Docker is not needed for these tests. Public internet is needed for initial dependency restore and an explicitly enabled live target check, but the automated tests use controlled responses and local sockets.

### Step 1: Recognize the solution and projects

Open [the solution](Uptime-Urpo.slnx). It groups three projects:

- `backend/UptimeUrpo.Api`: the running application.
- `tests/UptimeUrpo.UnitTests`: isolated checker and destination-policy tests.
- `tests/UptimeUrpo.IntegrationTests`: API pipeline and local TLS tests.

For teaching the setup, these were the initial creation commands. **They are historical steps, not commands to rerun over this checkout:**

```sh
dotnet new sln --name Uptime-Urpo --format slnx
dotnet new web --name UptimeUrpo.Api --output backend/UptimeUrpo.Api --framework net10.0 --no-restore --exclude-launch-settings
dotnet new xunit --name UptimeUrpo.UnitTests --output tests/UptimeUrpo.UnitTests --framework net10.0 --no-restore
dotnet new xunit --name UptimeUrpo.IntegrationTests --output tests/UptimeUrpo.IntegrationTests --framework net10.0 --no-restore
```

The generated Hello World route and empty tests were then replaced with the implementation below. The solution includes all three projects; both test projects reference the API project. The test project files pin their packages and `packages.lock.json` files record the resolved dependencies. We removed the unused template coverage collector instead of introducing tooling we do not use yet.

### Step 2: Build and run the tests before exploring the API

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet build Uptime-Urpo.slnx --no-restore
dotnet test Uptime-Urpo.slnx --no-build --no-restore
```

Restore obtains the locked dependencies; build compiles the C#; test executes the test projects. A lockfile mismatch should be investigated rather than fixed by casually deleting the lockfile. Build artifacts appear in ignored `bin` and `obj` directories.

At M1 verification, the build had zero warnings/errors and all 84 cases passed: 71 unit cases and 13 integration cases. Several tests are data-driven cases of the same behavior, rather than 84 separate test methods.

### Step 3: Follow `Program.cs`

Read [Program.cs](backend/UptimeUrpo.Api/Program.cs) from top to bottom. It creates the builder, adds local configuration, registers dependencies, builds the application, and maps two routes.

This resembles an Express entry point that wires configuration, services, middleware, and handlers. The ASP.NET Core host also owns startup/shutdown and invokes the one-shot hosted service.

The default listener is `http://127.0.0.1:5080`. That is local development access; we have not implemented authentication or the NUC deployment access layer. Keep the listener on loopback.

### Step 4: Read configuration as data with rules

Open [TargetOptions](backend/UptimeUrpo.Api/Monitoring/TargetOptions.cs) and [the sanitized example](backend/UptimeUrpo.Api/appsettings.Local.example.json). The settings select one target's ID, display name, URL, enabled flag, expected status, and timeout.

`IOptions<TargetOptions>` supplies a typed object to application services. Startup validation rejects unsafe or unsupported settings before any request. The initial restrictions include HTTPS on port 443, a DNS hostname, no URL credentials/query/fragment, and a timeout from 1 to 30 seconds.

Committed settings leave monitoring disabled. Private `appsettings.Local.json` is explicitly loaded, ignored by Git, and excluded from build/publish output. Environment variables override it, and command-line values override those. See [the complete configuration instructions](docs/MILESTONE-1.md#configure-one-owned-target).

For a deterministic teaching demonstration, start the API with checking disabled:

```sh
Monitoring__Target__Enabled=false Monitoring__Target__Id=example Monitoring__Target__Name="Example service" \
  dotnet run --project backend/UptimeUrpo.Api --no-build --no-launch-profile
```

Keep that terminal running and use another terminal for the next steps. The environment overrides work even if this checkout has an enabled local target.

### Step 5: Ask whether the API itself responds

```sh
curl --fail http://127.0.0.1:5080/health
```

Expected response:

```json
{"status":"ok"}
```

This is process liveness. It does not perform a target check. An unavailable website does not mean the monitor itself is unavailable.

### Step 6: Inspect a structured service response

```sh
curl --fail http://127.0.0.1:5080/api/services/example
```

In the disabled demonstration, this returns:

```json
{
  "id": "example",
  "name": "Example service",
  "enabled": false,
  "lastCheck": null
}
```

`null` means no check result exists. It must not be presented as confirmed success or failure. An unknown service ID returns 404 Problem Details; try `curl -i http://127.0.0.1:5080/api/services/missing` to inspect the status and response type.

### Step 7: Follow the checker's dependencies

Read [HttpChecker](backend/UptimeUrpo.Api/Monitoring/HttpChecker.cs). Its constructor receives the HTTP client factory, typed options, and time provider. `Program.cs` registers them, and the framework constructs the object. This is dependency injection rather than importing and constructing every dependency inside the checker.

`CheckAsync` returns `Task<CheckResult>`, comparable to `Promise<CheckResult>`. It awaits an asynchronous GET and evaluates the exact expected status. HTTP 204 is a failure when 200 was expected; a deliberate 401 expectation can succeed, but proves only that the authentication boundary responds.

The actual [result record](backend/UptimeUrpo.Api/Monitoring/CheckResult.cs) contains timestamp, duration, nullable status, success, and nullable failure category. `int?` is needed because a DNS failure produces no HTTP status. A failure category is an enum, serialized as a safe string such as `dns` or `unexpected_status`.

The client waits only for response headers. The duration includes DNS, TCP, and TLS work where needed; it is not full page-load time. Responses are disposed, and response draining is disabled to avoid downloading bodies during cleanup.

### Step 8: Distinguish timeout, shutdown, and target failure

The checker links the host cancellation token with its timeout. If the timeout expires, it returns a failed observation with `timeout`. If the host is shutting down, it rethrows cancellation rather than recording a website failure.

Expected network exceptions become sanitized categories. Their raw messages, destination addresses, and response bodies do not become API fields. `using` declarations release resources when the scope exits, including on exceptions; think of reliable `try/finally` cleanup.

Read `TimeoutCancelsTheRequestAndBecomesAFailedObservation` and `ShutdownCancellationPropagatesInsteadOfBecomingAResult` in [the checker tests](tests/UptimeUrpo.UnitTests/HttpCheckerTests.cs) to see the distinction exercised.

### Step 9: Understand the connection safety boundary

Read [PublicHttpsConnector](backend/UptimeUrpo.Api/Monitoring/PublicHttpsConnector.cs) and [the address policy](backend/UptimeUrpo.Api/Monitoring/PublicAddressPolicy.cs). The connector permits only the configured host and port, resolves its addresses, rejects non-public or mixed answers, and connects directly to one of the validated IPs.

This avoids validating one DNS answer and then independently resolving again during connection. The request still uses the original hostname, so the HTTP handler performs normal TLS hostname/certificate checks. Redirects, proxies, and cookies are disabled.

The internal test constructor accepts a connection function (`Func<...>`). Like passing a function in TypeScript, this lets a test record the selected endpoint without making a real public connection. It is not a production bypass setting. [The TLS tests](tests/UptimeUrpo.IntegrationTests/HttpsTransportTests.cs) additionally exercise real loopback TLS with a trust root that exists only in the test handler.

### Step 10: Explain one-shot startup versus scheduled monitoring

M1's `StartupCheckService` implemented `IHostedService.StartAsync`. If enabled, it awaited one check and saved the result in `StartupCheckState` before the server listened. M2 replaced those classes with [MonitoringWorker](backend/UptimeUrpo.Api/Monitoring/MonitoringWorker.cs) and [MonitoringState](backend/UptimeUrpo.Api/Monitoring/MonitoringState.cs); Part 3 explains how startup now remains responsive.

The endpoint reads that state. It does not call the checker. There is no recurring task, stale-state calculation, or failure threshold yet. A failed check is evidence about one request, not a DOWN incident.

### Step 11: Optional live demonstration

Stop the disabled demonstration with Ctrl+C. Follow [the private configuration instructions](docs/MILESTONE-1.md#configure-one-owned-target) and use an owned, approved URL before enabling checking. Then start normally:

```sh
dotnet run --project backend/UptimeUrpo.Api --no-build --no-launch-profile
```

Read `/api/services/{id}` using the configured ID. This checkout's local verification target uses `iltapaska`; a fresh checkout's default uses `example` and remains disabled. Read the result twice: `checkedAtUtc` and the other saved fields should remain unchanged.

A completed result includes `statusCode`, `success`, `durationMs`, and `failureCategory`. The API endpoint can return HTTP 200 while `lastCheck.success` is false: the endpoint successfully returned a failed observation. Do not change the expected status just to make an unexpected response look successful.

Our recorded live check of the approved owned target returned the expected HTTP 200 in approximately 180 ms. That is one historical observation, not a prediction for your run. Repeated restarts cause repeated checks, so use the disabled mode or tests for teaching whenever a live request is unnecessary.

### Step 12: Use the tests as executable explanations

Open [the API tests](tests/UptimeUrpo.IntegrationTests/ApiTests.cs). The current equivalent, `ReadsReturnTheSavedResultWithoutTriggeringMoreChecks`, makes two API reads between scheduled runs and asserts that reads cause no extra outbound call. `TargetFailureDoesNotFailApplicationLiveness` proves that a target 503 does not fail `/health`.

For a focused failure demonstration, run:

```sh
dotnet test tests/UptimeUrpo.UnitTests --no-build --no-restore --filter FullyQualifiedName~TimeoutCancelsTheRequestAndBecomesAFailedObservation
```

This creates a controlled request that waits until its timeout token is canceled; it does not contact a website. `[Fact]` marks one test, while `[Theory]` and `[InlineData]` describe one behavior across several inputs. Broader tests verify address restrictions, DNS rebinding, redirects, and real TLS failures locally.

### What was verified

The [M1 verification record](docs/MILESTONE-1.md#verification-record--2026-10-25) covers the build, 84 tests, package vulnerability lookup, local endpoint checks, one live target check, and a separate graceful direct-process shutdown check. Publish output was checked to ensure local settings were excluded. No server deployment occurred.

### Teach this part

Use disabled mode for a short teaching session:

1. Run `/health` and the service-detail request, and explain the different questions they answer.
2. Trace `Program.cs` → startup hook → checker → saved state → endpoint.
3. Open the result record and explain both nullable fields.
4. Run the focused timeout test and explain cancellation.
5. Show the repeated-read API test and ask why it asserts only one outbound call.

Ask the learner:

- Can the detail endpoint return 200 for a failed target check? **Yes; HTTP 200 describes the API operation, while the DTO describes the target observation.**
- Why does a DNS failure have no status code? **No HTTP response arrived.**
- Why use `ResponseHeadersRead`? **The monitor needs status and header timing, not the response body.**
- Why keep the hostname after validating an IP? **TLS still needs to verify the certificate for the requested hostname.**
- Does a single failed result open an incident? **No; incident/threshold behavior is not implemented yet.**

**What remains:** recurring checks, multiple targets, health state/freshness, persistence, incidents, uptime, and the dashboard. Part 3 requires approval before implementation.

## Part 3 — Run checks in the background

**Milestone 2 · Complete**

This part records the in-memory worker introduced in M2. M3 now requires PostgreSQL and saves results before publishing them. Use Part 4 for current startup commands; the scheduling/health concepts below still apply.

### What we built

The one-shot check is now a recurring monitor. Multiple configured services have independent intervals and timeouts. A shared limit bounds active checks, each service has one sequential loop, and current observations become UP, DOWN, DEGRADED, or UNKNOWN. The API includes a service list and additional health fields.

We also added controlled-time tests and kept the existing destination/TLS protections. Results and failure streaks remain in memory. There is no PostgreSQL, history chart, incident log, uptime percentage, or UI yet.

### What you need first

Use the existing checkout and .NET SDK. Build and test from the root as in Part 2. No Docker or public service is needed for automated tests. An enabled real target will receive recurring requests while the application runs.

### Step 1: Follow the lifecycle change

Read [MonitoringWorker](backend/UptimeUrpo.Api/Monitoring/MonitoringWorker.cs). It inherits `BackgroundService` and overrides `ExecuteAsync`. The method returns a task representing all its monitoring loops until shutdown.

In .NET 10, that work runs in the background without blocking the web host's startup. A first API response can therefore contain UNKNOWN with no completed observation. This is an intentional change from M1's startup wait.

Think of starting an async function in Node, retaining its promise, and awaiting it during shutdown. Here, the host owns the lifecycle. `Task.WhenAll` joins the per-target tasks, much like `Promise.all`, so the worker can wait for every loop to finish when cancelled.

### Step 2: Use the target list without breaking your current setup

Open [MonitoringOptions](backend/UptimeUrpo.Api/Monitoring/MonitoringOptions.cs) and [the example configuration](backend/UptimeUrpo.Api/appsettings.Local.example.json). New configuration uses `Monitoring:Targets`, an array of target objects, plus `MaxConcurrency`.

The M1 `Monitoring:Target` object still works. Its monitoring interval defaults to 60 seconds, timeout to 10 seconds, failure threshold to three, and slow-response threshold to 2,000 ms. We preserved the existing local settings, so you can continue using your working target without migration.

To add more services, move that old object into a `Targets` array and remove the old `Target` property. Give every service a unique ID. The application rejects conflicting old/new shapes instead of silently adding unexpected checks. The full sanitized two-service example and limits are in [the M2 configuration guide](docs/MILESTONE-2.md#configuration).

The committed configuration has no targets, so a clean checkout sends no requests. Disabled entries are visible but never checked. Restart after editing configuration; there is no live administration API.

### Step 3: Understand the interval precisely

Each loop performs these steps:

```text
Wait for a global slot
  → await one check
  → save the result and log safe fields
  → release the slot
  → await this target's interval
  → repeat until cancelled
```

The interval is a delay **after completion**. A 200-ms check with a 60-second interval starts roughly every 60.2 seconds when capacity is available. It is not a cron-style promise to run exactly on the minute.

Because the loop awaits its own request, it cannot overlap another check for that service. Because it creates the next delay after completion, it has no backlog of missed runs. After a long pause there can be one pending attempt per target, still governed by the global limit.

### Step 4: See how the concurrency gate works

The worker creates `new SemaphoreSlim(_options.MaxConcurrency)`. With the default three slots, three loops can hold a slot and a fourth must wait asynchronously.

Look at `WaitAsync(stoppingToken)`, `try`, and `finally` in `MonitorAsync`. A slot is always released after it was acquired, including on cancellation or an error. It is released before the interval delay so sleeping services do not block other services.

An async loop does not require a dedicated thread sitting idle for each target. Awaiting I/O or a delay yields until work can continue. A slow request can consume one slot, but other targets progress through the remaining slots.

### Step 5: Inspect the health rules

Read [MonitoringState](backend/UptimeUrpo.Api/Monitoring/MonitoringState.cs). Health is derived from the latest observation and its freshness:

- **UNKNOWN:** disabled, no completed result, or completion was more than twice the interval ago.
- **UP:** a fresh expected response at or below the slow threshold.
- **DEGRADED:** a fresh slow success, or failures below the DOWN threshold.
- **DOWN:** fresh consecutive failures reaching the threshold.

One success resets the streak, even if slow. A slow success recovers DOWN to DEGRADED. A stale gap breaks the failure streak. A single transient failure normally produces DEGRADED, not DOWN. The displayed failure count saturates at the configured threshold.

These are health snapshots, not incident records. UNKNOWN does not mean an outage recovered. A success that becomes stale stays visible as an old `lastCheck` while current health becomes UNKNOWN.

### Step 6: Learn why completion time differs from request time

`lastCheck.checkedAtUtc` says when the request started. `lastCompletedAtUtc` says when its outcome became available. Freshness uses elapsed time since completion.

The code uses UTC for display and monotonic timestamps for elapsed time. Adjusting the machine's wall clock must not make an observation artificially fresh or stale. A `TimeProvider` supplies both concepts, allowing tests to control time without waiting real minutes.

Timeouts must be shorter than intervals. Otherwise, a normal long timeout could make every observation stale before the following one completes, undermining the failure-streak rules. Startup validation catches that configuration rather than letting the monitor behave misleadingly.

### Step 7: Understand shared state and `lock`

The worker writes observations while API handlers read them. Those operations may run concurrently in .NET. `MonitoringState` uses a short `lock` around updates and snapshot creation so related fields stay consistent.

No network request or `await` happens inside that lock. The response is a separate record, rather than a mutable dictionary entry. This is different from the semaphore: the lock protects a tiny synchronous state operation, while the semaphore limits asynchronous network work.

### Step 8: Run the application and inspect all services

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
```

The list contains all configured services, including disabled ones. An empty list is correct when no targets are configured. Use an ID from the list to read `/api/services/{id}`; this checkout's preserved local target uses `iltapaska`.

Inspect `health`, `consecutiveFailures`, `lastCompletedAtUtc`, and `lastCheck`. With an enabled target, read again after the interval plus request time and the timestamp should advance. Reads themselves do not cause checks, but they can differ because the worker runs independently. Keep the local API on loopback; authentication and deployment access controls are later work.

### Step 9: Demonstrate failures without breaking a website

Run the API test that supplies controlled 503 responses followed by 200:

```sh
dotnet test tests/UptimeUrpo.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~ScheduledFailuresReachDownAndASuccessRecovers
```

Open [the test](tests/UptimeUrpo.IntegrationTests/ApiTests.cs). It waits for the first recorded check, advances the fake clock to subsequent intervals, observes DOWN after three failures, then supplies success and observes UP. The test makes no public requests and does not take down any real service.

Run scheduling and state tests separately:

```sh
dotnet test tests/UptimeUrpo.UnitTests --no-build --no-restore --filter FullyQualifiedName~MonitoringWorkerTests
dotnet test tests/UptimeUrpo.UnitTests --no-build --no-restore --filter FullyQualifiedName~MonitoringStateTests
```

[Worker tests](tests/UptimeUrpo.UnitTests/MonitoringWorkerTests.cs) hold fake requests open, inspect queued work, advance time, and release responses deliberately. [State tests](tests/UptimeUrpo.UnitTests/MonitoringStateTests.cs) cover thresholds, freshness boundaries, gap reset, and clock corrections. Small real waits coordinate task continuations; scenario time comes from the fake clock.

### Step 10: Explain graceful shutdown and recovery

The stopping token flows through semaphore waits, requests, and interval delays. Ctrl+C on a foreground run asks the host to cancel the worker, await all loops, and release resources. Host cancellation is not stored as a failed website check.

Normal HTTP/network failures produce sanitized results and the next interval runs normally. An unexpected internal checker error produces a safe error log and a gap rather than invented downtime; its loop can try again after the interval. If errors persist, old data becomes UNKNOWN. Logs must be investigated, not interpreted as proof that the target is DOWN.

Stop the process after a teaching session. State is deliberately lost on restart: history and restart continuity arrive with PostgreSQL in Part 4.

### What was verified

All 126 cases passed: 102 unit cases and 24 integration cases. A separate local process observed two scheduled HTTP 200 / UP results for the approved owned target, approximately a minute apart, and exited cleanly on SIGTERM. The existing local configuration was preserved and the smoke process was stopped.

See [the dated M2 record](docs/MILESTONE-2.md#verification-record--2026-10-25). This is local evidence, not a deployment claim. No NUC or database work occurred.

### Teach this part

For a short session, show the service-list JSON, trace the worker loop, then run the controlled failure/recovery test. Ask the learner:

- Why release the semaphore before sleeping? **A sleeping service should not consume a slot needed by another service.**
- What prevents overlap for one target? **Its next iteration cannot begin before its own awaited check and delay finish.**
- Why can a successful result show DEGRADED? **Availability and response speed are separate aspects of health.**
- Why can UNKNOWN contain a successful `lastCheck`? **The historical result exists, but it is no longer fresh evidence of current health.**
- Why does a wall-clock correction not change freshness? **Elapsed time is measured with monotonic timestamps.**
- Does graceful shutdown establish a website outage? **No; host cancellation is not a target observation.**

**What remains:** PostgreSQL, durable history, incidents, uptime calculations, and the dashboard. Part 4 requires approval before implementation.

## Part 4 — Keep monitoring history in PostgreSQL

**Milestone 3 · Complete**

This part records M3's persistence foundation. M4 extends the schema with incidents, adds HTTP query endpoints, and makes the local migration helper create a backup first. See Part 5 for current behavior.

### What we built

Checks now survive an application restart. PostgreSQL stores raw observations, the latest service snapshot/failure streak, and configuration periods. EF Core maps the data, a reviewed migration creates the schema, and a separate readiness endpoint reports storage availability.

We added local Compose tooling, restricted runtime credentials, bounded retention, and real PostgreSQL tests. We did not add a history HTTP endpoint, uptime calculation, incident tracking, or a UI. Those remain later parts.

The current flow is:

```text
Startup: verify schema → synchronize configuration → restore durable snapshots
Check: HTTP result → prepare observation → save history + snapshot → publish to memory
Read: API returns current snapshot; readiness checks storage separately
```

### What you need first

Use this checkout, the pinned .NET SDK, Python 3, and a running Docker engine. The database is local; no NUC access or paid service is needed. Keep your existing private target settings. Start from the repository root.

### Step 1: Restore the tools and start the isolated database

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet tool restore
python3 scripts/dev-db.py up
```

Read [compose.dev.yaml](compose.dev.yaml) and [the helper](scripts/dev-db.py). They operate only on the `uptime-urpo-dev` project. PostgreSQL 18.6 binds to `127.0.0.1:54330` and persists in a named volume. Random local passwords are generated once in ignored `.local/database.env`, not printed or committed.

Preserve that credential file with its volume. A new password file cannot reset credentials stored inside an existing database. The helper refuses to generate replacements if the volume already exists without its file. This protects your ability to diagnose and recover data rather than silently replacing credentials.

### Step 2: Apply the migration explicitly

```sh
python3 scripts/dev-db.py migrate
```

The helper uses the migration role to run the application's explicit `--migrate=true` command, applies outstanding migrations, then exits. It does not start the API or send target requests. Repeating it is safe when there are no pending migrations.

Normal API startup never automatically migrates. This keeps schema-changing permissions separate from normal monitoring. The helper removes runtime write access to EF's migration-history table after migration too.

### Step 3: Read the entities and context

Open [Entities.cs](backend/UptimeUrpo.Api/Persistence/Entities.cs) and [MonitoringDbContext](backend/UptimeUrpo.Api/Persistence/MonitoringDbContext.cs).

`MonitoredService` is the service identity/current snapshot. `MonitoringPeriod` records the settings active over a period. `CheckRecord` is one raw observation. Their C# properties map to SQL columns; nullable fields represent observations that do not exist yet or a request with no HTTP response.

The context maps relationships, lengths, indexes, and the nonnegative-duration constraint. It exposes `DbSet` collections, similar to an ORM session in a PHP or Node application. Its tracked objects are not API DTOs: URLs and storage fields stay behind the HTTP boundary.

### Step 4: Understand the migration files

Open the `InitialMonitoring` files under `backend/UptimeUrpo.Api/Persistence/Migrations` and [the model snapshot](backend/UptimeUrpo.Api/Persistence/Migrations/MonitoringDbContextModelSnapshot.cs). The migration creates tables and indexes, while the snapshot records EF's model for future comparisons.

The snapshot contains schema information, not your database's rows. The migration's `Down` drops tables; it cannot recover deleted history. Review generated changes as carefully as handwritten SQL migrations. Creating a migration and applying one are separate actions.

### Step 5: Follow one scoped save

Open [MonitoringWorker](backend/UptimeUrpo.Api/Monitoring/MonitoringWorker.cs), then [MonitoringStore](backend/UptimeUrpo.Api/Persistence/MonitoringStore.cs). For each observation, the worker creates an async DI scope and resolves the store. The scope gives it a separate `MonitoringDbContext` and disposes it after the operation.

This matters because a context is not thread-safe. Sharing one between parallel HTTP checks would mix tracked entities and concurrent queries. The connection pool can manage reusable database connections underneath; the context itself remains a short-lived unit of work.

### Step 6: See why the save comes before the snapshot update

`MonitoringState.Prepare` calculates the observation and next failure count without publishing them. The store inserts a history row and updates the durable latest snapshot in one `SaveChangesAsync` transaction. Only afterward does `Apply` publish the snapshot used by the API.

If inserting the history fails, the current database snapshot must not advance either. A real database test deliberately supplies a negative duration, triggers the SQL constraint, and verifies that both writes roll back. This is stronger evidence than a fake ORM store that simply accepts any object.

The latest snapshot duplicates a small set of result fields on purpose. It can survive raw-history retention and makes startup restoration simple. Atomic writes keep that duplication consistent.

### Step 7: Run with restricted runtime credentials

```sh
python3 scripts/dev-db.py run
```

The helper passes a runtime connection string through the child environment. That role can read/write application data but cannot create schema objects, databases, or roles. The migration role owns schema changes; neither application role is a superuser.

A plain `dotnet run` now needs a valid `ConnectionStrings__Monitoring` supplied separately. Use the helper for this local setup rather than putting a password into committed settings or command history.

### Step 8: Compare liveness and readiness

In another terminal:

```sh
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/health/ready
curl --fail http://127.0.0.1:5080/api/services
```

Liveness returns `{"status":"ok"}` if the process responds. Readiness returns `{"status":"ready"}` only after storage initialization and a successful schema/database check, with no unresolved last write failure. Otherwise it returns HTTP 503 and `{"status":"not_ready"}` without private diagnostics.

A website returning 500 is a target failure, not a storage failure. If that failed observation is saved correctly, the monitor can still be ready.

### Step 9: Understand restart restoration

Stop the API with Ctrl+C and run it again using the helper. The database and volume remain. Startup synchronizes configuration and restores durable snapshots before scheduling checks. A fresh failure streak survives restart, while old/future-dated observations become UNKNOWN and reset the streak.

UTC completion time bridges process lifetimes. Inside the new process, monotonic elapsed time governs freshness again. The monitor does not pretend it checked continuously while stopped. A new startup check may quickly replace the restored snapshot, so the controlled restart test below is the clearest demonstration of restoration before new work completes.

```sh
dotnet test tests/UptimeUrpo.IntegrationTests --no-restore --filter FullyQualifiedName~RestartRestoresTheLastObservationBeforeTheNextCheckCompletes
```

This uses fake HTTP responses and a disposable database. Its second request stays pending so the test can verify the restored snapshot directly.

### Step 10: Preserve configuration history

Changing interval, enabled state, URL, expectation, timeout, or thresholds creates a new configuration period and clears incompatible current state. Renaming a service does not change its identity. Removing a target retires it without deleting history; reintroducing it opens a new period.

This prepares for honest coverage calculations in Part 5. Disabled time is different from missing checks. Periods record changes observed at startup, not an invented exact time for an edit made while stopped. Keep stable IDs for the same service and document material target changes.

### Step 11: Read the LINQ history query and retention

Find `ReadHistoryAsync` in the store. `Where`, ordering, and `Take` form a database query; `ToListAsync` executes it. Filtering happens in PostgreSQL rather than downloading all rows and calling JavaScript-style array filtering afterward. `AsNoTracking` avoids tracking read-only rows.

The method uses `[start, end)` UTC request-start times, newest-first ordering, and a maximum of 1,000 results. It is tested directly; the HTTP history endpoint is Part 5 work.

`RetentionWorker` deletes at most 500 raw rows per minute by default, strictly older than 90 days by completion time. It does not run an unbounded deletion inside an API request. Current snapshots and configuration periods remain; incident retention will be added when incidents exist.

### Step 12: Learn failure behavior safely

Run the database scenarios:

```sh
dotnet test tests/UptimeUrpo.IntegrationTests --no-restore --filter 'Category=Database'
```

[The tests](tests/UptimeUrpo.IntegrationTests/DatabaseTests.cs) use a disposable PostgreSQL container, not your local development volume. They verify migrations, runtime DDL denial, history queries, transaction rollback, configuration periods, retention boundaries, restart continuity, and storage failure/recovery.

One test revokes INSERT permission, allows a scheduled attempt, and verifies that no new result is published. Readiness becomes unhealthy. Restoring permission allows the next normally scheduled result to be saved and readiness to recover. There is no unbounded memory queue or fake outage result.

For the fast suite without database containers, use:

```sh
dotnet test Uptime-Urpo.slnx --no-restore --filter 'Category!=Database'
```

Those worker/API tests use a test-only store. The actual application has no memory-only persistence fallback. Full database tests fail explicitly when Docker is unavailable rather than silently skipping evidence.

### Step 13: Stop local infrastructure without losing history

After stopping the API:

```sh
python3 scripts/dev-db.py status
python3 scripts/dev-db.py down
```

`down` stops only this Compose project and preserves the volume. Do not add `--volumes` as routine cleanup: removing the database volume destroys local history. Preserve `.local/database.env`, and never commit it, database backups, or real connection strings.

### What was verified

All 138 cases passed: 105 unit cases and 33 integration cases, including nine real PostgreSQL scenarios. A native local run used the restricted runtime role, reported ready, saved one approved owned-target HTTP 200 observation, and shut down gracefully. A SQL count confirmed the saved row afterward. The local database was left running for continued use; no NUC service or deployment was touched.

See [the M3 record](docs/MILESTONE-3.md#verification-record--2026-10-26) for exact limits, package pins, commands, and operational caveats.

### Teach this part

Draw the prepare → save → publish flow, show the entity/context mapping, then run the rollback or restart test. Ask the learner:

- Does assigning an entity property immediately commit SQL? **No; the context tracks the change until saving.**
- Why separate migration and runtime identities? **Normal monitoring needs data access, not schema-changing privileges.**
- Why publish after saving? **The API should not advertise an observation that failed to become durable.**
- Is a model snapshot a database backup? **No; it describes schema, not live rows.**
- Can retention remove history while the last snapshot remains? **Yes; they are stored separately and the snapshot still ages to UNKNOWN.**
- What do PostgreSQL tests establish that a fake store cannot? **Actual SQL, transaction, constraint, migration, and permission behavior.**

**What remains:** history/uptime/incident API design, dashboard, and deployment. Part 5 requires approval before implementation.

## Part 5 — Turn history into a useful read-only API

**Milestone 4 · Complete**

### What we built

The backend now exposes observed availability and coverage, bounded chart history, paginated incidents, an overview, and OpenAPI JSON. Incidents are saved atomically with their check result and snapshot. Existing list/detail routes remain compatible, and reads still never trigger checks.

This part also adds request limits, validated date/page parameters, real SQL cancellation tests, and a private local backup before migration. It does not add the React dashboard or deploy anything.

### Step 1: Back up, migrate, and restart

From the repository root, with Docker running, stop your old API using Ctrl+C in its own terminal, then:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Uptime-Urpo.slnx --locked-mode
dotnet tool restore
python3 scripts/dev-db.py up
python3 scripts/dev-db.py migrate
python3 scripts/dev-db.py run
```

The helper now creates a custom-format `pg_dump` backup in ignored `.local/backups` before migrating. A backup failure prevents migration. You can make another backup with `python3 scripts/dev-db.py backup`. These files contain private data and are mode 0600; preserve them deliberately and keep them out of Git.

The additive M4 migration was already applied locally after backup. The running user API was preserved, so it needs a restart to load the new code. Do not start a second monitor against the same database.

### Step 2: Discover the read contract

In another terminal:

```sh
curl --fail http://127.0.0.1:5080/openapi/v1.json
curl --fail http://127.0.0.1:5080/api/services
```

OpenAPI describes route/query names, response DTOs, and errors. It is JSON documentation, not the future dashboard. Use a configured ID from the service list in the following requests; this checkout's existing target uses `iltapaska`.

```sh
curl --fail http://127.0.0.1:5080/api/services/iltapaska/statistics
curl --fail http://127.0.0.1:5080/api/services/iltapaska/history
curl --fail 'http://127.0.0.1:5080/api/services/iltapaska/incidents?limit=20'
curl --fail http://127.0.0.1:5080/api/overview
```

### Step 3: Read the DTOs before the query implementation

Open [MonitoringResponses](backend/UptimeUrpo.Api/Contracts/MonitoringResponses.cs). `AvailabilityWindow`, `HistoryPoint`, `IncidentPage`, and `OverviewResponse` make the public JSON shapes explicit. Nullable percentages/timings represent absence, not an automatic zero or perfect score.

The API returns DTOs, not EF entities. Target URLs, database connection details, and raw exception messages remain outside the contract. This is similar to defining TypeScript response types, with actual C# record instances serialized at runtime.

### Step 4: Explain availability versus coverage

Availability is successful durably saved checks divided by all saved checks in the window. Two successful samples out of two yield 100%. Slow matching responses count as successful; failed checks reduce availability even before the DOWN threshold.

Coverage separately asks how many expected nominal slots contain evidence. If only two of eight slots contain those samples, coverage is 25%. We cannot infer what happened during the other six slots. Missing observations are not confirmed downtime and do not enter the availability denominator.

Read [SamplingCoverage](backend/UptimeUrpo.Api/Monitoring/SamplingCoverage.cs). Slots use each enabled configuration period's start/interval. Partial window edges count once; disabled/pre-enrollment time contributes none. Multiple samples in one slot cannot inflate coverage. Fixed-delay scheduling can drift across slot boundaries, so this metric is sampling density, not a continuous-time SLA.

The `24h`, `7d`, and `30d` windows are elapsed UTC durations, avoiding local-day ambiguity around daylight-saving changes. Display-time localization belongs to the dashboard.

### Step 5: Follow validation at the HTTP boundary

Open [QueryParameters](backend/UptimeUrpo.Api/Endpoints/QueryParameters.cs), then [MonitoringEndpoints](backend/UptimeUrpo.Api/Endpoints/MonitoringEndpoints.cs). `[AsParameters]` groups query values, and `TryResolve` turns raw input into a bounded query or validation errors.

History uses offset-bearing ISO-8601 dates, at most 30 days inside retention, an exclusive end, and no future end. Omitted dates select the previous 24 hours. `bucketMinutes` is restricted to documented values and at most 1,000 points; automatic selection grows the bucket for longer ranges.

Try an invalid request without changing any target:

```sh
curl -i 'http://127.0.0.1:5080/api/services/iltapaska/history?bucketMinutes=0'
```

It returns 400 Problem Details explaining the rule, without echoing the supplied value. An unknown ID returns 404. A storage problem returns sanitized 503. Those statuses describe the API operation, not the website's HTTP status.

### Step 6: Read the PostgreSQL aggregates

Open [MonitoringQueries](backend/UptimeUrpo.Api/Persistence/MonitoringQueries.cs). Parameterized `SqlQuery` calls group samples in PostgreSQL. `count(*) FILTER` separates successes/responses, while a distinct slot index prevents duplicate coverage. The application receives aggregate rows and fills empty buckets explicitly.

The interpolated SQL argument is a `FormattableString`, so EF binds IDs, times, and bucket lengths as parameters. No submitted query text becomes SQL. This uses your SQL experience without hiding the calculation inside complicated abstractions.

Mean/min/max timings include actual HTTP responses, including unexpected status responses. DNS failures remain failed observations but have no HTTP response timing. An empty bucket has null timing and remains present as `missing` or `not_monitored`. An observed bucket can still have partial coverage; it does not promise continuous observation.

### Step 7: Understand the read transaction

Each multi-query read uses repeatable-read isolation. Its historical queries see one consistent PostgreSQL snapshot while the monitor keeps writing. Request cancellation reaches those queries and disposes the transaction.

Overview also contains a separately captured cached health snapshot. It does not promise a distributed atomic instant across cache and database. That distinction matters when explaining consistency honestly.

### Step 8: Follow one durable incident

Read [MonitoringStore](backend/UptimeUrpo.Api/Persistence/MonitoringStore.cs). The pending first-failure time is saved on the service, so it survives restart. The threshold failure inserts an incident with first-failure and confirmation timestamps. Later failures do not insert another open incident; a partial unique index enforces that in PostgreSQL.

A success closes it as `recovered`, even if latency leaves the service DEGRADED. A stale gap or disabled monitoring keeps it open with uncertainty. Changing URL/status expectation ends it as `configuration_changed`, not recovered. The duration can include flagged gaps; it is not continuous proven downtime.

The history row, snapshot/streak, and incident transition share one transaction. The rollback test deliberately breaks a history insert at the confirmation step and proves that no incident or new snapshot survives the failed transaction.

M3 did not track incidents. The upgrade seeds only its latest known failed snapshot time and does not invent older outage history. New streaks retain their first tracked failure correctly.

### Step 9: Page through incidents with a cursor

An incident page contains `items` and nullable `nextBeforeId`. Pass that value as `beforeId` for the next older page, keeping `limit` between 1 and 100.

Because queries use IDs below a boundary, a newly inserted incident does not shift the older range. This avoids offset pagination moving items between pages. Current duration uses the page's `asOfUtc`; ended incidents keep their end-based duration. Closed incidents retain 365 days by end time; open incidents never expire.

### Step 10: Exercise behavior through controlled tests

```sh
dotnet test Uptime-Urpo.slnx --no-restore
dotnet test tests/UptimeUrpo.IntegrationTests --no-build --no-restore --filter 'FullyQualifiedName~Analytics|FullyQualifiedName~Incident|FullyQualifiedName~Upgrade'
```

[Analytics tests](tests/UptimeUrpo.IntegrationTests/AnalyticsTests.cs) cover sample/coverage denominators, duplicate slots, gaps, interval changes, and boundaries. [Incident tests](tests/UptimeUrpo.IntegrationTests/IncidentTests.cs) cover confirmation, recovery, restart, configuration changes, rollback, uniqueness, retention, and pagination.

[HTTP tests](tests/UptimeUrpo.IntegrationTests/AnalyticsApiTests.cs) exercise DTOs, errors, OpenAPI, and real PostgreSQL cancellation. They hold a table lock to verify the four-request concurrency bound and cancellation returning permits. A rate-limit test verifies 429 after the fixed-minute allowance while liveness remains available. These scenarios use disposable databases and no real target outages.

### Step 11: Recognize the operational boundary

Analytics routes share four active requests and 60 requests per minute, with no queue. Limits plus bounded ranges/pages and SQL command timeout constrain work; they do not provide authentication. Keep the listener private. There is no load benchmark or deployed evidence yet.

The user's existing API process was not stopped during implementation. Native checks of these new routes await its restart; API behavior was verified with ASP.NET Core's test server and real disposable PostgreSQL. Local history was backed up and retained through the migration, with no NUC or public deployment work.

### Teach this part

Show a statistics response, explain a 100%-availability/25%-coverage example, then trace an incident and its rollback test. Ask:

- Why can missing samples coexist with 100% observed availability? **The percentage describes observed samples; coverage reports missing evidence separately.**
- Why keep a DNS failure out of response-time averages? **No HTTP response headers arrived.**
- What prevents two open incidents for one service? **The write logic plus a unique partial database index.**
- Why is a changed URL a distinct resolution? **Success at the new target does not prove the old target recovered.**
- What makes cursor pages stable? **An ID boundary remains meaningful when newer items are inserted.**
- Does a 429 mean the target is DOWN? **No; the analytics request exceeded an API resource limit.**

See [the M4 contract and evidence](docs/MILESTONE-4.md). **Next is Part 6: the React dashboard**, after approval.

## Part 6 — Build the React dashboard

**Milestone 5 · Complete and locally verified**

### What we built

A private React dashboard now presents the C# application's monitoring data. The overview shows overall health, individual service statuses, response time, observed availability, and coverage. You can search services, filter those needing attention, and select 24 hours, seven days, or 30 days.

A service detail page adds response history, a keyboard-inspectable chart, its data table, open incident elapsed time, and paginated incident history. Both light and dark themes work, and the layout adapts to small screens. Loading, no services, missing observations, failed requests, and stale snapshots all have explicit views.

We did not add administration, registration, URL submission, another monitor, or a deployment. The UI reads the API; it never performs target checks itself.

### What you need first

Parts 1–5, a running development database/API, and Node/npm. This milestone was verified with Node 26.9.0 and npm 12.2.0; `frontend/.nvmrc` records the Node version. The frontend dependencies are exact versions with a committed package lock.

From the repository root, start the API if it is not already running:

```sh
python3 scripts/dev-db.py up
python3 scripts/dev-db.py run
```

In another terminal:

```sh
npm --prefix frontend ci
npm --prefix frontend run dev
```

Open **http://127.0.0.1:5173/**. The development API remains on port 5080. You do not need to run two monitors or duplicate the database.

### Step 1: Follow data across the language boundary

Read [the C# response records](backend/UptimeUrpo.Api/Contracts/MonitoringResponses.cs), then [the TypeScript interfaces](frontend/src/types.ts). C# uses `DateTimeOffset` for timestamps, but JSON carries strings. C# `double?` becomes `number | null`, so no observations can remain “no percentage.” Health is an explicit union of the four string values.

```ts
export type Health = 'UP' | 'DOWN' | 'DEGRADED' | 'UNKNOWN';
```

The browser does not receive EF entities, connection strings, or target URLs. It receives the safe DTOs we designed in Part 5. TypeScript checks code during compilation; a cast from JSON does not validate an arbitrary server response at runtime. Our API contract tests cover the producer, and the frontend rejects HTTP failures and non-JSON fallback pages.

**Teach it:** Trace `ObservedAvailabilityPercent` from C# through camelCase JSON to `observedAvailabilityPercent` in TypeScript, then show `percent(null)` displaying a dash instead of inventing 0% or 100%.

### Step 2: Connect development without adding CORS

[Vite configuration](frontend/vite.config.ts) proxies `/api` and `/health` to the existing loopback API. The browser sees requests to its own origin, `127.0.0.1:5173`.

[api.ts](frontend/src/api.ts) uses relative URLs such as `/api/overview`. Production can serve the same JavaScript next to the API without replacing a hard-coded backend hostname. `getJson` checks HTTP status and JSON content type before parsing; safe messages explain 404, 429, and 503 without exposing raw server exceptions.

This separation is useful in any full-stack project: build tools serve the interface during development, while the application owns the data contract.

### Step 3: Model loading as real state

Read [useResource](frontend/src/hooks.ts). It keeps `data`, `loading`, `error`, and receipt time separately. Those fields permit several useful combinations:

- Loading with no data: show a loading state.
- Data with no error: show the successful snapshot.
- Error with no data: explain that data could not be loaded and allow retry.
- Error with old data: keep useful evidence, but label it as old.

A failed refresh must not leave a green “current” banner. The overview becomes “Waiting for fresh signals,” uses neutral styling, and labels badges and accessible link names as last known. An older-than-90-second snapshot is also flagged. Server UNKNOWN still has its own target-freshness meaning.

`AbortController` is the browser counterpart to propagating a C# `CancellationToken`: cancel requests when a component goes away, its window changes, or ten seconds expire. The hook has a busy guard so polls do not overlap. It pauses while the tab is hidden and refreshes when it becomes visible.

Overview polls every 30 seconds; chart and first-page incidents every 60 seconds. This is UI polling of saved evidence, not the monitoring schedule. Multiple tabs still share the backend request limit, so 429 remains a real handled state.

**Demonstrate a failure safely:** use the isolated harness in Step 8 instead of stopping a real target or changing the database.

### Step 4: Build views from small components

Read [App.tsx](frontend/src/App.tsx). `OverviewPanel`, `ServiceDetails`, `Metric`, `Status`, and `IncidentList` each present a specific piece of the contract. React state holds UI choices such as search text, time window, theme, and loaded incident pages; it does not recreate the monitoring state machine.

`#/services/{id}` provides simple client navigation. A browser reload still requests `/`, and Back/Forward changes the selected service. A route change focuses the page heading. The skip link focuses the main content without accidentally changing the service route.

Window controls use real buttons with `aria-pressed`. Statuses have words as well as color. Theme state starts from the saved choice or system preference, and storage failures do not disable the control. [CSS variables](frontend/src/styles.css) define both themes without duplicating the layout.

### Step 5: Draw evidence without filling the gaps

Read [ResponseChart.tsx](frontend/src/ResponseChart.tsx), starting with `responseSegments`. A null average ends a line segment; the next HTTP timing starts a new one. Never draw a straight connection across an interval where nothing was observed.

The chart maps actual UTC bucket boundaries onto its horizontal axis. It shows missing observations and monitoring-disabled periods separately. Failed checks have a distinct strip, including failures with no HTTP response. A DNS failure is not a zero-millisecond response.

Hover or click/touch selects a bucket. The native range input gives keyboard users Home/End and arrow-key inspection. The selected bucket lists its start/end, average, min/max, completed checks, and failures. The expandable semantic table exposes every bucket without requiring someone to interpret SVG.

We used a small SVG implementation because the API already bounds and aggregates the data to at most 1,000 points. No chart library or interpolation layer was necessary for this graph.

There was a subtle integration bug worth teaching: .NET timestamps may contain more than three fractional-second digits, while JavaScript `Date` retains milliseconds. Keeping the original `to` but rounding `from` could request 30 days plus a fraction, which the API correctly rejects. `historyAt` normalizes **both** bounds and uses the server snapshot time so browser clock skew cannot request future history.

### Step 6: Present incidents without overstating certainty

The open-incident banner shows elapsed time since the first tracked failure and displays its later confirmation timestamp. It advances between requests. Closed incident duration remains fixed. Neither is an exact measurement of continuous downtime, and an incident containing monitoring gaps says so explicitly.

`IncidentList` sends `nextBeforeId` as `beforeId` to load older incidents. It merges pages by ID and keeps already loaded older rows when current incidents refresh. A chart-window change does not discard incident pages or keyboard focus. If a page request fails, the existing rows stay visible and the same cursor can be retried.

**Explain it to someone else:** “Health describes the latest signal; an incident records a confirmed failure episode; availability describes successful samples; coverage describes missing evidence.”

### Step 7: Serve the built interface from ASP.NET Core

```sh
npm --prefix frontend run build
"$HOME/.dotnet/dotnet" publish backend/UptimeUrpo.Api -c Release --no-restore -o .local/m5-publish
```

Vite creates `frontend/dist`. [The backend project](backend/UptimeUrpo.Api/UptimeUrpo.Api.csproj) includes these assets under `wwwroot` on publish and fails clearly if the frontend build is missing. Always rebuild before a release; the presence check cannot detect an outdated build.

[DashboardFiles](backend/UptimeUrpo.Api/Endpoints/DashboardFiles.cs) uses default/static-file middleware. Native development can serve `frontend/dist` after the API restarts. Published output serves `wwwroot`. There is no HTML catch-all, so `/api/not-a-route` still returns a 404 Problem Details response.

You can keep using Vite with the current API process. To try the built UI at `http://127.0.0.1:5080/`, build it, stop the existing API with Ctrl+C in its terminal, and start it again with `python3 scripts/dev-db.py run`. This is a local run, not a deployment. Node is a build dependency; ASP.NET Core serves the resulting files.

### Step 8: Verify behavior and teach with controlled examples

```sh
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run format:check
"$HOME/.dotnet/dotnet" test Uptime-Urpo.slnx --no-restore
```

The milestone has 25 frontend tests and 199 backend cases. [Component tests](frontend/src/App.test.tsx) exercise filtering, stale refresh recovery, empty/error states, theme storage failure, incidents/cursors, gaps, table access, focus, and routing. [Hook tests](frontend/src/hooks.test.tsx) verify cancellation, timeout, no overlapping request, hidden tabs, and obsolete requests. The C# static-file test proves that adding UI serving preserves API errors and liveness.

For a repeatable teaching demonstration with **synthetic data only**:

```sh
npm --prefix frontend run build
npm --prefix frontend run test:preview
```

Visit `http://127.0.0.1:5181/?scenario=signals`. It includes all health states and an incident with gaps. Open Test API, inspect chart buckets with the keyboard, and load the older recovered incident. Other scenarios are `empty`, `error`, `stale` (press Refresh), `missing`, and `loading`. Restart the harness to reset stale-scenario counters. Stop it with Ctrl+C afterward.

The harness does not connect to PostgreSQL or check real targets. Its fixtures are not imported into the application bundle. This lets you teach failures without creating false operational evidence.

Chrome verification used real local API data and synthetic browser scenarios, both themes, keyboard/pointer chart controls, and viewport widths 320/390/768/1440. Physical-device touch, Safari/Firefox, and a screen-reader session remain unverified. Read [the M5 evidence and limitations](docs/MILESTONE-5.md) before making portfolio claims.

### Teach this part

Give a five-minute demonstration: show overview → open service → explain availability versus coverage → inspect a missing bucket → open its table → explain incident confirmation and gaps → switch theme → show a failed refresh in the harness.

Ask your audience:

1. Does UI polling run another target check? **No. It reads persisted/cached observations.**
2. Can TypeScript guarantee arbitrary JSON matches an interface? **No. Types disappear at runtime; contracts need validation/testing at boundaries.**
3. Why keep availability and coverage separate? **Successful observed checks say nothing about missing observations.**
4. Why split the SVG line? **Connecting across null timings implies evidence we do not have.**
5. Why preserve old data after a refresh fails? **It is useful history, as long as its age and uncertainty are clear.**
6. Why normalize both query timestamps? **Different fractional precision can accidentally exceed a bounded date range.**
7. What proves physical touch support? **Testing the actual device; a narrow desktop viewport is not enough.**

**What remains:** M6 production images and NUC/private-access inspection, M7 delivery automation, and optional M8 Azure. Stop for the next milestone approval before that work.

## Part 7 — Deploy privately to the NUC

**Milestone 6 · Complete, privately deployed, and operationally checked**

### What we built

The NUC now runs the real monitoring application and PostgreSQL in their own containers. ASP.NET Core serves the built React interface and API together. Docker keeps these containers running without Vite or a native development terminal.

We added a production Dockerfile, isolated Compose networking, separate runtime/migration secrets, readiness checks, resource/log limits, release commands, backup-before-migration, a daily backup schedule, a restore drill into a separate database, and a private off-host recovery copy. The first access layer is SSH; no public hostname or Cloudflare rule was added.

This is deployment evidence, not a promise of indefinite reliability. The [M6 record](docs/MILESTONE-6.md) separates local tests, native Intel-host checks, private access, recovery checks, and remaining limits.

### What you need first

Parts 1–6 and authorized SSH access to the inspected NUC. We checked the actual Debian version, architecture, Docker/Compose, storage/memory, occupied ports, and existing proxy/services before deployment. The working account is `roope@nuc`; the alias's configured default account was rejected. We preserved the shared host's other services.

To use the deployed application from the Mac:

```sh
ssh -N -L 127.0.0.1:5081:127.0.0.1:8108 roope@nuc
```

Keep that terminal open, then visit **http://127.0.0.1:5081/**. You need neither a local API nor Vite for this deployment. The application continues monitoring on the NUC even after you close the tunnel; the tunnel only provides access to its private interface.

### Step 1: Distinguish images, containers, and volumes

An **image** is the packaged application/runtime. A **container** is an instance running that image. A **volume** holds PostgreSQL data independently of the container's writable layer.

Replacing an application image must not replace the database volume. Likewise, rebuilding a database container does not reset passwords stored in an initialized database. Preserve its original configuration and credentials with its volume.

[The Dockerfile](Dockerfile) has three stages: Node builds the frontend; the .NET SDK publishes the backend with those assets; the runtime image receives only the published output. Production needs the runtime, not Node or the SDK.

The output uses portable framework-dependent IL with `UseAppHost=false`, executed by `dotnet UptimeUrpo.Api.dll`. The initial architecture-specific restore conflicted with the existing portable lockfile. Publishing portable IL preserved locked restore and let us use native ARM64 and AMD64 runtime images without an unused native launcher.

[.dockerignore](.dockerignore) keeps local settings, dependencies, generated output, and fixtures out of the build context. Git ignore alone does not protect Docker builds: the two tools need their own boundaries.

**Teach it:** “Code belongs in the replaceable image; observations belong in the persistent volume; credentials belong in private state outside Git.”

### Step 2: Follow the production network

Read [Compose](deploy/compose.yaml). PostgreSQL joins only the internal database network and publishes no port. The app joins that network plus an outbound network so its checker can reach approved public HTTPS targets.

Only the application publishes a host port, bound to NUC loopback at 8108. The SSH command forwards Mac loopback 5081 to it. Existing SSH authentication protects this access path; the application itself has no login system.

We verified that the origin responds through authenticated SSH while a direct LAN connection to port 8108 was blocked/refused. A future Cloudflare route needs its own inspected Access policy and origin checks. A healthy local origin alone does not prove a public access layer is correct.

### Step 3: Separate operating-system and database identities

The application runs as the image's `app` user, PostgreSQL as `postgres`. Neither process runs as root. Read-only root filesystems, dropped capabilities, bounded temporary mounts, process/memory/CPU limits, and rotating logs constrain their operation.

Database identities are a separate boundary. The runtime role can work with monitoring data but cannot create schema objects or migrate. The short-lived migration task receives its own secret and exits after applying migrations. Runtime writes to EF migration history are revoked afterward.

Secrets are mounted as files, not baked into images or placed in the API. The host state/secrets directories are mode 0700; mounted files remain readable by the non-root users inside their assigned containers. Host administrators and Docker users remain trusted. The app container does not receive admin or migration passwords.

[Program.cs](backend/UptimeUrpo.Api/Program.cs) reads an optional private configuration file selected by `UPTIME_CONFIG_FILE`, then applies environment/CLI precedence. Native local development continues to use its existing helper/configuration independently.

### Step 4: Make readiness useful without starting another monitor

Docker runs the application with `--health-check` to probe `/health/ready`. That early CLI path makes one bounded loopback HTTP request and exits; it does not build another host or start another monitoring worker.

Liveness answers whether the process responds. Readiness additionally needs the database/schema. Stopping the isolated test database left `/health` at 200 and made readiness return 503. Restarting it restored readiness without inventing history.

A health check marks a container healthy/unhealthy; Docker's restart policy handles process/container exits and daemon restarts. It does not automatically restart every live unhealthy process. Persistent unhealthiness still needs investigation.

### Step 5: Trace a release and its failure boundary

Read [publish](deploy/publish) and [Stack.release](deploy/ops.py). The default workflow requires a clean commit, runs verification, builds AMD64, transfers the source snapshot/image through SSH, and holds a release lock. The private state directory lives outside the replaceable release folders.

```sh
./deploy/publish --host roope@nuc --directory /home/roope/apps/uptime-urpo --port 8108 --targets-file backend/UptimeUrpo.Api/appsettings.Local.json
```

The optional file transfers only its approved `Monitoring` configuration. It does not replace production database credentials. Omitting it preserves existing production targets, or starts a new stack with no targets.

The host-side sequence stops this app, starts/waits for its database, saves and validates a backup, runs explicit migrations with the migration identity, recreates the application, and checks readiness. Only then does `current` point to the new source release.

A failed backup prevents migration. A failed migration does not trigger an automatic database rollback or discard the volume. The seven [operations tests](deploy/test_ops.py) cover failure ordering, corrupt archives, credential-regeneration guards, remote script syntax, and schedule behavior. Real container tests cover the actual database/runtime boundaries.

**Explain it:** A release is more than copying files. It establishes which code ran, whether schema changes were deliberate, and which recovery point existed beforehand.

### Step 6: Prove a backup by restoring it

A successful `pg_dump` and a readable archive inventory are useful checks, but neither proves a restore works. [restore_check](deploy/ops.py) verifies the archive checksum, creates a uniquely named disposable database, restores with migration ownership, checks tables/runtime access, and compares row counts when the source app was stopped for the backup. It drops only that disposable database.

A stronger test caught a subtle PostgreSQL rule: **default privileges are per database**. Restoring objects under the migration owner did not automatically grant the restricted runtime access in a new database. The drill now establishes those per-database defaults before restoration and verifies a runtime read afterward.

The local isolated test includes synthetic service/history/incident rows. The real NUC restore used its actual backup and did not manufacture an incident: one service, one period, five checks, zero incidents, two migrations were recovered into a separate database. Restart then resumed monitoring and increased checks from five to six.

Use [the deployment guide](docs/DEPLOYMENT.md#backups-and-restore-drill) for exact backup/restore commands. Never use the synthetic seeding script on production; it rejects production project names.

### Step 7: Keep backups useful after deployment

[The installer](deploy/install-backups.py) preserves existing crontab entries and adds one project-scoped hourly check. [scheduled_backup](deploy/ops.py) uses Europe/Helsinki to create one daily backup at/after 03:17, retry after failures, and avoid repeating the day's backup during autumn's repeated hour. We did not change the shared host's timezone.

The schedule is installed and the scheduled helper was successfully invoked. Its next unattended execution remains future evidence. Logs are bounded to the last schedule check, and a marker records the last successful scheduled date.

Backups on the same NUC do not protect against losing that machine or disk. Copy recovery data off-host:

```sh
./deploy/fetch-backups --host roope@nuc --state /home/roope/apps/uptime-urpo/state
```

The owner-only local tar includes database archives, checksum manifests, settings, and recovery passwords. Four archived database backups were copied and checked during M6. This is a manual copy, not automatic off-host replication. Keep it private and outside Git/shared folders; plan disk monitoring and archive retention rather than silently deleting old backups.

### Step 8: State exactly what was verified

M6 passed 199 backend, 25 frontend, and seven operations tests, plus real container checks. Native AMD64 checks on the NUC covered readiness, real target observations, non-root/read-only operation, network/port isolation, graceful app shutdown, database/app restart continuity, a separate-database restore, SSH-forwarded browser UI, backup schedule installation, and a private off-host copy.

No physical host reboot, full replacement of the live database, public/Cloudflare access, physical phone test, load benchmark, or long-running reliability claim follows from these checks. An image rollback does not undo migrations; a full data recovery needs a separately reviewed plan and can lose observations newer than the chosen backup.

A monitor on the NUC still cannot independently announce that the NUC itself lost power or connectivity. Missing samples remain uncertainty; an external observer is a future complement.

### Teach this part

Demonstrate the SSH-forwarded dashboard, then explain the Dockerfile stages, database volume, two database identities, release ordering, and restore result. Ask:

1. Why doesn't a new app image erase history? **History lives in a persistent database volume.**
2. Why keep migration credentials out of the app container? **Normal monitoring should not have schema-changing authority.**
3. Does a checksum prove recoverability? **It proves matching bytes; an actual restore and access/data checks provide stronger evidence.**
4. Why did runtime reads initially fail in the restored copy? **Default grants belonged to the original database and needed establishing in the new one.**
5. Does closing the SSH tunnel stop monitoring? **No. It closes access from this Mac; the NUC containers keep running.**
6. Does Docker restart every unhealthy process? **No. Health status and restart policy have distinct roles.**
7. Does rolling back an image undo migrations? **No. Schema/data recovery needs its own compatible, reviewed procedure.**

**Next:** Part 8 adds GitHub Actions validation and explicitly triggered delivery, after the next milestone approval.

## Part 8 — Automate checks and delivery

**Milestone 7 · Planned; not built**

Introduce CI checks, reproducible release builds, explicitly triggered deployment, and failed-release recovery. Explain the difference between rolling back an application image and recovering a database after a migration.

## Part 9 — Explore Azure and Infrastructure as Code

**Milestone 8 · Optional and planned; not built**

Compare Azure concepts with familiar infrastructure, design a small Bicep deployment, estimate cost, and plan teardown. Provision paid resources only after separate explicit approval.

## How we will maintain the tutorial

At the end of each approved milestone, replace that part's outline with a lesson based on what actually exists. Keep a consistent structure:

1. **What we built:** visible behavior and the boundaries of the milestone.
2. **What you need first:** prerequisites and previous parts.
3. **Step-by-step walkthrough:** commands, source links, and explanations of the actual implementation.
4. **Why it works this way:** decisions and brief comparisons with familiar technologies.
5. **How to verify it:** meaningful tests, expected results, and one failure case.
6. **Teach this part:** a short demonstration and questions with explanations.
7. **What remains:** planned work and any unverified behavior.

Update examples when code changes. Use sanitized configuration and sample responses. Label historical verification results, and distinguish a tutorial command from evidence that the command was actually run.
