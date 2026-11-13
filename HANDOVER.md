# Burndown rewrite – handover

Read this first. Update it at the end of every milestone. No secrets, tokens or real financial data here.

## Status

| Milestone | Status |
|-----------|--------|
| Planning (AGENTS.md, PLAN.md) | Reviewed twice; all owner questions answered (D12, D15, D21); ready for M0 |
| M0 Scaffolding and tooling | Delivered, awaiting owner review |
| M1 Firefly client and test infrastructure | Delivered, awaiting owner review |
| M2 Overview back-end | Delivered, awaiting owner review |
| M3 Overview UI and chart | Not started |
| M4 Entry back-end | Not started |
| M5 Entry grid UI | Not started |
| M6 Browser tests and hardening | Not started |
| M7 Packaging | Not started |
| M8 Parity review and removal of old app | Not started |

## Delivered so far

- `AGENTS.md` rewritten for this project (`AGENTS.template.md` keeps the original template).
- `PLAN.md` with decisions D1–D21, final configuration shape, binding Overview and Entry specifications (formulas,
  colours, chart, per-type fields, timestamps, submit semantics) and milestones M0–M8. Audited against the old code for
  ambiguities and contradictions; a second review settled the points listed under Learnings. The owner answered its
  three questions: Asset accounts only (D12), last-use ranking in server process memory (D15), no paste (D21).
- Owner-created and committed: `.editorconfig`, `.csharpierrc.yaml`.

### M0 – Scaffolding and tooling (delivered)

- Root: `global.json` (SDK 10.0.200, MTP runner), `NuGet.config`, `dotnet-tools.json` (CSharpier 1.3.0),
  `.csharpierignore`, `.gitattributes`, `.dockerignore`, `.gitignore` additions. `src/Directory.Build.props` and
  `src/Directory.Packages.props` apply only to projects named `Burndown.*` (via a `MSBuildProjectName.StartsWith`
  condition); the old `src/Burndown` still builds unchanged.
- Projects in `src/Burndown.sln`: `Burndown.Common` (`Result`, `Error`, `AppClock`), `Burndown.Firefly` (options),
  `Burndown.Overview` and `Burndown.Entry` (options + validators + module registration), `Burndown.Web` (host),
  `tests/Burndown.UnitTests`, `tests/Burndown.ArchitectureTests`.
- Host: Kestrel on `127.0.0.1:9142` unless `urls` is set, network-free options validation at startup (fail fast, token
  never logged), Serilog, `AllowedHosts` restriction, Origin/Content-Type protection for non-GET `/api` requests (Vite
  origin in Development only), `/health/live`, `/health/ready` (stub in M0; real since M1), `--health-check`,
  `/api` 404 and SPA fallback.
- Client `src/Burndown.Client`: Vite + React 19 + TypeScript strict + Tailwind 4 + TanStack Query + Biome + Vitest; dark
  theme tokens; placeholder page showing the `/health/live` status; builds into `src/Burndown.Web/wwwroot`; dev server
  `127.0.0.1:5173` proxying `/api` and `/health` to the BFF.
- Checks: `dotnet build src/Burndown.sln -warnaserror` 0 warnings; XML-doc build 0 Roslynator warnings in new
  projects; CSharpier check clean; 79 .NET tests pass (71 unit, 8 architecture); client `npm ci`, `lint`, `build`
  and `test` (2 tests) pass. Smoke test of the built host with a fake token: live 200, ready 503, unknown `/api` 404,
  foreign Origin POST 403, foreign Host 400, `--health-check` exit 0.

### M1 – Firefly client and test infrastructure (delivered)

- `Burndown.Firefly`: Vogen ids (`AccountId`, `BudgetId`, `TransactionId`), `IFireflyClient` + `FireflyClient` (typed
  `HttpClient`, Bearer token, `Firefly:TimeoutSeconds`, no redirects, no retries), full pagination via
  `meta.pagination` (guard: 1000 pages, page size 100), typed models, wire DTOs and mapper (System.Text.Json only),
  error mapping (`firefly.unauthorized/forbidden/unavailable/timeout/validation/invalid_response/unexpected`; 422 bodies
  become per-field messages, `transactions.N.` prefix stripped), `FireflyReadinessCheck`. Caller cancellation
  propagates; only the client's own timeout maps to `firefly.timeout`. Logs carry method, path (no query) and status only.
- `Burndown.Entry`: `EntryConfigurationCheck` (configured cash account and budgets exist in Firefly). Both checks are
  tagged `HealthTags.Ready` and drive `/health/ready`. `Error` gained an optional `Fields` dictionary.
- Tests: `tests/Burndown.UnitTests/Firefly` (stubbed handler: status mapping, timeout, cancellation, pagination,
  mapping, 422 normalisation, create JSON shape, no retry on create, log hygiene, readiness checks) and the new
  `tests/Burndown.CharacteristicsTests` (15 tests, trait `Category=Characteristics`) against a real throwaway Firefly.
- Checks: `-warnaserror` build clean; XML-doc build has no Roslynator warnings in new projects; CSharpier clean;
  120 fast tests (unit + architecture) and all 15 characteristics tests pass (135 total).
- How to run: fast subset `dotnet test --solution src/Burndown.sln --filter-not-trait "Category=Characteristics"`;
  container tests `dotnet test --project src/tests/Burndown.CharacteristicsTests/Burndown.CharacteristicsTests.csproj`
  (needs Docker, about 20 s: the container is ready about 13 s after start; one container is shared by all tests).
- Throwaway Firefly recipe (`Infrastructure/FireflyFixture.cs`, `bootstrap.php`): image `fireflyiii/core:version-6.6.6`,
  SQLite (`DB_CONNECTION=sqlite`), random 32-char `APP_KEY`, `TZ=Europe/Brussels`, `TRUSTED_PROXIES=**`, port 8080,
  wait for the log line `NGINX + PHP-FPM is running correctly`. `bootstrap.php` is copied into the container and run with
  `php`; it creates a user, the passport personal-access client and a token, and prints `TOKEN:<jwt>`. Seeds: Checking,
  Savings, an Employer revenue account, budgets David and Tina, and one withdrawal (which creates the cash account).
  Rancher Desktop and Ryuk work without extra settings; the Ryuk helper container may stay listed in `docker ps -a`.
- Firefly behaviour findings (D12, description max 1000, 401 for missing resources, 3-decimal amounts, cash-account
  id, accounts filtered client-side) are recorded in `PLAN.md` under M1.
- Not done on purpose: no BFF endpoints yet (M2/M4); the client has no write method other than `CreateTransactionAsync`.

### M2 – Overview back-end (delivered)

- `Burndown.Overview`: `OverviewCalculator` (pure; budget, daily series, colour data, chart), `OverviewService`
  (validation, two parallel Firefly calls), `OverviewEndpoints` (`GET /api/overview`), `OverviewContract` (internal
  JSON records, camelCase), module wiring; `Program.cs` calls `api.MapOverviewEndpoints()`. The project now uses the
  ASP.NET shared framework (explicit Microsoft.Extensions package references trigger NU1510).
- `Burndown.Common.ErrorCodes.Validation`; `FireflyErrorCodes.ToHttpStatusCode`. Details and decisions in `PLAN.md` (M2).
- Tests (`tests/Burndown.UnitTests/Overview`, plus `Firefly/FireflyErrorCodesTests`): calculator (month lengths 28-31,
  leap February, zero income, negative budget, override incl. 0, exclusions, descriptions, weekends, today/past/future
  month, colour and amount ranges, rounding, two parity goldens from the old `DataProcessor`), service (default month,
  zone vs UTC midnight, range and override validation, two calls only, failure propagation), endpoints (JSON shape,
  400 with field errors, 502/503/504 mapping, POST refused). `ErrorCodes` added to the `LayerTests` allow-list.
- Checks: `-warnaserror` build clean; XML-doc build no new warnings; CSharpier clean; 191 fast tests pass. The
  container-based characteristics tests were not changed (the date-range and pagination behaviour the overview relies
  on is already covered there); no Overview-specific container test was added.
- Not done on purpose: no client code (M3); no caching of overview data.
- Note: `tests/Burndown.CharacteristicsTests/Infrastructure/bootstrap.php` (needed by `FireflyFixture`) shows as
  untracked: it was missed in the M1 commit and should be added to the next commit.

## Next: M3 – Overview UI and chart

See `PLAN.md`. Consume `GET /api/overview` (contract in `Burndown.Overview/OverviewContract.cs`); pick the chart
library (Recharts recommended); month navigation, override input, polished table, accessible chart data table, states
for loading/error (the response carries `code` and `detail` for 400/502/503/504). No open owner questions.

## Environment facts

- Branch: `rework`. Git root `C:\David\Programmatuur\Burndown`. Solution: `src\Burndown.sln` (old project `Burndown` plus,
  later, the new `Burndown.*` projects).
- Toolchain: .NET SDKs 8, 9 and 10.0.200, Docker 29.6 (Rancher Desktop), Node 24.19.
- Real Firefly III: `http://localhost:8042` (real data: read-only unless the owner asks, never written to by an agent).
- Old app: port 9042. New app: port 9142 until the old one is removed.
- Reference repos: `C:\_git\MovieDB`, `C:\_git\Motorsports` (never read their `prompt-history*` files).
- Uncommitted owner changes: `src/Burndown/appsettings.Development.json` (contains the old OAuth secret: never touch,
  stage or copy it). `src/Burndown.sln` now includes the new projects (uncommitted).

## Learnings

- M0: `dotnet test --nologo` silently runs zero tests in MTP mode (exit code 5); omit the flag. Run a built host from
  its output folder. The Vite build output in `wwwroot` is picked up by the Development static-web-assets manifest, so
  host tests assert on `<html`, not on test-specific content. Client tool versions are pinned by `package-lock.json`
  (TypeScript kept at 5.9 for tooling safety). `Vogen` is used for the Firefly ids since M1; the Vogen-generated public types are allow-listed in `LayerTests`.
- M1: Firefly date-range tests need DST-correct offsets (build `DateTimeOffset` through `TimeZoneInfo`, not a fixed `+01:00`).
  The characteristics tests never touch the real Firefly (a throwaway container on a random port).

- The old app has a quote-injection bug (string-built JSON), an unvalidated OAuth `state`, `limit=999` with no paging,
  and direct `DateTime.Now` use. Do not port those patterns.
- The old overview used UTC for "today"; the plan uses the configured zone. Deposits are never split, so the old
  first-split-only income sum gives the same results.
- Selectable accounts are Asset accounts only (D12, owner decision; the old app also offered Revenue accounts, which
  Firefly would reject in every current field). Verify the asset-only rule against the throwaway Firefly in M1 and map
  any remaining 422 to the account field.
- The owner enters rows by hand from a phone app: no paste (D21). The old app's reference data is ranked only
  alphabetically; the new one ranks by last use from an in-memory tracker (D15).
- Owner confirmed: time-of-day rule, date range (`MaxBackdatedMonths` 2, up to tomorrow) and the two-line chart.
- Owner decisions: desktop-first (D17), dark theme only (D18), frontend stack details delegated (D19), entry grid
  design to be tried and iterated on (D20); all remaining defaults in `PLAN.md` accepted.
- The old `docker-compose.yml` and `appsettings.Development.json` contain a Firefly OAuth client secret. Owner should
  rotate it.
- Second plan review, resolved without the owner (technical facts or internal contradictions): inside the container
  Kestrel must listen on `http://+:9142` (loopback-only binding applies to direct runs; compose restricts exposure); the
  Dockerfile lives at `src/Burndown.Web/Dockerfile` (a root `Dockerfile` would collide with the old `dockerfile` on
  Windows); `global.json` must select the MTP test runner; "now" and the date bounds come from the server;
  description prefill is optional (D16, AGENTS.md aligned); the same-date timestamp bump compares with the nearest
  preceding row on the same date and the result carries the effective timestamp; DST gaps are rejected and overlaps
  take the first occurrence; alphas are rounded to 2 decimals for parity with the old output; day/month names are
  English with `nl-BE` numbers (one shared formatter, no ambient locale).
