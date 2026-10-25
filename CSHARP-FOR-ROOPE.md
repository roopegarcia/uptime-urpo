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

### Entry point, namespaces, and project files

Start with [Program.cs](backend/UptimeUrpo.Api/Program.cs). Its top-level statements are the executable entry point; the compiler supplies the surrounding `Main` method. C# files elsewhere use `namespace UptimeUrpo.Api.Monitoring;` to organize type names. A `using UptimeUrpo.Api.Monitoring;` directive makes those names available without repeating the full namespace. This resembles importing names in TypeScript, though a C# `using` directive does not execute a JavaScript module.

The `.csproj` file declares the target framework (`net10.0`), nullable-reference checking, and implicit common namespace imports. `Uptime-Urpo.slnx` groups the API and test projects; it does not define a deployment. [Directory.Build.props](Directory.Build.props) applies shared settings to all projects: warnings are errors and dependency lockfiles are generated.

### Configuration becomes a typed object

[TargetOptions](backend/UptimeUrpo.Api/Monitoring/TargetOptions.cs) is a class with properties. For example, `public int TimeoutSeconds { get; set; } = 10;` declares an integer property, permits reading/writing, and supplies a default. The configuration binder fills these properties from `Monitoring:Target`.

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

Read [HttpChecker's constructor](backend/UptimeUrpo.Api/Monitoring/HttpChecker.cs). It requires `IHttpClientFactory`, `IOptions<TargetOptions>`, and `TimeProvider`. The framework supplies these because `Program.cs` registers them.

`AddSingleton<StartupCheckState>()` creates one state object for the host. `AddTransient<HttpChecker>()` creates a checker whenever resolved. The one-shot startup hook receives one checker and retains it for its own lifetime. No database scopes exist yet.

The named HTTP client registration sets shared policy, including the user agent and transport handler. A short-lived `HttpClient` created by the factory uses managed underlying handlers; disposing the client does not mean creating a new TCP connection for every dependency resolution.

### `async`, tasks, and cancellation in a real request

The checker method is `public async Task<CheckResult> CheckAsync(CancellationToken cancellationToken)`. It awaits `SendAsync`, which yields during network I/O. Compare this with awaiting `fetch` in JavaScript; the caller receives a task representing the eventual result.

A linked `CancellationTokenSource` combines the host's cancellation with the check timeout. The checker distinguishes them:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // Application shutdown is not evidence that the monitored site failed.
    throw;
}
```

`when` is an exception filter. Bare `throw;` rethrows while preserving the original exception context. Timeout cancellation is handled separately and returns a failed observation. Expected `HttpRequestException` failures become categories; unexpected programming errors are not swallowed by a blanket catch.

`Stopwatch` measures elapsed duration; `TimeProvider.GetUtcNow()` supplies the observation timestamp. Response timing ends at headers, not at complete body download.

### Two meanings of `using`

At the top of a file, `using` imports a namespace. Inside the checker, `using var response = ...;` declares a disposable resource. It calls `Dispose` when execution leaves that scope, including when an exception is thrown. This is comparable to `try/finally` cleanup in JavaScript or scoped resource management in PHP.

The client, request, response, and timeout source are disposed. Production socket streams own their sockets so disposal releases the connection. The handler's zero drain limit prevents response disposal from reading a body just to reuse the connection.

### Interfaces, delegates, and the connection boundary

[IDnsResolver](backend/UptimeUrpo.Api/Monitoring/IDnsResolver.cs) is a small interface for real DNS work. Tests supply controlled answers. This boundary lets us test rebinding without depending on real DNS changes.

[PublicHttpsConnector](backend/UptimeUrpo.Api/Monitoring/PublicHttpsConnector.cs) accepts a `Func<IPEndPoint, CancellationToken, ValueTask<Stream>>` in an internal test constructor. `Func` describes a callable value, comparable to a TypeScript function type. Tests supply a function that records which IP would be connected instead of opening a public socket. The public production constructor supplies the real socket function.

`internal` limits that constructor to this assembly and the explicitly named friend test assemblies. It is not an HTTP-accessible bypass. `ValueTask<Stream>` is the callback-compatible asynchronous return type used here; prefer ordinary `Task` elsewhere unless an API or measured need calls for `ValueTask`.

Collections such as `addresses.Any(...)` and `.Distinct()` use LINQ to inspect in-memory values. Here they reject unsafe answers and avoid duplicate connection attempts. These are not database queries; EF Core's LINQ translation comes in M3.

### Hosting and JSON responses

[StartupCheckService](backend/UptimeUrpo.Api/Monitoring/StartupCheckService.cs) implements `IHostedService`. The host awaits `StartAsync` during startup and calls `StopAsync` during shutdown. This hook performs one check and returns; it has no loop. M2 will introduce `BackgroundService` for recurring work.

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

## Future entries

- M2: `BackgroundService`, lifecycle, concurrency, time, and state transitions.
- M3: EF Core entities, scoped contexts, LINQ, migrations, transactions, and PostgreSQL tests.
- M4: DTOs, validation, query composition, API errors, and availability calculations.
- M5–M7: frontend integration, production configuration, operations, and automated delivery.

Add examples and lessons only when introduced. Mark implemented examples with source links and verification commands; keep unimplemented ideas labeled as previews.
