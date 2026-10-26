# Milestones and acceptance criteria

Work one milestone at a time. Approval of the roadmap is not permission to bypass the approval gate between milestones. M0–M3 are complete and locally verified; M4–M8 are planned. See the dated records for [M0](LOCAL-DEVELOPMENT.md#milestone-0-verification-record--2026-10-25), [M1](MILESTONE-1.md#verification-record--2026-10-25), [M2](MILESTONE-2.md#verification-record--2026-10-25), and [M3](MILESTONE-3.md#verification-record--2026-10-26).

At every milestone:

1. Explain unfamiliar concepts before introducing them in application code.
2. Implement only the approved milestone, with meaningful automated checks where applicable.
3. Update the README, architecture decisions, and `CSHARP-FOR-ROOPE.md` using actual code examples once code exists. Expand the corresponding part of [the tutorial](../tutorial.md) with what was built, a walkthrough, verification, and teaching prompts.
4. Report what was built, what was learned, commands/results, and unverified limits.
5. Stop for approval before starting the next milestone.

## M0 — Architecture and learning setup

Deliver architecture, repository layout, prerequisites, roadmap, risk documentation, SDK selection, and the first five C# concepts. Install the ARM64 SDK locally and verify Docker availability. Do not scaffold a solution, application, database, frontend, CI, or deployment.

Acceptance: `sh scripts/check-prerequisites.sh` selects the pinned SDK and reports a reachable Docker engine; documentation explains how to reproduce the setup. Record actual evidence in the local development guide. This milestone has prerequisite/documentation checks, not application tests.

## M1 — First C# API

Status: complete. The one-shot startup hook implements `IHostedService`; it has no scheduler. OpenAPI remains in M4. The committed target is disabled; real local settings stay ignored and are excluded from build/publish output.

Create the solution, one ASP.NET Core Minimal API project, and xUnit unit/API integration test projects. Introduce typed results/DTOs, configuration, dependency injection, `IHttpClientFactory`, async/cancellation, and the outbound security boundary. Use one approved HTTPS target, one startup check, `/health`, and a read endpoint for its saved result. Reads do not initiate checks. Use no scheduler or database.

Acceptance: `dotnet build` and `dotnet test` pass. Deterministic tests cover exact expected/unexpected HTTP status, DNS/connection/TLS failure categories, timeout versus shutdown cancellation, safe destinations/redirect behavior, and JSON contracts. Tests do not require public internet. Demonstrate startup and read behavior separately against the approved real target, identifying it as an external smoke check.

## M2 — Background monitoring

Status: complete. Fixed-delay per-target loops run under a shared concurrency limit. Current snapshots expose health, freshness, and failure streaks; both the new target list and legacy single-target settings are supported. In-memory state remains intentionally temporary. See [the implemented behavior and tests](MILESTONE-2.md).

Introduce `BackgroundService`, validated multiple-target configuration, per-target scheduling/timeouts, concurrency limit, health state transitions, structured logs, and graceful shutdown. Results and state are in memory; document restart loss.

Acceptance: controlled-time tests demonstrate no overlapping target checks, global concurrency bounds, independent target progress, no catch-up bursts, failure threshold, recovery, stale UNKNOWN, and cancellation. A failing target does not stop the scheduler. Shutdown cancellation does not manufacture a DOWN result.

## M3 — PostgreSQL persistence

Status: complete. Explicit migrations, scoped EF contexts, durable results/current snapshots, configuration periods, retention, readiness, and disposable PostgreSQL integration tests are implemented. Raw history can be queried through the tested store method; HTTP history and incidents remain M4. See [the implementation and operational instructions](MILESTONE-3.md).

Verify and pin EF Core/Npgsql packages. Add entities, migrations, scoped database access, durable results/state, monitoring configuration periods, indexes, retention, and database-aware readiness. Introduce local PostgreSQL Compose configuration with isolated project resources and loopback-only access if a host port is needed for native .NET development. Database integration tests use disposable containers.

Acceptance: disposable PostgreSQL tests migrate an empty database, store and query results, preserve relevant state across app restart, enforce retention boundaries, and exercise database failures. Migration commands and local volume removal consequences are documented. No NUC deployment occurs.

## M4 — Monitoring API

Complete the read-only endpoints, durable incident transitions, observed availability and coverage, pagination, bounded history/aggregation, validation, cancellation, Problem Details, and OpenAPI. Lock the endpoint/query DTOs and coverage opportunity mapping in this milestone's implementation plan.

Acceptance: unit/database/API tests cover first failure through DOWN and recovery, one open incident, atomic result/state writes, gaps and stale results, disabled/configuration periods, window edges, no observations, slow successes, pagination, invalid ranges/IDs, and sanitized errors. API reads never cause target requests.

## M5 — React dashboard

Verify/pin React, TypeScript, Vite, and supported Node tooling; add frontend checks and lockfiles. Build a five-second overview, detail/history charts, recent incidents, current incident duration, themes, and responsive accessible controls. Use a small chart dependency only if it materially helps hover, touch, keyboard access, and gaps.

Acceptance: frontend checks/build pass. Browser verification covers loading, empty, error, stale and gap states, themes, keyboard operation, responsive layouts, chart interaction, and comprehensible status without color alone. State explicitly whether physical-device touch testing occurred. The API/UI remain private by design; no deployment yet.

## M6 — Docker and NUC operations

Inspect the actual host and existing proxy/access services before proposing changes. Build a production image serving frontend and API, plus isolated PostgreSQL Compose configuration. Add non-root execution, health/readiness checks, restart behavior, resource and log limits, secrets handling, backup/restore, and a release script with backup-before-migration.

Acceptance: local image checks pass; after an explicitly authorized deployment, verify the Intel NUC origin, private access layer, no origin bypass, no public PostgreSQL port, restart continuity, graceful shutdown, and restore into a separate database. Record each verification separately. Do not change unrelated services, publish publicly, or invent host details.

## M7 — CI and delivery reliability

Add GitHub Actions for backend build/tests, disposable PostgreSQL integration tests, frontend checks/build, and reproducible release images. Deployment is explicitly triggered, with appropriate secret handling, required checks, backup-before-migration, and recovery instructions.

Acceptance: failing validation blocks delivery; reproduce the pipeline from a clean checkout. Exercise a safe failed-release scenario and recovery, distinguishing image rollback from database restore. Document which deployment actions actually ran; do not claim automation from a local script alone.

## M8 — Optional Azure and Infrastructure as Code

Explain subscriptions/resource groups, identities, networking, compute, storage/database, and Azure/AWS parallels. Prefer Bicep for the Azure-focused exercise. Design a small deployment, budget alerts, and repeatable teardown before provisioning.

Acceptance: reviewed cost estimate and infrastructure plan first. Paid provisioning requires explicit approval; budget alerts are not spending caps. If approved later, verify provisioning, security, health, teardown, and residual billable resources. Keep all credentials out of Git.

## Professional learning evidence

Each milestone should produce something explainable in an interview: a concrete design decision, the relevant code path, a meaningful test, and an honest limitation. The sequence demonstrates typed backend development, asynchronous I/O, dependency lifetimes, SQL modeling, migrations, API design, frontend integration, threat modeling, container operations, and recovery planning.

Do not attach calendar deadlines or describe planned capabilities as completed features.
