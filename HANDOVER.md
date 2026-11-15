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
| M6 Browser tests and hardening | Delivered, awaiting owner review |
| M7 Packaging | Delivered, approved |
| M8 Parity review, removal of old app, `run.ps1` | Delivered, awaiting owner review |

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
- Throwaway Firefly recipe (`Infrastructure/FireflyFixture.cs`, `bootstrap.php`): image `fireflyiii/core:version-6.7.8` (bumped from 6.6.6; earlier sections record what was verified on 6.6.6),
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
- **Second feedback round:** (1) When is now a segmented date-time picker (`whenSegments.ts` pure logic, `WhenInput` in
  `GridRow.tsx`; see PLAN "M5 as delivered"). (2) Category (and every select combobox): after Backspace-clearing or
  opening with the arrows, ArrowDown/Up then Enter accepts the highlighted option and moves on; Tab on an emptied,
  un-navigated box no longer silently picks the first item. Tests: 187 Vitest, 11 Playwright (new: date parts with real
  caret selection, Category Enter after clearing). Biome, `tsc`, build clean.
- **Reminder:** the dev debug session points at the **live** Firefly; Ctrl+Enter in Entry creates real transactions.
  Use a throwaway Firefly (`Firefly__BaseUrl`/`Firefly__Token`) for manual submits. Still unanswered: do you want that
  override documented as a compose profile?
- **Third feedback round (row-by-row submit):** after the stale-saldo investigation (Learnings) the client now posts one
  row per request with progress ("Submitting X of N…", `<progress>`, inert grid), like the legacy app. Server:
  `SubmitRequest.Previous` + `TimestampSequencer.Seed` keep the non-decreasing times across requests. Stops
  (`failed`/`uncertain`/network) leave the remaining rows as not sent. Details: PLAN "M5 as delivered". Tests: 189 Vitest,
  new unit tests for `Seed`/`Previous`, 11 Playwright and 25 characteristics tests green. **Limit:** Firefly's queued
  recalculation cannot be observed, so this does not guarantee a correct saldo; the durable fix is Firefly-side (sync
  queue, a single worker, or `firefly-iii:recalculate-running-balance`, the last one untested).
- **Owner answers:** the read-only check of the live Firefly was allowed; Entry is not the default page; the first
  focus is the **Day part of When** (implemented: new/duplicate/post-submit rows focus When; Vitest 189 and 11
  Playwright green); no compose profile for a throwaway Firefly.
- **Live Firefly finding (read-only, container env only):** `QUEUE_DRIVER=sync`, no queue worker or cron process, so
  the async-queue hypothesis does **not** apply to the live instance; the stale-saldo cause is still unknown
  (candidates: rules/webhooks touching the transaction after creation, or pre-existing dirty `balance_after` rows).
  Checking those needs DB reads: ask first.
- **Disclosure:** my `docker inspect` env dump printed Firefly's `STATIC_CRON_TOKEN` into tool output (not stored in a
  file); consider rotating it.
- **Owner answers (round 2):** read-only DB check of the live Firefly allowed; all keyboard shortcuts now work for the
  owner (no rebinding needed).
- **Live DB check (SELECT only, read-only session):** 10,510 transactions, 0 with `balance_dirty`, 0 NULL
  `balance_before/after`, per-row arithmetic is correct. 12 active rules, 0 webhooks. The `balance_before` →
  previous `balance_after` chain (ordered by date, order, id) has historical breaks in many accounts (KBC zichtrekening
  David 22, Tina 22, Cash account 75, ...), none newer than 2026-01-05 for the KBC accounts. In the broken rows
  `balance_before = 0` and `balance_after = amount`, as if the row were first in the account. So the stale saldo is
  stored data (not a display issue) and has nothing to do with Burndown's payload; a repair (opening and saving the
  transaction, or Firefly's recalculation command) is a write on live data and needs the owner's explicit go-ahead.
  Hypothesis, unproven: rules (`apply_rules: true`) updating the journal right after creation trigger recalculations
  that race.
- **Owner decision:** do not modify the live Firefly data (no recalculation command, no repair). Not to be proposed
  again; any further stale-saldo investigation stays on the throwaway Firefly.
- **Result in practice (owner, 2026-11-14):** after the row-by-row submit, a batch of transactions in KBC Mastercard
  Gold left the balance correct. So sequential posting avoids the stale saldo on the live instance, even though the
  mechanism (rules racing the recalculation, most likely) is unproven. Older broken rows stay as they are.
- **Follow-up live DB analysis (SELECT only):** the Mastercard batch (5 rows, all created within the same second)
  has an intact chain. Across all data since 2025-01 (about 6,800 rows, 143 chain breaks = 2%) the breaks do **not**
  correlate with backdated inserts (38% of broken vs 40% of intact rows), edits after creation, transaction type, or
  burst creation (0 of 34 rows created within 2 s of the previous journal were broken). They **do** cluster in time:
  Aug 2025 – Jan 2026 (11, 20, 9, 35, 31, 14 per month), then 0–2 per month from Feb 2026 on, none in Aug–Nov 2026. The
  2026-11-14 stale rows (5350–5361) were repaired by the owner's re-saves, so the broken state is no longer visible.
  Conclusion: the old breaks belong to a period (a Firefly version/config or the legacy app of that time), not to
  rapid posting; the remaining open risk is a rare, unreproduced stale saldo. Row-by-row submit stays as protection.

### M6 – Browser tests and hardening (delivered)

- `FakeFireflyClient` is scriptable (`CreateFailure`, `ReadFailure`, `TransactionRequests`, `Script()` scope that
  resets). New in `src/tests/Burndown.BrowserTests`: `BrowserPages.cs` (helpers incl. `BURNDOWN_SCREENSHOT_DIR`
  screenshots), `EntryScenarioBrowserTests.cs`, `ErrorScreensBrowserTests.cs`. 24 browser tests total (13 new): see PLAN
  "M6 as delivered" for the list.
- Accessibility: contrast computed for all tokens (all AA); placeholder colour raised in `Combobox.tsx` and
  `OverrideField.tsx`. Roles/names/`aria-invalid` asserted in a Playwright test. No other issue found.
- Logging review: clean (paths only; HttpClient/AspNetCore at Warning). Overview needs at most 6 transaction requests
  per month (tested).
- Checks: `-warnaserror` build, CSharpier, Biome, `tsc`, client build clean; Vitest all green; fast .NET suite 312
  passed; characteristics 25 passed (Docker, env vars unset); browser 24 passed.
- Gotchas: the fake's `TimeProvider` is frozen, so the account cache never expires (use "Reload accounts" or route
  aborts to fail the Entry load). After clicking a button, focus leaves the grid, so Ctrl+Enter does nothing: click
  "Submit N rows". The banner counts only the latest submit run.
- **Write safety:** no request went to the live Firefly; the characteristics tests used a throwaway container.

### M7 – Packaging (delivered)

- New: `src/Burndown.Web/Dockerfile`, `docker-compose/burndown/docker-compose.yml` + `.env.example`, root `README.md`,
  `version.txt` (`1.0.0`) + `src/version.props` (imported by `src/Directory.Build.props`, `Burndown.*` only; the old
  project is unaffected). `.dockerignore` extended. Design and flags: see PLAN "M7 as delivered".
- Verified with the built image and compose against a **throwaway Firefly 6.6.6** (synthetic data, port 18142; the live
  Firefly was never contacted): container `healthy`, runs as uid 1654, read-only root fs, no capabilities; `/health/live`
  200, `/health/ready` 200 (with matching ids; 503 with the default ids 16/13/14, as designed), SPA served, API reads 200,
  foreign `Host` 400, foreign `Origin` POST 403, logs contain no token. A missing `FIREFLY_TOKEN` makes compose refuse
  to start. All containers and the throwaway Firefly were removed again.
- **Memory** (`docker stats`, 256 MiB limit, heap hard limit 50 % = 128 MiB): 32 MiB idle after start, 65 MiB after
  about 450 requests (overview, reference, SPA). Plenty of headroom, so 256 MiB and 50 % stay.
- Gotchas: the aspnet chiseled image sets `ASPNETCORE_HTTP_PORTS=8080`, which makes Kestrel warn when
  `ASPNETCORE_URLS` is also set (cleared in the Dockerfile). The new compose file needs its own project name
  (`burndown-web`): the legacy compose in the root uses project and service `burndown`, so a shared name would let
  `compose up` recreate or orphan the legacy container. New files are LF. Compose variables must be set in the same
  PowerShell call as `docker compose` (env vars do not persist between tool calls).
- Not done (by design): no automated image build in tests; the browser tests still run the host directly.

### M8 – Parity review, removal of the old app, `run.ps1` (delivered)

Note: earlier milestone sections above keep their historical wording (`Burndown.sln`, port 9142, "old app"); since M8
the solution is `src/Burndown.slnx`, the port is 9042 and the old app is gone.

- Removed: `src/Burndown/` (38 tracked files), `publish.cmd`, root `dockerfile`, root `docker-compose.yml`, old
  `run.ps1`, untracked `dist/`. `.sln` → `.slnx` (`src/Burndown.slnx`; the Assets folder lists `run.ps1`, compose,
  docs, `version.txt`). `src/Directory.Build.props`, `Directory.Packages.props` and `version.props` are now
  unconditional. `.csharpierignore`, `.dockerignore`, `.gitignore` cleaned of legacy entries.
- Port 9142 → 9042: `Program.cs`, `HealthCheckCommand.cs`, `vite.config.ts`, `launchSettings.json`, Dockerfile, compose,
  `HealthCheckCommandTests` (9142 stays as the intentional "other port" case). Compose gained the `SOURCE_REVISION_ID`
  build arg.
- New root `run.ps1` (PowerShell 7): `-FireflyUrl`, `-FireflyToken` (SecureString), `-NoCache`; prompts for the URL
  (ENTER accepts `http://host.docker.internal:8042`) and the token, validates both, sets `FIREFLY_BASE_URL`,
  `FIREFLY_TOKEN`, `SOURCE_REVISION_ID` as process env vars only (removed in `finally`), runs `docker compose build` and
  `up --detach --force-recreate --remove-orphans`, waits for the container to be healthy and reports `/health/ready`.
  Nothing is written to disk. Use it with the real token yourself; the agent never ran it against the real Firefly.
- Legacy Docker: Portainer (`localhost:9000`) held only the `evcc` stack; the `burndown` stack and container no longer
  existed. Removed the leftover image `burndown:latest` and the networks `burndown_default` / `burndown-web_default`.
  Nothing else was touched. The Portainer password was used in memory only and is not recorded.
- Verification: `run.ps1` run in-process against a **throwaway Firefly 6.6.6** (port 18142): image built, container
  healthy on `127.0.0.1:9042`, `/health/live` and `/` 200, `/health/ready` 503 only because the default ids 16/13/14
  do not exist there (script warned as designed); env showed the trimmed base URL. Cleaned up afterwards. The
  ENTER-default prompt was verified by reading (secure `Read-Host` cannot be driven from piped stdin).
  Build `-warnaserror` 0 warnings; CSharpier check clean; fast tests 312/312; browser tests 24/24; client `lint` clean,
  `vitest` 189/189.
- Learnings: `-FireflyToken` is a SecureString, so call `run.ps1` in-process (`& .\run.ps1 ...`), not via `pwsh -File`
  with an argument. The `Zero tests ran` / exit 8 from a solution-wide run comes from the Browser project when excluded
  by filter; run Browser tests with `--project`.

## Next

Nothing pending: the plan is complete after owner review of M8. Reminders for the owner: rotate the old Firefly OAuth
client secret (still in git history), Firefly's `STATIC_CRON_TOKEN` (printed once into tool output in an earlier
session) and the Portainer password (shared in chat). The default ids (cash 16, budgets 13/14) are for the live
Firefly; `/health/ready` is 503 against any other instance.

## Environment facts

- Branch: `rework`. Git root `C:\David\Programmatuur\Burndown`. Solution: `src\Burndown.slnx` (the old project was removed in M8).
- Toolchain: .NET SDKs 8, 9 and 10.0.200, Docker 29.6 (Rancher Desktop), Node 24.19.
- Real Firefly III: `http://localhost:8042` (real data: read-only unless the owner asks, never written to by an agent).
- App: port 9042 (127.0.0.1). Deploy with `.\run.ps1`.
- Reference repos: `C:\_git\MovieDB`, `C:\_git\Motorsports` (never read their `prompt-history*` files).
- The owner's uncommitted `src/Burndown/appsettings.Development.json` (old OAuth secret) was deleted together with the
  old project in M8 (the secret remains in git history of the old compose file). Never copy it.

## Learnings

- Future-dated rows make the account balance look wrong (investigated read-only on the live DB, 15 Nov 2026, account
  21, 17-row bulk entry from 8 Nov). Firefly's displayed current balance sums only transactions with
  `transaction_journals.date <= now` (`Steam::finalAccountBalance`), while its stored `balance_before/after` chain
  includes every row in date order. The stored chain was correct (total 307.25), but the UI showed 614.25 because two
  rows (192 and 115) were dated today at 11:32/11:33 while the batch was submitted at about 11:23: the client's
  "row above plus one minute" chain ran past the real time of day. It corrected itself once those times passed. This
  was not the stale-saldo race below (`QUEUE_DRIVER=sync` live, no worker). Fix (owner decision): the server never
  sends a row dated today later than its "now" (server clock, configured zone, minute precision). When the same-day
  chain would run past now, `TimestampSequencer.Place` shifts it back (only as far as needed) so the last row lands at
  now (moved rows are reported as `bumped`; the client shows "Time moved to HH:mm (not in the future, in entry
  order)."). Never before the seed (previous submitted row): with no room, rows share that moment; nothing is rejected
  for lack of room. Tomorrow and later are not capped.
  Follow-up owner decision: several rows at exactly the same moment must stay allowed (Firefly allows it). The old rule
  bumped equal times by +1 minute, so the rule is now non-decreasing per date (equal allowed, only earlier rows move
  up). Characteristics tests confirm Firefly accepts identical timestamps and keeps creation order. Browser tests: the
  fake "now" (10:00) equals the client's default time, so rows 2 and 3 now share 10:00 instead of 10:01/10:02.
  `BrowserFixture` now sets `ASPNETCORE_URLS=http://127.0.0.1:0` while it runs (restored after), because `Program.cs`
  otherwise also binds 9042, which the deployed container holds. The browser tests also need a built client in
  `src/Burndown.Web/wwwroot` (`npm run build`) and Playwright Chromium. Results: 327 fast, 27 characteristics, 24
  browser tests green. See PLAN D7. Tests: `TimestampSequencerTests` (`Place_*`),
  `EntrySubmitServiceTests`, `EntrySubmitCharacteristics`, `gridReducer.test.ts`. Possible follow-up: cap the client's
  default time (row above plus one minute) at the server's now, so the grid never proposes a future time. Check this
  first when a balance looks off right after entering rows.

- Stale account balance after a batch (investigated, not reproduced with defaults). Firefly keeps per-row
  `balance_before/after/dirty` and recalculates them in the `ProcessesNewTransactionGroup` listener, which is a queued
  listener. Throwaway Firefly 6.6.6 experiments (7 backdated posts incl. near-midnight, legacy naive-date payload versus
  the new offset payload, TZ UTC and Europe/Brussels): with `QUEUE_CONNECTION=sync` both payloads leave correct
  balances, so payload shape is not the cause. With an async queue the balance columns stay `NULL/dirty=1` until a
  worker runs; one worker fixes them, several concurrent workers (SQLite) left some rows stale. A UI save fires the
  update event, which recalculates (hence the "open, save, close" fix). Likely cause on the live instance: its queue
  or worker setup (not checked: the live Firefly is off limits); a rapid batch makes that race more likely than the
  legacy app's one-at-a-time posts. Workaround: `php artisan firefly-iii:recalculate-running-balance` in the Firefly
  container. The experiment code was removed again (not a regression test).

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
  description prefill is optional (D16, AGENTS.md aligned); the same-date timestamp rule (non-decreasing) compares with the nearest
  preceding row on the same date and the result carries the effective timestamp; DST gaps are rejected and overlaps
  take the first occurrence; alphas are rounded to 2 decimals for parity with the old output; day/month names are
  English with `nl-BE` numbers (one shared formatter, no ambient locale).
