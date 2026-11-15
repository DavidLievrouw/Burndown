# Burndown rewrite – plan

Product spec, decisions and milestones. Conventions live in `AGENTS.md`; status lives in `HANDOVER.md`.

## Goal

Replace the existing Blazor Server app (`src/Burndown`) with a new application, built from scratch as **new projects in
the same solution**, on the `rework` branch. The old app stays untouched and runnable until parity; its removal is the
last milestone, on the owner's request.

The new app is an efficiency layer on top of Firefly III for one user (David):

1. **Entry** – keyboard-only bulk entry of expenses, income and transfers (primary focus).
2. **Overview** – monthly burndown budget table plus chart, with a polished look.

Out of scope: editing or deleting transactions, reports, budgets management, bank import, PWA/offline, mobile,
multi-user, login. Those stay in Firefly III.

## Decisions

| # | Decision | Rationale / notes |
|---|----------|-------------------|
| D1 | .NET 10 ASP.NET Core minimal-API BFF + React/TypeScript (Vite) SPA served from `wwwroot` | Owner confirmed. A custom grid gives full control over keyboard behavior, which a component library cannot. |
| D2 | No app database; Firefly III is the source of truth | Nothing to migrate or back up. |
| D3 | Firefly personal access token, held server-side only | Owner decision. No OAuth, no login. |
| D4 | Local-only: HTTP, loopback, no CSP/HTTPS/HSTS; host and Origin checks remain | Owner decision. Protects against DNS rebinding and cross-site posts to localhost. |
| D5 | Ids, categories, targets and tag names are configuration (`Burndown` options section in `appsettings.json`) | Owner confirmed. Token via user-secrets/`.env`. |
| D6 | English UI (including day and month names), `nl-BE` number/currency/date formats (`€ 1.234,56`, `dd/MM/yyyy`), Monday-first weeks, EUR only, zone `Europe/Brussels` from configuration | Owner decision. |
| D7 | Entry timestamps: see "Entry specification – timestamps" | Required to keep Firefly's entry order. |
| D8 | Minimum month 2023-12, maximum next month | Preserve current rules. |
| D9 | New projects added alongside the old one; new app on port 9142 until the old one is removed | Owner request. |
| D10 | Tooling: CSharpier, central package management, Roslynator, `TreatWarningsAsErrors`, Vogen (strongly typed ids), xUnit v3 (Microsoft.Testing.Platform runner selected in `global.json`), Shouldly, NetArchTest, Testcontainers, Playwright, Vitest, Biome | Mirrors MovieDB/Motorsports (check their `Directory.Packages.props` for versions). |
| D11 | Tests use a throwaway Firefly III container; the real Firefly (`localhost:8042`) is never written to | Real data. |
| D12 | Selectable accounts are **Asset accounts only** (`autocomplete/accounts?types=Asset account`), for every account field | Owner decision (replaces "keep the old Asset + Revenue"): Firefly only accepts asset-type accounts in a withdrawal source, a deposit destination and transfer accounts (verify in M1). Firefly's 422 errors still map to the account field. Revenue accounts and liabilities are not offered. |
| D13 | Options are owned per slice under the `Burndown` section (`Burndown:Overview`, `Burndown:Entry`); only the time zone is shared | Keeps feature slices independent. |
| D14 | The overview's budget override is transient (client/session state, never stored) | There is no app database. |
| D15 | Ranking is by **last use, kept in the BFF's process memory** (an in-memory singleton in the Entry slice, updated on every successful submit): used accounts, categories, target tags and budgets come first, most recently used first; the rest follow alphabetically (accounts) or in configured order (the others). It survives page reloads and is lost when the process restarts; nothing is persisted or read back from Firefly | Owner decision. Simple, no storage. Firefly's description autocomplete order is used as-is. The reference-data response is ranked by the server; the client keeps the order of the response. |
| D16 | Prefilling other fields from a description's last use is **optional**, decided after M4 shows whether it is cheap | Firefly autocomplete returns names only. |
| D17 | **Desktop-first**, not mobile-first (deliberate divergence from the Motorsports/MovieDB template); verify at 1440 px and 1024 px | Owner decision. |
| D18 | **Dark theme only**; no light mode and no theme switch | Owner decision; the old default Blazor theme is the anti-example. Colour scaling (green/red alpha) must be designed for a dark background. |
| D19 | Frontend stack: Vite, React, TypeScript strict, Tailwind with Radix primitives, TanStack Query, Biome, Vitest | Owner delegated the detail choices to the agent. |
| D20 | Entry layout: one mixed grid with a type per row (proposal in "Pending decisions"); the owner will try it and give feedback | Owner agreed to try the agent's design. The agent still presents the short keyboard/focus design at the start of M5 and builds it after the owner's go-ahead; layout is then iterated on feedback. |
| D21 | **No paste support.** The owner enters rows by hand from a phone app; the grid needs no clipboard import | Owner decision. Removes paste from the grid, the keyboard map and the browser tests. |

Pending decisions (resolve in the named milestone, then record here):

- ~~**Chart library** (M3).~~ **Decided in M3: Recharts** (3.10; needs the npm override `react-is` ^19 for React 19).
  The chart data is also available as a table behind a `<details>` toggle, and a screen-reader summary is rendered.
- **Entry grid layout** (M5). Proposal: one grid; each row has a type (`E`xpense/`I`ncome/`T`ransfer, one key) and
  type-dependent columns (see "Entry specification"). Sticky values per column. Fields not applicable to a type are
  skipped by Tab. Present a short design (keys and focus map) to the owner before building. The design must also cover:
  how Enter behaves in a combobox (accept, then advance?), date/time entry (fixed `dd/MM/yyyy HH:mm` mask versus
  short forms such as typing only the day number), the shortcut map (submit, duplicate, delete, move; avoid browser-
  reserved combinations). There is no paste support (D21).
- **Version info** (M7): `version.txt` or `version.props`.

## Write safety (binding from M4, the first milestone that writes to Firefly)

The Firefly at `localhost:8042` holds the owner's real data (D11). M4 adds the first code that creates transactions, so
these rules apply to every test, probe and manual run by the agent:

1. **Test layers and what they may touch.**
   - Unit, endpoint and architecture tests use a fake `IFireflyClient`/stub `HttpMessageHandler`. They make no network
     call at all.
   - Characteristics tests write **only** to the Testcontainers throwaway Firefly (random port, synthetic data, destroyed
     afterwards). No other Firefly is ever configured in a test.
   - Browser tests (M6) use the fake client or the throwaway Firefly, never the real one.
2. **Hard guard in the fixture.** `FireflyFixture` is the only place a test obtains a base URL and token; it uses the
   container's mapped endpoint. A test-assembly guard fails the run if a configured base URL has port `8042` or points
   at a non-container host, and `FireflyFixture` refuses an `FIREFLY_*`/`Firefly__*` environment override (environment
   variables beat user-secrets, so test hosts always set `Firefly:BaseUrl`/`Firefly:Token` explicitly).
3. **Test hosts never inherit the developer's configuration.** `WebApplicationFactory`-based tests replace
   `IFireflyClient` (or set explicit container settings) and clear the Firefly user-secrets/environment values, so a
   forgotten user-secret token can never be used.
4. **No manual write runs.** The agent never starts the host with the owner's real token, and never sends POST, PUT,
   PATCH or DELETE to `localhost:8042` (not even to "check" something). A manual or exploratory run of the Entry
   endpoints uses the throwaway container (a compose profile on another port, or the characteristics fixture) only.
5. **Read-only exploration of the real Firefly** only on the owner's explicit request, and never during M4 tests.
6. **Verification.** At the end of M4 the agent states in the handover which layers wrote where (expected: fake client
   only, plus the throwaway container) and checks `git status`/test output for any use of port 8042.

## Open questions for the owner

None open. (Q1–Q3 were answered and recorded as D12, D15 and D21.) Add new questions here, ask the owner, then record
the answer in the decisions table and delete the question.

## Configuration shape (final unless a milestone finds a reason to change it)

```json
{
  "Burndown": {
    "TimeZone": "Europe/Brussels",
    "Overview": {
      "MinimumMonth": "2023-12",
      "MonthlyInstallmentTag": "MonthlyInstallment",
      "ExcludedDestinationAccountType": "Expense account"
    },
    "Entry": {
      "CashAccountId": 16,
      "MaxBackdatedMonths": 2,
      "Categories": ["Leven", "Nodig", "Optioneel", "Folietjes"],
      "TargetTags": ["SharedDavid", "SharedTina", "Tina", "David", "Settled"],
      "Budgets": [
        { "Id": 13, "Name": "David" },
        { "Id": 14, "Name": "Tina" }
      ]
    }
  },
  "Firefly": {
    "BaseUrl": "http://host.docker.internal:8042",
    "Token": "<secret: user-secrets or environment only>"
  }
}
```

The old code hard-codes account `16`, budgets `13` (David) and `14` (Tina), categories and target tags (as above).
Account `16` was verified (read-only, owner-approved) to be Firefly's built-in **Cash account**: it is the counterpart of
every expense (destination) and income (source), which is why a single `CashAccountId` setting suffices and why the
overview excludes withdrawals to real "Expense account" destinations.

**Validation split.** At startup the app validates only what it can without the network (shape, `BaseUrl` is an absolute
URL, token present, zone resolvable, ids positive, lists non-empty and without duplicates) and fails fast with a clear
message. Checks that need Firefly (the cash account exists and has type "Cash account", the budgets exist) belong to
`/health/ready` and are surfaced in the UI; Firefly being down must never stop the app from starting.

## Overview specification (binding; port from `DataProcessor.cs`, `Home.razor`, `IncomeService.cs`, `ExpenseService.cs`)

Selected month `M`; all dates are calendar dates in the configured zone.

1. **Inputs.**
   - `Income(M-1)` = sum of the amounts of deposits dated in month `M-1`. The owner has no split deposits, so summing
     all splits and summing the first split are equivalent; sum all splits anyway.
   - `Installments(M-1)` = sum of the amounts of all splits of withdrawals in `M-1` that carry the
     `MonthlyInstallment` tag, whatever their destination type.
   - `Expenses(M)` = withdrawal splits dated in `M`, excluding those with the `MonthlyInstallment` tag and those whose
     destination account type is "Expense account".
   - A split's calendar date is the date part of the timestamp as Firefly returns it (its own offset); do not convert to
     UTC or to another zone.
2. **Month budget** = `Income(M-1) − Installments(M-1)`; the computed value may be zero or negative. An optional
   **override** (decimal ≥ 0; `0` is a valid override, absent/null means "computed") replaces it entirely.
   The override is per view, transient, and reset when the month changes (old behavior).
   The UI also shows `Income(M-1)` and `Installments(M-1)`.
3. **Per day** (`d` = 1..days in month): `dailyBudget = monthBudget / daysInMonth`; `cumulativeBudget(d) = dailyBudget × d`;
   `amount(d)` = sum of that day's expenses; `runningTotal(d)` = sum of `amount(1..d)`;
   `perDay(d) = cumulativeBudget(d) − runningTotal(d)`. No rounding until presentation (round half away from zero to
   2 decimals).
4. **Description of a day** = that day's non-empty descriptions, distinct ignoring case (first occurrence wins), sorted
   case-insensitively, joined with `", "`.
5. **Colours** (computed on the server as intensity data, rendered by the client; port exactly, with golden tests).
   Let `today` be the current date in the configured zone (the old code used UTC; that is a deliberate fix).
   - `perDay` colour: none for `date > today`. Otherwise, over the days with `date ≤ today`:
     `range = perDay(d) < 0 ? |min(perDay)| : max(perDay)` (falling back to 1 when there are no such days);
     `perDay ≥ 0` → green with alpha `min(1, perDay/range)`; `perDay < 0` → red with alpha `min(1, |perDay|/range) / 2`.
     Guard against a zero range (the old code would divide by zero; define the result as alpha 0).
   - `amount` colour: red with alpha `min(1, |amount|/range) / 2`, where `range = max(1, |max(amount over all days)|)`
     (no future-date guard here, as in the old code: a future day simply has amount 0 and alpha 0).
   - The old code rounded every alpha to 2 decimals (`0.00`, invariant culture). The server returns alphas rounded the
     same way (half away from zero), so golden tests can compare against the old output.
   - Weekends (Saturday, Sunday) are tinted; today is highlighted.
6. **Chart** (new). X axis: day 0..N. Two lines in EUR: *break-even pace* (dotted; was "ideal remaining") = `monthBudget − cumulativeBudget(d)` (from
   `monthBudget` at day 0 down to 0 at day N), and *actual remaining* = `monthBudget − runningTotal(d)`, drawn for
   `date ≤ today` only (none for a future month, all days for a past month). Values may go below zero (overspend). A
   "today" marker, an accessible table alternative, and the same data as the table (no separate calculation).
7. **Totals:** total spent, remaining (`monthBudget − total spent`).
8. **Navigation/range:** months from `MinimumMonth` (2023-12) to the month after the current one (in the configured zone);
   anything else is a 400. Number formats per D6.

## Entry specification (binding)

**Row types and fields** (all rows have a date and time, a description, an amount):

| Type | Account(s) | Other fields | Firefly payload |
|------|-----------|--------------|-----------------|
| Expense | source account (D12) | category (one of `Categories`), target tag (exactly one of `TargetTags`), budget (optional, one of `Budgets`) | `withdrawal`; source = chosen account; destination = `CashAccountId`; `category_name`; `tags` = [target]; `budget_id` = id or null |
| Income | destination account (D12) | none | `deposit`; source = `CashAccountId`; destination = chosen account |
| Transfer | from and to accounts (D12; must differ) | none | `transfer`; source = from; destination = to |

All posts go to `/api/v1/transactions` with `apply_rules: true` and `fire_webhooks: true`, one transaction (one split)
per request, built with typed models and `System.Text.Json`. Dates are sent in ISO 8601 with the offset of the configured
zone, minute precision.

**Validation** (identical on client and server; the server is authoritative):
description required, trimmed, at most the length Firefly accepts (verify against the throwaway Firefly in M1);
amount a decimal > 0 with at most 2 decimals; a **category** (expense only, required, as in the old app), a target tag
(expense only, required) and an optional budget, all of which must be configured values. Date bounds (calendar dates in
the configured zone, as in the old date pickers): the date is not earlier than `today − MaxBackdatedMonths months`
(`AddMonths` semantics) and not later than tomorrow (any time on tomorrow is allowed). Both bounds and "now" are
supplied by the server in the reference data; the client never uses its own clock for them. A batch is bounded in size
(pick a limit in M4, for example 100 rows; reject larger batches with a 400).

**Timestamps (D7).**
- Each row has an explicit date and time (`dd/MM/yyyy HH:mm`, configured zone).
- The first row of a session defaults to the server's "now" (minute precision, configured zone). A new row defaults to
  the row above plus one minute (plain addition: at 23:59 that is 00:00 on the next date, and validation then applies as
  usual). After a successful submit, the next new row continues from the last submitted timestamp plus one minute (in
  memory only).
- Typing only a date changes the date; the time of day carries over from the row above (plus one minute when the date is
  unchanged).
- Within one batch, rows that share a calendar date must have strictly increasing timestamps in grid order, compared
  with the nearest preceding row **on the same date** (rows on other dates in between are ignored). The server enforces
  this by bumping a row to that row's timestamp plus one minute if needed (so equal or earlier times are bumped); if the
  bump would roll into the next day it rejects the row with a field error. Rows on different dates may appear in any
  order. The server compares only within the batch it receives (it keeps no state), so the client is responsible for
  continuing the sequence across batches (see above). A bumped row's per-row result carries the effective timestamp,
  and the client shows it.
- Daylight saving: a local time that does not exist (spring forward) is rejected with a field error; an ambiguous one
  (fall back) resolves to the first (summer-time) occurrence. Both are covered by tests.

**Submit semantics.** Rows are posted sequentially in grid order and the response reports per-row results (created id
or field errors). A 4xx validation failure on one row does not stop the others. A connectivity error, timeout, 5xx or
401 stops the batch: the remaining rows are "not attempted". A timeout on a POST leaves that row "uncertain, check in
Firefly" and it is never retried automatically. Succeeded rows are never sent again (client guard against
double-submit, in addition to disabling submit while a request is in flight).

## Architecture

See `AGENTS.md` (Architecture). Projects: `Burndown.Common`, `Burndown.Firefly`, `Burndown.Overview`,
`Burndown.Entry`, `Burndown.Web` (.NET projects), `Burndown.Client` (the React app, not a .NET project, under `src/`),
and `src/tests/*`. Features never reference each other;
architecture tests enforce it.

BFF API sketch (final shape decided per milestone):

- `GET /api/overview?month=YYYY-MM[&budgetOverride=…]` → month budget (computed and effective), daily rows, series for
  the chart. The override is never stored.
- `GET /api/entry/reference` → accounts, categories, target tags, budgets, plus the server's `now`, the configured zone
  and the allowed date range (the Firefly-derived lists are cached with TTL; `now` and the range are computed per
  request, never cached; `POST …/refresh` reloads the lists).
- `GET /api/entry/suggest/descriptions?q=` → description suggestions (autocomplete).
- `POST /api/entry/submit` → ordered batch; response has per-row result (created id or field errors).
- `GET /health/live`, `GET /health/ready`.

## Milestones

Each milestone ends with: build, format check, tests, handover update, then **stop for owner review**.

### M0 – Scaffolding and tooling

**Status: delivered, awaiting owner review.** See `HANDOVER.md` for the details and check results.

- Add `global.json` (SDK 10.0.200, `rollForward: latestFeature`, `test.runner: Microsoft.Testing.Platform`, copy from
  Motorsports), `dotnet-tools.json` (CSharpier), `NuGet.config`, `.gitattributes` (LF), `.csharpierignore`
  (including `src/Burndown/`), `.dockerignore`, `.gitignore` additions (`.env`, `artifacts/`, `node_modules`, client
  build output). Check that `global.json` does not break the old project's build.
- Add `src/Directory.Build.props` and `src/Directory.Packages.props` that apply only to `Burndown.*` projects, so the
  old project is unaffected. Verify the old app still builds unchanged.
- Create projects and add them to `src/Burndown.sln`: Common, Firefly, Overview, Entry, Web, tests (unit, architecture).
- Web: options validation (network-free part only), `/health/live`, `/health/ready` stub (implemented in M1),
  `--health-check` (calls `/health/live`), `AllowedHosts`, Origin check middleware, SPA fallback, port 9142 (Kestrel on
  `127.0.0.1` when run directly; the container sets `http://+:9142`, see `AGENTS.md`).
  Allowed origins: `http://localhost:9142` and `http://127.0.0.1:9142` (plus the Vite dev origin in Development only); a
  missing Origin is accepted for non-browser clients.
- Client: Vite + React + TypeScript strict + Tailwind + Biome + Vitest; dev proxy to the BFF; a placeholder page.
- Architecture tests for dependency directions and visibility.
- Ask the owner any remaining configuration questions.
- Verify and fix the command list in `AGENTS.md`.

### M1 – Firefly client and throwaway-Firefly test infrastructure

- Typed client: accounts, autocomplete (accounts, descriptions), categories, tags, budgets, transactions list (date
  range, type, fully paginated), transaction create. Typed models; error mapping (401, 403, 422 per field, 5xx,
  timeout).
- Test fixture: throwaway Firefly III (+ database) via Testcontainers, pinned image tags, bootstrapped with a personal
  access token and synthetic accounts/transactions. Document how to run it and how long it takes; mark these tests with
  a trait so the fast subset excludes them.
- Characteristics tests: pagination, 422 mapping, 401 handling, create then read back.

**Delivered (M1) and what the throwaway Firefly 6.6.6 taught us:**

- Accounts: no account-autocomplete endpoint is called; the client lists Asset accounts (paged) and the UI filters
  client-side (this is what AGENTS.md requires anyway). Description autocomplete is `autocomplete/transactions`.
- `Firefly:TimeoutSeconds` (default 30, validated 1..300) is a new option.
- D12 check: Firefly accepts a deposit into an expense account and a deposit from the cash account into an asset
  account; it rejects a withdrawal from a revenue account. The asset-only rule is therefore the app's own.
- Description maximum is **1000** characters (1001 gives a 422 on `description`).
- Amounts with three decimals are accepted by Firefly, so the app must enforce two decimals itself.
- A transfer with equal source and destination, and an unknown budget, give field-keyed 422s (`budget_id`).
- GET of a non-existent resource by id answers **401**, not 404. The client only uses list calls.
- The cash account is created on demand by a withdrawal without a destination; its id is not predictable, so tests
  look it up with `accounts?type=cash`.
- Date ranges (`start`/`end`) are day-inclusive in Firefly's own time zone (`TZ` env, `Europe/Brussels` in the
  fixture). Mind DST when building test timestamps (`+02:00` in summer).

### M2 – Overview back-end

- Port `DataProcessor` rules (see "Overview specification" above) into `Burndown.Overview` as pure, tested logic with the
  clock abstraction. Colour scaling is computed on the server as data (values and ranges), not CSS.
- Endpoint with month range validation (min 2023-12, max next month), `budgetOverride`, bounded Firefly
  round trips (counted in a test).
- Tests: month lengths 28–31, leap February, zero income, negative budget, descriptions distinct/ordered, exclusions
  (`MonthlyInstallment` tag, expense-account destinations), "today" around midnight in zone vs UTC.
- Parity check against the old app using the same (synthetic) data.

**Delivered (M2) and decisions taken:**

- `GET /api/overview?month=yyyy-MM&budgetOverride=<decimal>` (both optional). Month defaults to the current month in the
  configured zone; allowed range 2023-12 to current month + 1. The override is invariant-culture (`.` decimal), `0` to
  999,999,999.99, and is never stored. Errors: 400 `code: "validation"` with `errors` keyed `month`/`budgetOverride`
  (both reported at once); Firefly failures map to 502 (401/403/unexpected/invalid response), 503 (unavailable), 504
  (timeout) with the user-readable message as `detail` (`FireflyErrorCodes.ToHttpStatusCode`).
- Exactly **two** parallel Firefly list calls per view: deposits over the previous month, and withdrawals from the
  first day of the previous month to the last day of the viewed month (installments of the previous month and the
  month's expenses). Counted in tests.
- One calculator (`OverviewCalculator`, pure) owns budget, daily series, colour data and chart. Response = month meta,
  `budget` (income, installments, computed, effective, overridden), `days[]` (amounts, cumulative budget, running total,
  per-day remaining, tone green/red/none and alpha values as data), `totals`, `chart` (points day 0..N with `breakEven` and
  `actual`, `actual` null after today; `todayDay` only when today is in the viewed month). Money is rounded to 2
  decimals once at the boundary; colours use the unrounded values. Day-0 `actual` is the budget when the month has
  started (own definition; the spec did not fix it).
- Deliberate differences from the old code: "today" from the configured zone (old: UTC); description order uses
  `InvariantCultureIgnoreCase` (old: `ToLower()`). Parity verified by running the old `DataProcessor` on synthetic
  data (Feb 2024, two budgets) in a scratch project and embedding its output as golden strings in the tests.

### M3 – Overview UI and chart

- Design system basics (tokens, typography), **dark theme only** (no light mode, no switch; use dark-tuned colours,
  contrast checked, `color-scheme: dark`), app shell, month navigation (keyboard: arrows,
  Home for current month), override input.
- Burndown chart (budget line vs actual remaining, today marker, weekend shading optional) plus an accessible data
  table. Choose the chart library here.
- Polished table: today highlight, weekend tint, per-day remaining colouring, sticky header, totals.
- Loading/empty/error states (including token invalid or Firefly unreachable).
- Component tests and a Playwright smoke test at 1440 px and 1024 px.
- Delivered notes: all text goes through `src/lib/format.ts` (nl-BE numbers, English day/month names, no ambient
  locale). Layout: table left and sticky chart right from `xl` (1280 px), stacked (chart first) below. The override
  input debounces 400 ms, is sent as an invariant decimal point, is reset when the month changes, and Escape clears
  it. Keys: ArrowLeft/ArrowRight/Home (ignored while typing in an input). A query error replaces the content with an
  error panel (token invalid/expired, Firefly unreachable, timeout, generic) with a retry button. The smoke test
  (`tests/Burndown.BrowserTests`) hosts the real app on Kestrel (`WebApplicationFactory.UseKestrel`) with a fake
  `IFireflyClient` and a fixed clock (15 Feb 2024), and checks render, no horizontal overflow and keyboard month
  navigation at 1440 and 1024 px.

- **Follow-ups after review (owner answers recorded):** the dotted line is the **break-even pace**: above it you are
  saving, below it you are overspending (contract field `ideal` renamed to `breakEven`). The chart data table is taller
  on desktop. New **year block** above the month: `GET /api/overview/year?month=yyyy-MM` returns the result of every
  month from January up to, but not including, the viewed month (month result = budget − spent, the `remaining` of the
  month's last day), the total, and the year (the previous year when January is viewed; the first month is clamped to
  2023-12, so 2023 shows "No previous data"). Same month validation as the overview. Exactly two parallel Firefly list
  calls (deposits and withdrawals over the whole span, the calculator filters per month). The client queries it
  separately, so a failure shows inline in the block and leaves the month overview intact. Open: a reported "server not
  reachable" when navigating to a future month (see `HANDOVER.md`).

### M4 – Entry back-end

- Reference data endpoint (cached with TTL and refresh, ranked by the in-memory last-use tracker of D15, updated after
  every successful submit; tested), description suggestions endpoint, batch submit.
- Validation per type (expense, income, transfer) using the same rules the client shows; strongly typed ids.
- Timestamp rules per "Entry specification – Timestamps" (server-side enforcement of increasing timestamps per calendar
  date, offset from the configured zone), tested.
- Sequential submit with per-row results; never resend a succeeded row; 422 to field errors.
- Characteristics tests against the throwaway Firefly: created transactions have the right type, accounts, category,
  tag, budget, ordering, `apply_rules` and `fire_webhooks`.

**M4 as delivered (contract the M5 client builds on; all under `/api`, JSON camelCase):**
- `GET /entry/reference` and `POST /entry/reference/refresh`: `now`, `today`, `timeZone`, `minDate`, `maxDate`,
  `maxBatchSize` (100), `accounts` (`{id,name}`, active Asset accounts, last-used first), `categories`, `targetTags`,
  `budgets` (`{id,name}`, configured; last-used first). Only the account list is cached (5 minutes, single-flight, failures never
  cached); `refresh` reloads it.
- `GET /entry/suggest/descriptions?q=`: returns `{ descriptions: [...] }`; blank query returns an empty list without a Firefly call; over 200 characters is
  a 400 on field `q`.
- `POST /entry/submit` with `{ rows: [...] }` (1–100 rows, otherwise 400 on field `rows`). Always 200 with per-row
  `results` plus `summary` and `stopped`. Row status: `created` (with `id`), `invalid` (own validation, never sent),
  `rejected` (Firefly 422 mapped to the row's field names; the batch continues), `failed` (401/403/5xx/unexpected; the
  batch stops), `uncertain` (timeout or unreadable answer; the batch stops and the row may or may not exist in Firefly,
  so the UI must tell the owner to check Firefly before retrying), `notAttempted` (after a stop). Rows are validated and
  time-sequenced first; invalid rows do not take part in the sequence. `bumped` and `dateTime` report the effective
  time. Nothing is ever resent automatically. `apply_rules`/`fire_webhooks` are set by the Firefly client; they cannot be
  read back, so no test asserts them.
- A bump that rolls into the next day or lands in a DST gap makes that row `invalid` on field `dateTime`.

### M5 – Entry grid UI (primary deliverable)

- Short keyboard/focus design agreed with the owner first.
- Grid with the behavior in `AGENTS.md` (Entry UX principles): Enter/Tab flow, accept-on-Enter/Tab comboboxes,
  filter-as-you-type without clearing, sticky values, duplicate/delete/move row, inline validation,
  Ctrl+Enter submit-all, per-row status, partial-failure handling, double-submit guard.
- Client-side filtering of accounts from the loaded list; debounced, cancellable description suggestions.
- Hooks for keyboard handling covered by Vitest; the full flows by Playwright (M6).

**M5 as delivered (built directly on the owner's request, without a separate design round; layout and shortcuts are open
to feedback, D20):**

- Hash-routed top navigation (`#/` Overview, `#/entry` Entry). Entry mounts on first visit and then stays mounted, so
  typed rows survive a peek at Overview. Overview unmounts when inactive (its month keys must not fire in Entry), so its
  selected month resets when the owner returns.
- ARIA grid, one `rowgroup` per entry: a field line plus a fixed-height status line (errors never shift the layout).
  Cells that do not apply to the row type show "—" and are skipped by Tab.
- Keys: Tab/Shift+Tab and Enter move between fields; Enter on the last field goes to the next row's Description or adds a
  row; Tab on the last field of the last (non-empty) row also adds a row; Ctrl+Enter submits; Alt+↑/↓ moves the row,
  Ctrl+D duplicates, Alt+Delete deletes; Ctrl+↑/↓ goes to the same
  field in the row above/below; Esc closes the list or undoes the typed text. Type: `e`/`i`/`t` then Tab.
- **First focus (owner-confirmed):** a new, duplicated or post-submit blank row focuses **When** with its **Day** part
  selected (a deviation from AGENTS' "first field", which would be Type). Enter/Tab then moves on to Description.
  Entry is not the default landing page. No compose profile for a throwaway Firefly is wanted.
- When is a segmented picker over one text input showing `dd/MM/yyyy HH:mm`: focus selects the day; digits replace the
  selected part and move on by themselves (`5` → day 05, `1` waits for a second digit; the year takes four digits);
  ←/→ pick the part, ↑/↓ step it (wrapping inside the part; a day that no longer fits the month is clamped); `/ . - :`
  and space move on. Pasting accepts the forgiving short forms (`5`, `5/3`, `14:30`, `5/3 14:30`). The value is always
  a valid date-time. Amount accepts `,` or
  `.` and normalizes to `12,50`.
- Description suggestions: debounced 250 ms, at least 2 characters, cancelled when stale, filtered client-side.
- Submit validates every row client-side first and sends nothing if any is invalid. Only the empty row at the very end
  of the list is ignored; any other row without a description is invalid. Created rows lock and are never
  resent; `uncertain` rows are excluded until the owner chooses "Treat as not sent". The reference data is not
  invalidated after a submit; "Reload accounts" refreshes it.
- **Submit is row by row (owner request, like the legacy app):** the client posts one row per `/entry/submit` request,
  in grid order, and waits for each answer before sending the next. `SubmitRequest.previous` (optional list of
  `yyyy-MM-ddTHH:mm` effective times of rows already created) seeds the server's time sequencer so strictly increasing
  times continue across requests (bad value: 400 on `previous`). The button shows "Submitting X of N…" with a
  `<progress>` bar, and the grid is `inert` while submitting. A stop (`failed`/`uncertain`, or a transport error) ends
  the loop; the remaining rows become `notAttempted` and the banner counts created / needs attention / unconfirmed /
  not sent. The 100-row `maxBatchSize` is still enforced by the server but no longer limits the client.
  Limit: Firefly's queued balance recalculation is not observable through the API, so one-by-one posting only
  spreads the load; it cannot guarantee the account balance is final (see HANDOVER Learnings).

### M6 – Browser tests and hardening

- Playwright (real HTTP listener, throwaway Firefly): keyboard-only batches of mixed types, partial failure,
  focus assertions, layout checks at 1440 px and 1024 px, overview smoke test.
- Error UX: token expired, Firefly down, timeouts. Accessibility pass (roles, focus, contrast).
- Review logging (no tokens, no descriptions) and round-trip counts.

**M6 as delivered:** the browser suite runs against a real Kestrel listener with the scriptable `FakeFireflyClient`
(`CreateFailure`, `ReadFailure`, `Script()`); the throwaway Firefly stays covered by the characteristics tests. New
tests: mixed expense/income/transfer batch, rejected row (field error, resubmit sends only that row), uncertain row
(batch stops, "Treat as not sent"), rejected token on submit, long error text without overflow at 1440/1024 px,
Overview error alerts (unauthorized, unavailable, timeout) with "Try again", Entry load panel and "Reload accounts"
failure/recovery, Overview round trips (at most 6 transaction requests per month), and an accessibility test (named
grid controls, listbox/`aria-expanded`, `aria-invalid` on a rejected field). Contrast pass: all text tokens meet WCAG AA
(muted 6.45:1, bad 6.29:1, ok 8.53:1, accent 7.72:1); placeholders were `muted/60` (3.0:1) and are now full `muted`.
Logging review: only paths (never query strings, bodies, tokens or descriptions) are logged; `System.Net.Http.HttpClient`
and `Microsoft.AspNetCore` logs are at Warning, so no URLs with descriptions leak.

### M7 – Packaging

- Multi-stage Dockerfile at `src/Burndown.Web/Dockerfile` (client build, publish, chiseled non-root runtime;
  `ASPNETCORE_URLS=http://+:9142` inside the container), exec-form healthcheck, compose file under
  `docker-compose/` with `127.0.0.1:9142` publish, memory limit and heap cap, `.env.example`.
- README with run instructions, token creation/rotation. Measure memory.

### M8 – Parity review and removal of the old app (only on the owner's request)

- Owner signs off parity. Remove `src/Burndown`, `publish.cmd`, `run.ps1`, root `dockerfile`/`docker-compose.yml`,
  committed `dist/`. Migrate `.sln` to `.slnx`, move the new app to port 9042, and tighten `Directory.*` to apply
  everywhere. The history still holds the old OAuth client secret: remind the owner to rotate it.

## Risks and open questions

- Throwaway Firefly bootstrap (creating a personal access token non-interactively) may need a database seed or a small
  PHP artisan step; investigate in M1 and record the working recipe. Firefly III supports SQLite
  (`DB_CONNECTION=sqlite`), which avoids a second database container: try that first. Docker is Rancher Desktop on
  Windows: confirm Testcontainers finds the engine (it may need `DOCKER_HOST` or the Ryuk setting) and record the fix.
  **Resolved in M1:** SQLite works; the token is created by a small PHP script (`bootstrap.php`); Testcontainers
  and Ryuk work with Rancher Desktop without extra settings. See `HANDOVER.md`.
- Firefly's description autocomplete may not rank by recency; its order is used as-is (D15). Account/category/tag/budget
  ranking is the in-memory last-use tracker (D15).
- Budgets `13`/`14` = David/Tina (owner-confirmed); account `16` = Firefly Cash account (verified).
- The Firefly personal access token expires; the UI must make that obvious.
