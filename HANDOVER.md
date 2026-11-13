# Burndown rewrite – handover

Read this first. Update it at the end of every milestone. No secrets, tokens or real financial data here.

## Status

| Milestone | Status |
|-----------|--------|
| Planning (AGENTS.md, PLAN.md) | Reviewed twice; all owner questions answered (D12, D15, D21); ready for M0 |
| M0 Scaffolding and tooling | Delivered, awaiting owner review |
| M1 Firefly client and test infrastructure | Delivered, awaiting owner review |
| M2 Overview back-end | Delivered, awaiting owner review |
| M3 Overview UI and chart | Delivered, awaiting owner review |
| M4 Entry back-end | Delivered, awaiting owner review |
| M5 Entry grid UI | Delivered, awaiting owner review |
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

### M3 – Overview UI and chart (delivered)

- Client (`src/Burndown.Client/src`): `features/overview/` (`OverviewPage`, `MonthNav`, `OverrideField`,
  `SummaryCards`, `DayTable`, `BurndownChart`, `ErrorPanel`, `api`, `colors`, `override`), `lib/format.ts` (the only
  place that formats money/dates), `lib/months.ts`, `hooks/useMonthKeys.ts`. `App.tsx` renders the page. Chart library:
  Recharts (decision recorded in `PLAN.md`). See the M3 notes in `PLAN.md` for behaviour.
- Tests: Vitest 61 tests in 6 files (format, months, colours, override parsing, month keys hook, 12 page tests with a
  stubbed fetch incl. 401/503/network errors and the empty month). New project `tests/Burndown.BrowserTests`
  (Playwright for .NET, trait `Category=Browser`): real Kestrel listener, fake Firefly client, fixed clock; 3 tests
  (render and no horizontal overflow at 1440 and 1024 px, keyboard month navigation). Set `BURNDOWN_SCREENSHOT_DIR` to
  also write full-page screenshots. Both viewports were inspected visually and look right.
- Checks: `-warnaserror` build clean; CSharpier clean; Biome clean; `npm run build` OK; 194 .NET tests pass (unit,
  architecture, browser); characteristics tests were not changed.
- Prerequisites for the browser tests: build the client first (`npm run build` in `src/Burndown.Client`) and install the
  browser once after a build: `pwsh src/tests/Burndown.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium`.
  Exclude them with `--filter-not-trait "Category=Browser"` if those are missing. The "Zero tests ran" line for the
  characteristics assembly in the fast subset is expected (it filters them all out; exit code 8).
- **Follow-ups (owner review, see `PLAN.md`):** chart line renamed to "Break-even pace" (`ideal` → `breakEven` in the
  contract), taller chart table, new year block (`YearOverviewService`, `GET /api/overview/year`, `YearBlock.tsx`;
  two Firefly calls). Tests added: 7 year-service unit tests, 2 endpoint tests, 5 page tests, 1 format test, year block
  assertions in the browser tests. Checks after the follow-ups: `-warnaserror` build, CSharpier, Biome, `npm run build`,
  67 Vitest tests and the fast .NET suite (unit, architecture, browser) all green.
- **Fixed (owner report):** stepping through months quickly logged 500s with `TaskCanceledException`. TanStack Query aborts the superseded browser request, which cancels the in-flight Firefly calls; the cancellation escaped as an unhandled exception. New `RequestAbortedMiddleware` (before origin protection) turns a cancellation of an aborted request into status 499 without an exception; cancellations of live requests and other exceptions still propagate. 3 unit tests. The earlier "server not reachable" report was withdrawn by the owner.
- Not done on purpose: no Entry UI (M5); full browser flows against a throwaway Firefly are M6.

### M4 – Entry back-end (delivered)

- Code (`src/Burndown.Entry`): `EntryContract`, `EntryReferenceService` (+ `EntryDates`), `AssetAccountCache`,
  `EntryUsageTracker`, `DescriptionSuggestionService`, `EntryRowValidator`, `TimestampSequencer`, `EntrySubmitService`,
  `EntryEndpoints`; registered in `EntryModule`, mapped in `Program.cs`. The endpoint and status contract is in the
  "M4 as delivered" part of `PLAN.md`.
- Tests: 104 new fast tests (validator, sequencer incl. DST, submit service, cache TTL/refresh/failure, usage tracker,
  reference and suggestion services, endpoints incl. Origin and content-type refusal) and 3 new characteristics tests
  plus a plain `ThrowawayGuard` test class. Checks: `-warnaserror` build, XML-doc build (no new warnings), CSharpier
  check clean; fast suite 312 passed; characteristics 25 passed.
- **Write safety (PLAN.md "Write safety") – which layer wrote where:** unit, endpoint and architecture tests used the
  in-process `RecordingFireflyClient`, no network. Characteristics tests wrote only to the Testcontainers throwaway
  Firefly (random local port; `ThrowawayGuard` refuses port 8042 and non-local hosts, and the fixture refuses
  `Firefly__*` environment overrides; both verified at run time, the environment variables were empty). The browser
  tests use a fake client and were not changed. The agent did not start the host with a real token and sent no request
  to `localhost:8042`.
- Not done on purpose: no UI (M5); description prefill (D16) is not built.

### M5 – Entry grid UI (delivered)

- Code (`src/Burndown.Client/src/features/entry/`): pure logic (`when`, `amount`, `matching`, `model`, `gridReducer`,
  `api`), `Combobox`, `GridRow`, `EntryPage`, `ShortcutLegend`; `src/hooks/useDebouncedValue.ts`; `App.tsx` now has the
  Overview/Entry nav. Overview's `toApiError` is exported and `ErrorPanel` takes a `fallbackTitle`.
- Shortcut map, layout and the Description-focus deviation: see "M5 as delivered" in `PLAN.md`. All open to feedback.
- Biome: `biome.json` turns off `a11y/useSemanticElements` and `useFocusableInteractive` for `GridRow.tsx` and
  `EntryPage.tsx` (ARIA grid on divs).
- Tests: 153 Vitest tests (incl. 6 page-level keyboard flows with a stubbed `fetch`). 4 new Playwright tests
  (`EntryBrowserTests`: keyboard-only batch with in-order creation and focus, incomplete row, Tab-accept of a
  suggestion, no layout shift/overflow at 1440 and 1024 px). Real Chromium confirmed the Tab-accept and Ctrl+Enter
  flows. Alt/Ctrl+arrow row shortcuts are covered only by Vitest: **try them in your own browser/OS**, since some
  OS/browser combinations intercept them.
- **Owner feedback after review:** Ctrl+D duplicates the row (replaces Alt+Shift+↓; `preventDefault` stops the browser
  bookmark dialog, verified in Chromium); Tab on the last field of the last non-empty row adds a row; an empty row that
  is not the final one is invalid ("Enter a description."); the "Reload accounts" 415 was fixed (JSON content type).
  Browser tests: run with `ASPNETCORE_URLS=http://127.0.0.1:0` when a dev instance already holds port 9142.
- `FakeFireflyClient` now serves asset accounts and suggestions and records created transactions in memory. Checks:
  `-warnaserror` build, CSharpier, Biome and `tsc` clean; fast .NET suite 317 passed.
- **Write safety:** only the in-memory fake and a stubbed `fetch` were used; no request went to any Firefly.
- To try it: `npm run build` in `src/Burndown.Client`, run the host with a token for a throwaway Firefly, open
  `http://127.0.0.1:9142/#/entry`.
- Open questions: should Entry be the default landing page? Is Description the right first focus? Are the shortcuts OK?

## Next: M6 – Browser tests and hardening

Extend the Playwright suite (mixed types, partial failure and uncertain rows with a scripted fake, throwaway Firefly),
then error UX (token expired, Firefly down), an accessibility pass, and a logging/round-trip review. Adjust M5 on the
owner's feedback first.

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

- M3: Recharts pulls in `react-is@17`; `package.json` has an `overrides` entry for `react-is` ^19. jsdom lacks
  `ResizeObserver` (stubbed in `test-setup.ts`). Testing Library normalizes nbsp to a space, so page tests match
  `€ 3.100,00` with a regular space (the format unit tests assert the real nbsp). `chunkSizeWarningLimit` is 800 kB
  (Recharts makes the bundle about 640 kB). `WebApplicationFactory.UseKestrel(0)` + `StartServer()` gives a real
  listener; read the address from `IServerAddressesFeature`. In Development the host serves the real built `wwwroot`.

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
