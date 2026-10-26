# C# for Roope

## Milestone 0 — Before the first API

You already know JavaScript, TypeScript, PHP, SQL, and Linux operations. The goal is to connect new C# concepts to that knowledge and then recognize them in Uptime-Urpo's code.

This section records the introductions made before application code existed. Its short snippets are conceptual previews, not complete implementations. **M1 is now implemented**; the next section links its actual source and explains the concrete code.

### 1. Static types and nullable references

TypeScript checks types and normally erases them when producing JavaScript. C# compiles to .NET assemblies; the runtime also knows types such as `int`, `string`, and your own result records. C# is not just JavaScript with type annotations: for example, `int` is a value type and `string` is a reference type.

```csharp
int expectedStatus = 200;
int? actualStatus = null;
string? failureCategory = null;
```

For Uptime-Urpo, a DNS failure has no HTTP response, so `actualStatus` must be able to represent absence. `int?` is a nullable value type. With nullable reference analysis enabled, `string?` tells the compiler that a reference may be null; plain `string` expresses the intention that it is present. Nullable reference warnings help prevent mistakes but are not runtime input validation.

Like TypeScript's strict null checking, the compiler asks you to handle absence before dereferencing. Unlike PHP's looser coercions, assigning arbitrary text to an `int` does not silently become an integer.

### 2. Classes, records, interfaces, properties, and constructors

A **class** groups state and behavior. A **record** is useful for a data value such as a check result and provides generated value-based equality. An **interface** specifies a contract; use one when a real boundary benefits from substitution, not for every class.

```csharp
public sealed record CheckResult(
    DateTimeOffset CheckedAtUtc,
    int? StatusCode,
    bool Success);
```

This positional record declares properties and a constructor. `new CheckResult(time, 200, true)` supplies those values. It resembles a TypeScript DTO type plus an actual runtime constructor and equality behavior. Records are not automatically deeply immutable; keep the data they contain simple.

A constructor also declares what an object needs to work. We will initially prefer explicit constructors in behavior classes so dependency wiring is visible. A property looks like `public string Name { get; init; }`: `get` allows reading and `init` allows assignment during initialization.

### 3. `Task`, `async`/`await`, and cancellation

A planned checker method might have this shape:

```csharp
public Task<CheckResult> CheckAsync(CancellationToken cancellationToken);
```

This is a method declaration preview, not a standalone compilable implementation. `Task<CheckResult>` is comparable to `Promise<CheckResult>`. Inside an `async` method, `await` waits for completion without holding a thread blocked on network I/O. Async does not mean a dedicated thread is created per request.

Pass the cancellation token through the HTTP call so shutdown and timeouts can stop work. JavaScript's `AbortSignal` is a useful comparison. Target timeout must become a failed check; host shutdown cancellation must not be recorded as evidence that the website failed.

We will measure elapsed request duration with a monotonic timer, and use UTC wall-clock time to say when a result happened. Those solve different problems.

### 4. Dependency injection and lifetimes

In Node, a service module often imports its HTTP library directly. In ASP.NET Core, `Program.cs` registers dependencies and the framework constructs your service with them.

```csharp
public sealed class HttpChecker
{
    private readonly IHttpClientFactory _clients;

    public HttpChecker(IHttpClientFactory clients)
    {
        _clients = clients;
    }
}
```

The constructor says the checker needs an HTTP client factory. `private readonly` keeps that field internal and prevents replacing its reference after construction; it does not make the referenced object immutable. The code still needs namespace imports, registration, and check behavior in M1.

Lifetimes describe how long registered instances live:

- **Singleton:** one instance for the application lifetime, such as the hosted worker.
- **Scoped:** one instance within an explicit scope; HTTP requests normally get a scope. Database contexts usually belong here.
- **Transient:** a new instance whenever resolved.

The scheduler must create appropriate scopes for database work later. A long-lived worker cannot safely capture a single database context and share it across parallel checks. Dependency injection makes construction explicit; it does not remove responsibility for object lifetimes or thread safety.

### 5. The host, middleware, and endpoints

`Program.cs` is the composition entry point, similar to the file that creates an Express app and registers its middleware/routes. ASP.NET Core's host also handles configuration, dependency injection, logging, server lifetime, and hosted workers.

A Minimal API endpoint maps a route to a handler. Middleware surrounds endpoint handling for concerns such as errors or request limits. Use an endpoint to translate HTTP into an application call and a response DTO; keep HTTP checking behavior in a readable service class.

Liveness asks whether this application process responds. A monitored website returning 500 must not make Uptime-Urpo's own liveness fail. Readiness asks whether the application has its required dependencies, such as PostgreSQL once introduced.

## Why this architecture helps learning

One backend project and explicit service classes make it possible to follow a request or scheduled check from start to finish. Minimal APIs avoid controller ceremony for a small read-only interface. Direct EF Core access later keeps SQL querying and persistence visible instead of hiding them behind a generic repository.

An interface, helper, or dependency should solve a concrete problem: testable HTTP/time boundaries, resource lifetime, or a repeated behavior. We will explain each such choice before adding it.

## Learning checkpoints

Before M1, try explaining:

- Why can a check fail while its HTTP status is null?
- How does a record differ from a TypeScript interface?
- Why does `await` not mean one thread per HTTP request?
- How does the checker receive an HTTP client factory?
- Why is a target outage different from this application's liveness?

M0 had no application tests. M1 now has tests; follow the commands in [the M1 guide](docs/MILESTONE-1.md).

## Milestone 1 — Follow the real code

This section introduces the concepts learned in M1. M2 has since changed the hosting/state classes and checker signature; the notes below point out those changes so links still lead to the current implementation.

### Entry point, namespaces, and project files

Start with [Program.cs](backend/UptimeUrpo.Api/Program.cs). Its top-level statements are the executable entry point; the compiler supplies the surrounding `Main` method. C# files elsewhere use `namespace UptimeUrpo.Api.Monitoring;` to organize type names. A `using UptimeUrpo.Api.Monitoring;` directive makes those names available without repeating the full namespace. This resembles importing names in TypeScript, though a C# `using` directive does not execute a JavaScript module.

The `.csproj` file declares the target framework (`net10.0`), nullable-reference checking, and implicit common namespace imports. `Uptime-Urpo.slnx` groups the API and test projects; it does not define a deployment. [Directory.Build.props](Directory.Build.props) applies shared settings to all projects: warnings are errors and dependency lockfiles are generated.

### Configuration becomes a typed object

[TargetOptions](backend/UptimeUrpo.Api/Monitoring/TargetOptions.cs) is a class with properties. For example, `public int TimeoutSeconds { get; set; } = 10;` declares an integer property, permits reading/writing, and supplies a default. M1 bound one object from `Monitoring:Target`; M2 binds a `MonitoringOptions` root containing targets, while still accepting the legacy object.

`IOptions<TargetOptions>` is a generic interface: the type argument says which settings it carries. The application validates the bound object before startup. C# static types cannot establish that an operator-supplied URL is safe, so runtime validation is still necessary, just as TypeScript types do not validate incoming JSON.

`is >= 1 and <= 30` is a C# pattern checking the permitted timeout range. Conditions joined with `&&` short-circuit, allowing a null/length check to guard later property access. `out var uri` declares a variable that receives the parsed URI if parsing succeeds.

### A result record and a failure enum

The actual [CheckResult](backend/UptimeUrpo.Api/Monitoring/CheckResult.cs) is:

```csharp
public sealed record CheckResult(
    DateTimeOffset CheckedAtUtc,
    double DurationMs,
    int? StatusCode,
    bool Success,
    FailureCategory? FailureCategory);
```

`sealed` prevents inheritance. The record constructor creates the six properties. `int?` represents the absence of a response status, and `FailureCategory?` represents either one allowed enum value or no failure. An enum gives names to a finite set of values; this is comparable to a TypeScript enum or a union of named failure categories.

The API's JSON configuration serializes the failure enum as strings such as `unexpected_status`. Its [response DTO](backend/UptimeUrpo.Api/Contracts/ServiceResponse.cs) deliberately omits the target URL. DTO means “data transfer object”: the shape allowed across the HTTP boundary.

### Constructor injection is visible object construction

Read [HttpChecker's constructor](backend/UptimeUrpo.Api/Monitoring/HttpChecker.cs). It now requires `IHttpClientFactory` and `TimeProvider`. M1 also injected the one target's options; M2 instead passes a target to each check. The framework supplies constructor dependencies because `Program.cs` registers them.

M1 registered `StartupCheckState` as a singleton; M2 replaced it with `AddSingleton<MonitoringState>()`, still one shared state object for the host. `AddTransient<HttpChecker>()` creates a checker whenever resolved. The worker receives one and retains it, safely using it for multiple checks because it has no mutable per-target fields. M3 now creates separate database scopes for persistence operations; see its section below.

The named HTTP client registration sets shared policy, including the user agent and transport handler. A short-lived `HttpClient` created by the factory uses managed underlying handlers; disposing the client does not mean creating a new TCP connection for every dependency resolution.

### `async`, tasks, and cancellation in a real request

The current checker method is `public async Task<CheckResult> CheckAsync(TargetOptions target, CancellationToken cancellationToken)`. It awaits `SendAsync`, which yields during network I/O. Compare this with awaiting `fetch` in JavaScript; the caller receives a task representing the eventual result.

A linked `CancellationTokenSource` combines the host's cancellation with the check timeout. The checker distinguishes them:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // Application shutdown is not evidence that the monitored site failed.
    throw;
}
```

`when` is an exception filter. Bare `throw;` rethrows while preserving the original exception context. Timeout cancellation is handled separately and returns a failed observation. Expected `HttpRequestException` failures become categories; unexpected programming errors are not swallowed by a blanket catch.

M1 used `Stopwatch` for elapsed duration. M2 uses `TimeProvider.GetTimestamp()` and `GetElapsedTime()` so tests can control the monotonic clock too. `TimeProvider.GetUtcNow()` supplies the observation timestamp. Response timing ends at headers, not at complete body download.

### Two meanings of `using`

At the top of a file, `using` imports a namespace. Inside the checker, `using var response = ...;` declares a disposable resource. It calls `Dispose` when execution leaves that scope, including when an exception is thrown. This is comparable to `try/finally` cleanup in JavaScript or scoped resource management in PHP.

The client, request, response, and timeout source are disposed. Production socket streams own their sockets so disposal releases the connection. The handler's zero drain limit prevents response disposal from reading a body just to reuse the connection.

### Interfaces, delegates, and the connection boundary

[IDnsResolver](backend/UptimeUrpo.Api/Monitoring/IDnsResolver.cs) is a small interface for real DNS work. Tests supply controlled answers. This boundary lets us test rebinding without depending on real DNS changes.

[PublicHttpsConnector](backend/UptimeUrpo.Api/Monitoring/PublicHttpsConnector.cs) accepts a `Func<IPEndPoint, CancellationToken, ValueTask<Stream>>` in an internal test constructor. `Func` describes a callable value, comparable to a TypeScript function type. Tests supply a function that records which IP would be connected instead of opening a public socket. The public production constructor supplies the real socket function.

`internal` limits that constructor to this assembly and the explicitly named friend test assemblies. It is not an HTTP-accessible bypass. `ValueTask<Stream>` is the callback-compatible asynchronous return type used here; prefer ordinary `Task` elsewhere unless an API or measured need calls for `ValueTask`.

Collections such as `addresses.Any(...)` and `.Distinct()` use LINQ to inspect in-memory values. Here they reject unsafe answers and avoid duplicate connection attempts. These are not database queries; EF Core's LINQ translation comes in M3.

### Hosting and JSON responses

M1's `StartupCheckService` implemented `IHostedService`: the host awaited one check in `StartAsync` and called `StopAsync` at shutdown. M2 replaces that class with [MonitoringWorker](backend/UptimeUrpo.Api/Monitoring/MonitoringWorker.cs), a `BackgroundService` whose `ExecuteAsync` runs until shutdown. See the next section for the lifecycle change.

The endpoint handlers in `Program.cs` use lambdas (`=>`), similar to JavaScript arrow functions. ASP.NET Core binds the route's `id` and injects the options and saved state. `Results.Ok(...)` serializes the DTO with HTTP 200; a missing ID returns 404 Problem Details. The health endpoint returns a typed result with `TypedResults.Ok(...)`.

The endpoint status and target status answer different questions: a 200 API response can contain a failed target check with HTTP 503.

### Logging and tests

Structured logging uses named placeholders rather than interpolating a raw exception or URL into a message. Expected failure logs include only success, status, and category. Default URL-bearing HTTP-client loggers are removed.

In xUnit, `[Fact]` marks one test case. `[Theory]` with `[InlineData(...)]` runs the same behavior with several inputs. `Assert.ThrowsAnyAsync<OperationCanceledException>` checks cancellation propagation. The test projects' short primary constructors are a compact constructor syntax; production classes initially use explicit constructors to keep dependency assignment visible.

[Unit tests](tests/UptimeUrpo.UnitTests/HttpCheckerTests.cs) isolate the checker with a fake HTTP handler. [API integration tests](tests/UptimeUrpo.IntegrationTests/ApiTests.cs) start the actual ASP.NET Core pipeline with controlled outbound responses. [TLS integration tests](tests/UptimeUrpo.IntegrationTests/HttpsTransportTests.cs) exercise real local sockets and certificates, with a test-only trust root. None require a public target or Docker.

### Explain it back

Follow the actual files and explain:

1. How does `Monitoring:Target` become `TargetOptions`?
2. Which object performs the one startup check, and why do later reads cause no checks?
3. What do null status and `dns` mean compared with status 503 and `unexpected_status`?
4. Why is shutdown cancellation rethrown instead of saved as failure?
5. How does connecting to a validated IP avoid a second DNS lookup without disabling hostname validation?

Run the commands in [the M1 verification guide](docs/MILESTONE-1.md#run-locally), then walk through [tutorial Part 2](tutorial.md#part-2--build-the-first-c-api) with another learner.

## Milestone 2 — Asynchronous lifecycle and shared state

This section records the worker concepts introduced in M2. M3 adds a persistence step before publishing each result; see the next section for the current save/restore path.

### `BackgroundService` owns a task that lasts until shutdown

Read [MonitoringWorker](backend/UptimeUrpo.Api/Monitoring/MonitoringWorker.cs). It inherits from `BackgroundService` and overrides `ExecuteAsync(CancellationToken stoppingToken)`. `override` supplies our implementation of a method defined by the base class.

The returned task represents the worker's whole lifetime. In .NET 10, `ExecuteAsync` runs in the background without blocking other hosted services from starting. The API can therefore serve an initial UNKNOWN response while the first check is pending. See [Microsoft's .NET 10 behavior note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task).

Compare this with starting a long-lived async function in Node and retaining its promise so shutdown can await it. ASP.NET Core owns the lifecycle rather than leaving a loose promise running after application shutdown.

### One sequential loop per target

`MonitorAsync` awaits a check, records it, then awaits a delay. It cannot begin that target's next check while the previous await is unfinished. We use a fixed delay after completion, avoiding interval callbacks that can overlap when work takes longer than expected.

`Task.WhenAll` joins the target-loop tasks, comparable to `Promise.all`. It does not mean allocating a dedicated thread for each website. Awaiting HTTP, a semaphore, or a delay yields while there is nothing to execute.

### `SemaphoreSlim` limits concurrent asynchronous work

The actual worker uses:

```csharp
await slots.WaitAsync(stoppingToken);
try
{
    // Check and save one observation.
}
finally
{
    slots.Release();
}
```

This is an abbreviated excerpt; the complete `try` body is in the worker. A semaphore is a counted gate: three slots permit at most three active checks. A fourth loop waits asynchronously. `finally` releases an acquired slot even when the request fails or is cancelled.

The slot is released before the interval delay, so sleeping targets do not consume network-work capacity. Each target has at most one pending loop, and configuration limits the total target count. This is an application-level async concurrency limit, not a distributed lock or a thread pool size.

### Cancellation travels through every wait

The stopping token reaches the semaphore, checker, and interval delay. A target's independent timeout reaches only its own HTTP request. One target timing out cannot cancel the other target loops.

The worker rethrows host cancellation from a loop, and `ExecuteAsync` recognizes cancellation as normal shutdown. `StopAsync` waits for the joined loops. It does not fabricate a failed target observation. An unexpected checker error instead logs a sanitized error and leaves a monitoring gap, allowing the next normal interval to try again.

### `lock` protects a short state update

Node code often relies on an event loop when accessing an in-memory object. ASP.NET Core request handlers and worker continuations can execute concurrently, so a mutable dictionary and multi-field updates need synchronization.

[MonitoringState](backend/UptimeUrpo.Api/Monitoring/MonitoringState.cs) uses `lock (_gate)` around recording or copying a snapshot. Related fields—result, completion time, and failure count—change as one protected operation. Readers receive a separate response record, rather than the mutable internal entry.

A `lock` is appropriate for these tiny synchronous operations. We never hold it during HTTP work or an `await`. It is different from `SemaphoreSlim.WaitAsync`, which is the async gate used around requests.

### Monotonic time answers “how long?”

`GetUtcNow()` answers “what time should we display?” A monotonic timestamp answers “how much time elapsed?” Wall-clock adjustments must not turn a fresh observation stale.

The checker uses time-provider-aware cancellation deadlines and elapsed duration. The worker uses `Task.Delay(interval, _time, stoppingToken)`. State uses monotonic elapsed time since completion. All use `TimeProvider.System` in the running application.

Tests replace that provider with `FakeTimeProvider` from a test-only Microsoft package. `clock.Advance(TimeSpan.FromSeconds(60))` moves scenario time forward without waiting a minute. Test helpers also wait briefly for async continuations to settle before asserting; fake time alone does not guarantee every continuation has run.

### Pure observations versus derived health

A `CheckResult` describes one completed request. `HealthStatus` describes what that evidence means **now**. A slow matching response is successful but DEGRADED; a previously successful result becomes UNKNOWN if stale. Reading health never rewrites the historical observation.

Consecutive failures increment until the threshold, a success resets them, and a stale gap breaks the streak. The counter saturates at the threshold. [State tests](tests/UptimeUrpo.UnitTests/MonitoringStateTests.cs) exercise these rules without network work. [Worker tests](tests/UptimeUrpo.UnitTests/MonitoringWorkerTests.cs) exercise scheduling and resource limits with controlled pending requests.

### Configuration compatibility is part of an interface

[MonitoringOptions](backend/UptimeUrpo.Api/Monitoring/MonitoringOptions.cs) adds a list of `TargetOptions`, unique-ID validation, and a global limit. Its nullable `Target` property accepts the old single-target shape. `GetTargets()` presents one effective list to the rest of the application.

The code rejects a nonempty new list combined with an old target. Silent merging could unexpectedly add or duplicate checks. Empty committed configuration is valid and sends no requests. Timeouts must be shorter than intervals to keep normal request timing compatible with the freshness rules. Runtime changes still require restart.

### Explain it back

1. Why does an awaited sequential loop prevent overlap for one target?
2. Why must the semaphore slot be released before the interval delay?
3. Which waits must receive the stopping token?
4. Why can UNKNOWN contain a previously successful `lastCheck`?
5. Why does `Task.WhenAll` not mean one dedicated thread per target?
6. Why do tests advance a fake clock and then wait for continuations?

Follow [tutorial Part 3](tutorial.md#part-3--run-checks-in-the-background) for a runnable teaching session and [the M2 guide](docs/MILESTONE-2.md) for exact configuration, API fields, and verification evidence.

## Milestone 3 — Entities, contexts, and transactions

### Entity classes map to SQL tables

Read [the entity classes](backend/UptimeUrpo.Api/Persistence/Entities.cs). `MonitoredService`, `MonitoringPeriod`, and `CheckRecord` are ordinary C# classes with properties. EF Core's model maps those properties to columns and identifies primary/foreign keys.

You already understand SQL: `CheckRecord.ServiceId` becomes a foreign key to the service, while `MonitoringPeriodId` identifies the settings active for that observation. `long` corresponds to PostgreSQL `bigint`. Nullable C# fields such as `int? StatusCode` map to nullable columns. UTC `DateTimeOffset` values use PostgreSQL `timestamp with time zone`; PostgreSQL normalizes instants rather than retaining the original time-zone name.

These entities are persistence shapes, not public DTOs. They include URLs and operational fields that do not belong in API responses. The current response records remain separate.

### `DbContext` is one unit of database work

[MonitoringDbContext](backend/UptimeUrpo.Api/Persistence/MonitoringDbContext.cs) inherits EF Core's `DbContext`. Its `DbSet<CheckRecord>` represents the mapped collection of history rows. `OnModelCreating` specifies keys, maximum lengths, indexes, relationships, enum conversion, and a nonnegative-duration constraint.

Compare a context with a PHP ORM unit of work or a Node ORM's transaction-aware session. A context tracks entity instances and pending changes. It is not thread-safe, and it is not the database itself.

`AddDbContext` registers a scoped context. The long-lived worker therefore creates an async DI scope per persistence operation, resolves `IMonitoringStore`, awaits the operation, then disposes that scope. Each parallel target gets a different context while the provider manages database connection pooling underneath.

### Tracking and `SaveChangesAsync`

In [MonitoringStore.SaveAsync](backend/UptimeUrpo.Api/Persistence/MonitoringStore.cs), querying a service returns a tracked entity. Assigning its latest result fields changes the tracked object. `db.Checks.Add(...)` tracks a new history entity. Neither action alone is the final database commit.

`await db.SaveChangesAsync(token)` generates the required SQL and persists those related changes atomically in a transaction. If the history insert violates the duration constraint, the current snapshot update rolls back too. A real PostgreSQL test proves that behavior rather than relying on an in-memory ORM substitute.

The latest snapshot deliberately duplicates a few observation fields. That makes startup restoration cheap and permits raw-history retention without deleting the last known snapshot. The transaction keeps both representations consistent.

### LINQ describes the query before it runs

The store's history method uses `Where`, `OrderByDescending`, `ThenByDescending`, and `Take`. With EF Core's `IQueryable`, these operations build an expression that the provider translates into SQL. `ToListAsync` executes it. `AsNoTracking` avoids change tracking for a read-only result.

This differs from calling `.filter()` on an already loaded JavaScript array: filtering and limiting should happen in PostgreSQL before rows cross the network. In-memory LINQ, used in earlier milestones, executes against objects already in memory; the syntax can look similar while execution is different.

The history query uses `[start, end)` timestamps, a stable secondary ID ordering, and a maximum result count. No HTTP history endpoint exists until M4; the method is currently exercised directly by database tests.

### Migrations are versioned schema changes

Read [the migration directory](backend/UptimeUrpo.Api/Persistence/Migrations/MonitoringDbContextModelSnapshot.cs) alongside `MonitoringDbContext`. The generated migration creates tables, constraints, and indexes. Its model snapshot lets EF compare future model changes with the previous migration model; it is not a backup of live data.

This resembles versioned SQL migration files used in PHP or Node projects. `dotnet ef migrations add` generates a proposed change; review it before applying it. The local helper's explicit migration command applies outstanding changes with the schema owner and exits without starting checks. Normal startup never silently changes the schema.

The migration's `Down` method drops tables. Reversing a schema change does not recover removed data. Backup/restore and production deployment safety remain separate M6 work.

### Prepare, persist, then publish

`MonitoringState.Prepare` calculates the next snapshot/failure count without exposing it. The worker saves it through the scoped store and calls `Apply` only after success. SQL failure leaves the previous memory snapshot unchanged, marks readiness unhealthy, and discards the unsaved observation.

One narrow `IMonitoringStore` interface isolates the worker's persistence boundary for fast tests; `MonitoringStore` uses EF directly. There is no generic repository hierarchy or production memory fallback. Database tests use the real provider in disposable PostgreSQL.

At startup, synchronization restores durable observations and failure streaks. UTC age reconstructs a freshness baseline across processes; old/future-dated records start UNKNOWN. Once running, monotonic time again protects freshness from wall-clock corrections. We cannot persist a meaningful monotonic timestamp across process lifetimes.

### Configuration periods preserve evidence

Changing a service's interval, enabled state, URL, expectation, timeout, or thresholds closes its old period and opens a new one. Display-name changes do not change identity. Removing a service retires it and preserves history.

The partial unique index permits one open period per service. Synchronization closes changed periods before inserting replacements inside one explicit transaction. That ordering matters: it avoids temporarily violating the uniqueness rule. Future coverage calculations can use enabled periods instead of assuming every service was monitored forever.

### Bounded maintenance and readiness

[RetentionWorker](backend/UptimeUrpo.Api/Persistence/RetentionWorker.cs) creates its own scopes and deletes one small batch per minute. `ExecuteDeleteAsync` sends a database-side deletion instead of loading/tracking every old entity. Strictly older-than-cutoff deletion keeps the boundary unambiguous.

Liveness still asks whether the API process responds. Readiness now asks whether storage initialization succeeded, recent writes are healthy, and the migrated schema can be queried. A target failure saved correctly does not make the monitor unready.

### Real database tests

[PostgresFixture](tests/UptimeUrpo.IntegrationTests/PostgresFixture.cs) starts a disposable container, creates restricted identities, and provisions a separate database for each scenario. [DatabaseTests](tests/UptimeUrpo.IntegrationTests/DatabaseTests.cs) applies migrations and exercises actual SQL, transactions, permissions, retention, and restart behavior. Its temporary credentials are generated during the test, not stored in Git.

`await using` ensures asynchronous cleanup of connections, contexts, scopes, transactions, and containers. `CancellationToken` flows into queries and saves too, just as it did for HTTP requests.

### Explain it back

1. What does a `DbContext` track, and why must parallel checks not share one?
2. When do property assignments become committed SQL writes?
3. Why is the memory snapshot updated after the database save?
4. How can raw history expire while the last known snapshot still exists?
5. Why are migration and runtime database identities different?
6. Why do real PostgreSQL tests matter for a transaction or permission rule?

Follow [tutorial Part 4](tutorial.md#part-4--keep-monitoring-history-in-postgresql) and [the M3 run/verification guide](docs/MILESTONE-3.md).

## Future entries

- M4: DTOs, validation, query composition, API errors, and availability calculations.
- M5–M7: frontend integration, production configuration, operations, and automated delivery.

Add examples and lessons only when introduced. Mark implemented examples with source links and verification commands; keep unimplemented ideas labeled as previews.
