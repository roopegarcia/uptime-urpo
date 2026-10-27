# Milestone 5 — React monitoring dashboard

Status: complete, locally verified on 2026-10-27. No NUC deployment, access-layer changes, database migration, or target changes occurred.

## What was built

- Overview with overall health, explicit UP/DOWN/DEGRADED/UNKNOWN labels, disabled services, search, and an attention filter.
- Service details with 24-hour, seven-day, and 30-day statistics. Observed availability and coverage remain separate; absent observations have no percentage.
- SVG response chart with actual bucket times, average/min/max inspection, failed-check strip, explicit missing/not-monitored buckets, keyboard range control, pointer/touch handlers, and a semantic data table. Lines never bridge missing response times. A failed check without HTTP headers has no response-time point.
- Current incidents with elapsed time since first failure, confirmation/end timestamps, gap uncertainty, recovery/configuration-change labels, and cursor pagination. Loading older pages survives a time-window change and regular incident refreshes.
- Light/dark themes with system preference initially and a persisted explicit choice; blocked localStorage does not break theme controls. Responsive layouts, skip link, focus styles, reduced-motion support, and status text independent of color.
- Loading/error/empty states, bounded cancellable reads, visible stale snapshots, and same-origin static file support in ASP.NET Core.

## Start locally

Keep the existing API running on loopback port 5080. From the repository root in another terminal:

```sh
npm --prefix frontend ci
npm --prefix frontend run dev
```

Open **http://127.0.0.1:5173/**. Vite proxies `/api` and `/health` to port 5080. No CORS rule or public listener is needed. Start the API separately with `python3 scripts/dev-db.py run` if it is stopped. The API still needs its configured database.

Use `#/services/{id}` links for details. Hash routing keeps reloads and browser back/forward simple without a server catch-all that could hide API errors. The application has no URL-entry form or built-in authentication; loopback remains a development boundary.

## Source walkthrough

- [App.tsx](../frontend/src/App.tsx): overview, details, filters, routing, incidents, and explicit view states. Components receive safe DTOs, not target addresses.
- [types.ts](../frontend/src/types.ts): TypeScript read contracts matching the C# records. Types are compile-time checks, not runtime JSON validation.
- [api.ts](../frontend/src/api.ts): same-origin GETs, status-specific safe errors, JSON content-type guard, and server-derived history bounds. Normalize both bounds to milliseconds; .NET's additional precision must not turn 30 days into 30 days plus a fraction.
- [hooks.ts](../frontend/src/hooks.ts): abort on timeout/unmount/window change, no overlapping poll, hidden-tab pause, refresh on return, cached data on refresh failure, and resilient themes.
- [ResponseChart.tsx](../frontend/src/ResponseChart.tsx): separate polylines for contiguous HTTP observations, actual UTC spacing, keyboard inspection, and table alternative. A small SVG implementation avoids an additional chart dependency for this bounded graph (API maximum 1,000 buckets).
- [styles.css](../frontend/src/styles.css): shared theme variables and responsive rules. No external font, image, analytics, or CDN request is required.
- [DashboardFiles.cs](../backend/UptimeUrpo.Api/Endpoints/DashboardFiles.cs): standard default/static-file middleware. Native development can serve `frontend/dist`; published assets live under `wwwroot`.

Overview polls every 30 seconds. History and first-page incidents poll independently every 60 seconds. At most three automatic initial reads are in flight for one tab, below the API's shared four-reader limit; multiple tabs/users can still receive 429. Polls have no overlap or immediate retry, a ten-second timeout, and pause while hidden. Manual refresh buttons are disabled while their request runs. The header Refresh updates status/statistics; Refresh history updates the chart; incident errors provide Retry incidents.

A failed overview refresh retains the previous data but labels health as **last known**, neutralizes its color, and shows an alert. A snapshot older than 90 seconds is also flagged. This UI freshness is separate from the server's target UNKNOWN calculation. History/incident failures retain their own earlier results with an alert. Reads never trigger checks.

Dates display in the browser's time zone, with zone abbreviations and seconds; API values remain UTC. Windows are elapsed UTC hours (24/168/720), not local calendar-day ranges. Incident elapsed time is not proven continuous downtime, especially across gaps.

## Build and publish preparation

```sh
npm --prefix frontend run build
"$HOME/.dotnet/dotnet" publish backend/UptimeUrpo.Api -c Release --no-restore -o .local/m5-publish
```

The project includes built assets under `wwwroot` in publish output. Publish fails with a clear instruction if `frontend/dist/index.html` is absent. Build the frontend before publishing every release; presence alone does not prove assets are current. Node is needed for building, not for serving the published application.

Native API processes need restarting to load the new static-file middleware. The user's existing process was preserved during M5; Vite already works with its API. No second monitor was started against the development database. Production images, access checks, and deployment belong to M6.

## Verification record — 2026-10-27

Selected and locked: Node 26.9.0 (`frontend/.nvmrc`), npm 12.2.0, React/React DOM 19.3.1, TypeScript 7.0.2, Vite 8.3.5, React plugin 6.1.2, and Vitest 5.0.5. See exact remaining versions in package.json and package-lock.json. Vite's [documented Node requirement](https://vite.dev/guide/) and [React releases](https://github.com/facebook/react/releases) were checked; the installed versions were verified against npm registry metadata. npm reported zero dependency vulnerabilities at installation; that is a dated check, not a security guarantee. Optional fsevents install scripts were blocked by the local npm policy; builds, tests, and Vite hot reload still worked.

Commands:

```sh
npm --prefix frontend ci
npm --prefix frontend run build
npm --prefix frontend test
npm --prefix frontend run format:check
"$HOME/.dotnet/dotnet" test Uptime-Urpo.slnx --no-restore
"$HOME/.dotnet/dotnet" format Uptime-Urpo.slnx --verify-no-changes --no-restore --verbosity quiet
"$HOME/.dotnet/dotnet" publish backend/UptimeUrpo.Api -c Release --no-restore -o .local/m5-publish
git diff --check
```

Results: 25 frontend tests; 144 backend unit and 55 integration cases (199 backend, 224 combined). Frontend type-check/production build and backend publish passed. Static-serving integration tests use isolated HTML/JS fixtures and prove `/`, assets, cache headers, liveness, API 404 Problem Details, and missing-asset 404 without a database. The existing database tests still use disposable PostgreSQL containers, not the development database.

Browser checks used Chrome on the Mac:

- Real local API: overview, service detail, 24-hour and 30-day histories, real gaps, and the distinction between 100% observed availability and incomplete coverage.
- Isolated synthetic harness: all four statuses plus disabled monitoring, open/closed incidents, gap uncertainty, load-older pagination, loading, empty workspace, initial 503, failed refresh with retained stale data, and history containing only missing observations.
- Both themes; keyboard Home/ArrowRight chart inspection and Enter theme activation; pointer chart selection; expanding the data table. Component tests additionally cover filters, focus retention, missing route, skip-link navigation, timeouts, cancellation, hidden tabs, and storage failure.
- Viewports 320, 390, 768, and 1440 pixels: inspected layouts and checked document width for overflow; no horizontal page overflow in the checked detail view. A cramped 320-pixel header was corrected.

Physical devices, real touch gestures, Safari/Firefox, and a screen-reader session were **not tested**. Pointer/touch handlers and responsive viewport checks do not constitute physical-device evidence. No production image/runtime, NUC origin, private access layer, or public host was verified.

## Reproduce synthetic browser scenarios

```sh
npm --prefix frontend run build
npm --prefix frontend run test:preview
```

Open `http://127.0.0.1:5181/?scenario=signals`. Other scenarios: `empty`, `error`, `stale` (press Refresh once), `missing`, and `loading`. Restart the harness to reset stale-scenario request counters. It serves built assets and synthetic DTOs only; it does not use PostgreSQL or make monitoring requests. Fixtures are absent from the application bundle and publish output. Stop the harness with Ctrl+C afterward. Running the harness directly requires the pinned Node version's TypeScript stripping support.

See [tutorial Part 6](../tutorial.md#part-6--build-the-react-dashboard) for the teaching walkthrough. M6 requires the next approval and host/access inspection first.
