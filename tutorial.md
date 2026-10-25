# Building Uptime-Urpo: learn it, then teach it

Uptime-Urpo is a C#/.NET learning project: a private uptime monitor for websites owned by its operator. This tutorial follows the project as it is built, explaining what changed, why it changed, and how to verify it.

Each part corresponds to one milestone. **Parts 1 and 2 are complete. Parts 3–9 are planned.** The application now performs one startup check and serves its saved result through a local API. It does not yet schedule checks or store history.

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

Read [StartupCheckService](backend/UptimeUrpo.Api/Monitoring/StartupCheckService.cs). `IHostedService.StartAsync` runs as the host starts. If enabled, it awaits one check and saves the result in `StartupCheckState`. Then the server starts listening. Startup can therefore wait up to the configured check timeout.

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

Open [the API tests](tests/UptimeUrpo.IntegrationTests/ApiTests.cs). `ReadsReturnTheSavedStartupResultWithoutMoreChecks` makes two API reads and asserts that the outbound handler was called once. `TargetFailureDoesNotFailApplicationLiveness` proves that a target 503 does not fail `/health`.

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

**Milestone 2 · Planned; not built**

Introduce `BackgroundService`, multiple configured targets, intervals, timeouts, limited concurrency, health transitions, and graceful shutdown. Teach why asynchronous work, cancellation, and no-overlap scheduling matter. Demonstrate failures and recovery with controlled tests. Results remain in memory at this stage.

## Part 4 — Keep monitoring history in PostgreSQL

**Milestone 3 · Planned; not built**

Introduce EF Core, Npgsql, entities, migrations, LINQ, scoped database contexts, retention, and disposable database tests. Explain how a result survives an application restart and why database work must not share one context across concurrent checks.

## Part 5 — Turn history into a useful read-only API

**Milestone 4 · Planned; not built**

Add the remaining read endpoints, incident history, observed availability, monitoring coverage, bounded queries, validation, and OpenAPI. Teach the uptime denominator with worked examples drawn from tested application behavior, including missing observations.

## Part 6 — Build the React dashboard

**Milestone 5 · Planned; not built**

Connect React and TypeScript to the C# API. Explain loading/error states, accessible status indicators, interactive charts with gaps, incident duration, responsive layout, and themes. Record browser and physical-device checks separately.

## Part 7 — Deploy privately to the NUC

**Milestone 6 · Planned; not built**

Inspect the real server before writing host-specific instructions. Teach production images, Compose, private access, readiness, backups, restores, and recovery. Document actual approved deployment commands and evidence without exposing secrets or private infrastructure details.

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
