# Burndown – AI Agent Guide

This file holds the conventions for working on Burndown. `AGENTS.template.md` is the generic template this file was
derived from; do not edit it for project work.

## Project overview

- **Product:** a personal, single-user (David) efficiency layer on top of [Firefly III](https://www.firefly-iii.org/).
  It has two parts:
  1. **Entry:** extremely fast, keyboard-only bulk entry of expenses, income and transfers. The owner reads transactions
     from a banking app on a phone and types them in by hand in a desktop browser, so entering many rows quickly
     without touching the mouse is the primary goal.
  2. **Overview:** a monthly "burndown" budget overview (table plus chart) that shows how the month's budget is spent
     day by day.
- **Scope:** keep the current scope (expense, income, transfer, overview). Detailed operations (editing, reports,
  budgets, bank import) stay in the Firefly III interface. Do not add features outside this scope without asking.
- **Stack:** confirmed by the owner:
  - Back-end: .NET 10 / C# 14, ASP.NET Core minimal APIs as a backend-for-frontend (BFF). No application database;
    Firefly III is the single source of truth.
  - Front-end: React + TypeScript (Vite) single-page app with a custom keyboard-first entry grid, served from the BFF's
    `wwwroot`. TanStack Query for server state, Tailwind CSS with Radix primitives for UI, Biome for lint and format.
  - Back-end tests: xUnit v3 (Microsoft.Testing.Platform), Shouldly, NetArchTest, Testcontainers (throwaway Firefly III),
    Playwright for .NET. Front-end tests: Vitest and Testing Library.
  - Tooling: CSharpier for C#, Biome for TypeScript, central package management. The solution stays `src/Burndown.sln`
    until the old app is removed, then becomes `.slnx`.
- **Runtime/deployment:** one Docker container (Linux) on the owner's machine, reachable at `http://127.0.0.1:9142`
  (port 9042 after the old app is removed, see Coexistence).
  Firefly III runs on the same machine (`http://host.docker.internal:8042` from the container). There is no
  authentication in the app: it is local-only.
- **Current plan and handover:** `PLAN.md` and `HANDOVER.md` in the repository root (product spec, decisions and
  milestones; status). Read the handover first; update it at the end of every milestone.
- **Language rule:** the UI is English. Code, identifiers, comments, docs, logs and commits are English. Data that comes
  from Firefly (category names, tags, account names) stays as it is (Dutch). Format money and dates for `nl-BE`
  (`€ 1.234,56`, `dd/MM/yyyy`), the week starts on Monday, and there is a single currency (EUR). Day and month names are
  English (PLAN D6), so build date text from one shared formatter (numbers and numeric dates `nl-BE`, names from a fixed
  English list) and never from the browser's or server's ambient locale (`toLocaleDateString`, `dateStyle`, ...).
- **Reference implementations:** `C:\_git\MovieDB` and `C:\_git\Motorsports` (read-only siblings by the same owner).
  Mirror their repository layout, tooling, `Directory.*.props`, central packages, architecture tests, Dockerfile and
  compose conventions. Deliberate differences: no database, no authentication, no CSP/HTTPS/HSTS (local-only app), a
  React front-end instead of Blazor/MudBlazor, **desktop-first** instead of mobile-first, and a **dark theme only**
  (no light mode, no theme switch). Do not copy those differences blindly in the other direction either.
- **Precedence:** this file holds conventions, `PLAN.md` holds the product spec. If the two, or a document and the
  code/environment, disagree, stop and ask the owner before choosing, then record the answer in the plan.
- **The rewrite:** the working application in `src/Burndown` (Blazor Server + Radzen) is being replaced from scratch on
  the `rework` branch. The new application consists of **new, separate projects added to the same solution**
  (`src/Burndown.Web`, `src/Burndown.Firefly`, and so on). Do not modify the old `Burndown` project, its files, or the
  root `dockerfile`/`docker-compose.yml`/`publish.cmd`/`run.ps1`; it stays runnable as the behavioral reference until
  the new app reaches parity. The new projects must not reference it. Removing the old project is its own final
  milestone, done only on the owner's request. Do not copy its patterns (string-built JSON, hard-coded ids, unpaged
  queries, OAuth).
- **Coexistence:** while both apps exist, the new app uses port **9142** (the old one uses 9042). It takes over 9042
  when the old one is removed.

### Architecture

Projects (under `src/`, tests under `src/tests/`) and dependency directions (refine in `PLAN.md`):

- **Burndown.Web:** host. Startup, options validation, BFF endpoints, SPA hosting, health endpoint, `--health-check`
  switch, request-origin protection.
- **Burndown.Firefly:** typed Firefly III API v1 client, request/response DTOs (System.Text.Json), pagination, and error
  mapping. The only project that knows Firefly's wire format.
- **Burndown.Common:** small, dependency-light primitives: result/error types, the clock (`TimeProvider` plus the
  configured zone), money and date helpers.
- **Burndown.Overview:** feature slice: month budget calculation, burndown model and endpoints.
- **Burndown.Entry:** feature slice: reference data (accounts, categories, tags, budgets, description suggestions),
  validation and bulk submit.
- **src/Burndown.Client:** the React SPA (not a .NET project). Built into the host's `wwwroot` during publish.
- **Tests:** unit, characteristics (against a throwaway Firefly), architecture and browser tests, under `src/tests/`.

The solution stays `src/Burndown.sln` while the old project exists (new projects are added to it). It is migrated to
`.slnx` in the milestone that removes the old project.

Rules: features depend on Common and Firefly, never on each other. Common depends on nothing. Firefly depends only on
Common. The host composes everything through explicit module registration. Enforce boundaries with architecture tests.
Keep shared abstractions small. The strongly typed Firefly ids (account, budget, transaction) live in `Burndown.Firefly`
(a Firefly concept); features reference them from there. Feature slices own their own options and DTOs; the BFF's JSON
contract types belong to the slice that serves them, never to `Burndown.Firefly`.

## Agent behavior

- **Private files:** never read, search, quote, edit or stage any file named `prompt-history.txt` or
  `prompt_history*.txt`, wherever it is located (the owner's personal notes). Exclude them from searches, diffs and
  commits.
- **The real Firefly III is off limits for writes.** The Firefly at `localhost:8042` holds the owner's real financial
  data. Never POST, PUT, PATCH or DELETE against it, and do not run the app with a token pointing at it in automated
  runs. Read-only exploration is allowed only when the owner explicitly asks. All tests and manual agent runs use a
  throwaway Firefly III container with synthetic data (Testcontainers or a compose profile on another port).
- Work one milestone at a time, then stop and wait for the owner's review. Do not start the next milestone unprompted.
  Do not commit unless asked.
- After every milestone update `HANDOVER.md`: status table, what was delivered (files, tests, check results), the plan
  for the next milestone, environment facts and new learnings. Promote learnings that become conventions into this
  file or the plan. Keep secrets, tokens and real financial data out of it.
- Ask questions first when scope, product behavior or the Firefly mapping is not established by code or documentation,
  and record the answers in the plan. Do not re-ask what the plan already decided.
- Examine the existing code and working tree before changing files. Preserve unrelated user changes (the owner's
  uncommitted work).
- Follow the repository's patterns and architecture; make complete, focused changes without unrelated cleanup.
- Keep secrets, tokens, client secrets, personal data and real transactions out of source, logs, test output and
  external services. The old repository history and `docker-compose.yml` contain a Firefly OAuth client secret: never
  copy it, and remind the owner to rotate it.
- Validate all submitted data on the server. Report errors through the application's standard result, logging and UI
  mechanisms; do not hide failures behind broad catches or success-shaped fallbacks.
- Update directly related documentation when behavior, architecture or operational requirements change.

## Entry UX principles (the core of the product)

Efficiency of bulk entry decides every design choice. The acceptance test for any entry change is: can the owner enter
a batch of rows without touching the mouse and without clearing or retyping anything the app could have predicted?

- The whole flow works with the keyboard only: Tab/Shift+Tab and Enter move between fields, Enter on the last field adds
  a row and puts focus in the first field of the new row, and a documented shortcut (for example Ctrl+Enter) submits
  all valid rows.
- Entry is a list/grid: rows are added to a list, reviewed, then submitted in one go. Provide keyboard row actions
  (duplicate previous row, delete row, move up/down).
- Autosuggest comboboxes (accounts, descriptions, categories, tags, budgets): typing filters instantly; the first match
  is pre-highlighted; Enter or Tab accepts it. Never require clearing a field first, and never require the mouse to
  pick a value. Match on any word and ignore case and accents. Rank recently used values first: in the BFF's process memory, per `PLAN.md` D15 (survives page reloads, lost on
  restart, never persisted).
- Sticky values: date, source account, category, tag and budget carry over from the previous row by default.
  Pre-filling the other fields from a known description's last use is **optional** (D16, decided after M4); do not
  build it before then.
- Amount fields accept both `,` and `.` as decimal separator and reject nonsense inline.
- There is no paste or clipboard import (D21): rows are typed by hand.
- Validation is inline and per row/field, using the same rules as the server. Do not shift the layout on blur (a
  message that appears on blur can move the target between mouse-down and mouse-up).
- Submit status is per row. A partial failure never loses rows: failed rows stay, with the Firefly error mapped to the
  field; succeeded rows are marked and cannot be submitted twice (idempotent against double Ctrl+Enter).
- Rows get explicit date+time values; a new row defaults to the row above plus one minute, and rows on the same calendar
  date are strictly increasing in grid order (enforced by the server), so Firefly keeps the entry order. The next batch
  continues from the last submitted timestamp in the same session. This is required behavior (see `PLAN.md`). "Now" and
  the allowed date range come from the server (the reference-data response), never from the browser clock.
- Accessibility: use correct ARIA roles (grid, combobox, listbox), visible focus, and manage focus explicitly. Do not
  trap focus; never steal focus while the user types.
- Desktop and keyboard first. Verify layouts at about 1440 px and a narrower window (for example 1024 px). Mobile is not
  a goal; there is no PWA or offline mode.
- Cover focus, Enter/Tab flow and layout with Playwright browser tests, not only component tests.

## Business rules (Overview and Entry; the binding spec is in `PLAN.md`, sections "Overview specification" and "Entry specification")

Summary only; if this list and `PLAN.md` ever differ, stop and ask. Port from `DataProcessor.cs` and cover with tests.

- **Month budget** = previous month's income (deposits, all splits) minus the previous month's withdrawals tagged
  `MonthlyInstallment`. The user may override it for the selected month (transient, never stored, reset when the month
  changes).
- **Daily budget** = month budget / days in the month. Cumulative budget on day N = daily budget × N.
- **Expenses** are withdrawals, excluding tag `MonthlyInstallment` and excluding transactions whose destination account
  type is "Expense account". Per-day remaining = cumulative budget − running total of expenses.
- Per day, group expenses; descriptions are distinct (case-insensitive), ordered and comma-joined.
- Navigation: minimum month is **2023-12**; maximum is the month after the current one. "Today" and "the current month"
  come from the configured zone (see Time).
- Presentation: per-day remaining is coloured green/red scaled by range for dates up to today; amount cell red-scaled;
  weekends are tinted. Add a **burndown chart** (cumulative budget line versus actual remaining budget, today marked).
  The overview must look polished; it is functionally complete today but visually poor.
- Tags, categories, target tags, budgets and special account ids that the old code hard-coded (cash account
  `16`, budgets `13` David/`14` Tina, categories `Leven`/`Nodig`/`Optioneel`/`Folietjes`, targets `SharedDavid`/`SharedTina`/
  `Tina`/`David`/`Settled`, tag `MonthlyInstallment`) are configuration (`Burndown` options section, see the shape in
  `PLAN.md`; validated at startup), never literals in code. Non-secret defaults live in `appsettings.json`; the token
  lives elsewhere.
- Entry posts to `/api/v1/transactions` with `apply_rules: true` and `fire_webhooks: true`:
  - **Expense:** `withdrawal`, source = chosen account, destination = the configured cash account
    (`CashAccountId`, Firefly's built-in Cash account), category from the configured list, exactly one target tag,
    optional budget.
  - **Income:** `deposit`, source = the configured cash account, destination = chosen account. Selectable accounts are
    Asset accounts only, in every account field (`PLAN.md` D12).
  - **Transfer:** `transfer`, from and to accounts must differ.

## Architecture and code conventions

- Keep host composition in the host project and feature behavior in its owning feature. Keep Firefly wire details in
  `Burndown.Firefly`.
- Make a type `public` only when another production assembly must use it, or the host requires it (`Program` for
  `WebApplicationFactory`). Everything else is `internal`, including options, health checks and assembly markers. Tests
  see internals through `InternalsVisibleTo`; do not make a type public for a test. Re-check visibility when adding a
  type.
- Do not use primary constructors on classes or structs: declare `private readonly` fields (`_camelCase`) and an
  explicit constructor. Positional (primary) constructors are allowed, and preferred, on `record` and `record struct`
  types. This applies to test projects too.
- XML documentation must be complete: every `<param>` (including positional record parameters), `<typeparam>` and
  `<exception>`. Roslynator reports these (RCS1140/1141/1142) only when `GenerateDocumentationFile` is true, which the
  IDE always does; check with the XML-doc build command below.
- Formatting follows `.editorconfig` and `.csharpierrc.yaml`: LF line endings, UTF-8, 4 spaces (2 for yaml/json/csproj/
  sln/slnx), print width 120, braces always, `var`, trailing commas. `TreatWarningsAsErrors` is on and Roslynator rules
  are warnings; fix them rather than suppressing.
- Use the repository's dependency injection and module-registration patterns. Use central package management; do not
  put versions in project files.
- Use a result/error type for expected failures (validation, Firefly 4xx); exceptions are for the unexpected. Validate
  before sending anything to Firefly.
- Use strongly typed IDs (for example Vogen) for Firefly ids (accounts, budgets, transactions) instead of bare
  integers or strings. Do not mix an account id with a budget id.
- Money is `decimal`; never `double`/`float`. Parse and format only through shared helpers.
- Compute derived data (the month budget, the daily/cumulative series, timestamp sequencing) in exactly one place,
  covered by tests, and reuse it.
- Build JSON only with `System.Text.Json` and typed models; never by string interpolation or concatenation (the old app
  had a quote-injection bug this way).
- Time:
  - Use `TimeProvider` and a single shared clock that exposes the configured zone (`Burndown:TimeZone`, IANA id,
    default `Europe/Brussels`). Never call `DateTime.Now`/`UtcNow` or `DateTimeOffset.Now` in business logic.
  - Resolve "today", the current month and the allowed month range server-side in the configured zone. Never trust the
    browser clock for them.
  - Always specify the `DateTimeKind` when creating a `DateTime`. Use `DateOnly` for pure dates. Send Firefly timestamps
    with an explicit offset.
  - Test DST boundaries, the month boundary near midnight in the configured zone versus UTC, and a "today" that differs
    between UTC and Europe/Brussels at the same instant.
- Keep the BFF's route and JSON formats stable once the client uses them, unless a change is intentional and documented.

## Firefly III integration

- Firefly III API v1 only, called from the BFF with a **personal access token** (`Authorization: Bearer`). The browser
  never sees the token or talks to Firefly directly.
- The token is a secret: user-secrets in development, an environment variable or git-ignored `.env` in Docker. Never
  commit or log it, and never put it in test output. It expires (about one year); a 401 from Firefly must surface as a
  clear "token invalid or expired" error in the UI and the health endpoint.
- Use a typed `HttpClient` with a configured base URL (`Firefly:BaseUrl`), timeouts and `IHttpClientFactory`. No blanket
  retries; retry only idempotent reads when measured to be needed.
- Always paginate (follow `meta.pagination`); never rely on `limit=999`. Request only what a view needs
  (date range and type filters).
- Map Firefly 422 validation errors (`errors` keyed by field, such as `transactions.0.source_id`) to row and field
  errors in the BFF response. Map 401/403/5xx/timeouts to distinct, user-readable errors. Do not expose raw reason
  phrases or response bodies.
- Reference data (accounts, categories, tags, budgets) may be cached in memory only with a short TTL, an explicit
  refresh path and a regression test. Transactions and overview data are not cached unless measured to matter.
- Description suggestions use Firefly's autocomplete endpoints; debounce on the client and cancel stale requests.
- Account autosuggest must work offline from the loaded list (filter client-side) so typing is never blocked on the
  network.
- Be careful with batch submission: send rows sequentially in grid order (timestamps must remain ordered), report per-row
  results, and make retries safe against duplicates (a row that succeeded is never resent).

## Web, UI and configuration

- This is a local-only app. It is reachable only on the host's loopback (compose maps `127.0.0.1:9142:9142`; 9042 after
  the old app is removed), uses plain HTTP, and has **no login**. Deliberate divergences from the template: no CSP, no
  HTTPS redirection, no HSTS, no antiforgery cookie flow. Do not add them.
  - Run directly on the host (dev, tests, `dotnet run`): Kestrel binds to loopback only (`http://127.0.0.1:9142`).
  - Inside the container Kestrel must listen on all container interfaces (`ASPNETCORE_URLS=http://+:9142`), otherwise
    Docker's port publishing cannot reach it. This is not a violation: exposure is restricted by the compose port
    mapping, which publishes on `127.0.0.1` only. Never publish the port on other host interfaces and never use
    `network_mode: host`.
- Local-only does not mean unprotected: restrict `AllowedHosts` to `localhost;127.0.0.1` (DNS rebinding), accept
  state-changing BFF requests only with `Content-Type: application/json` and an `Origin` of `http://localhost:<port>` or
  `http://127.0.0.1:<port>` (plus the Vite dev origin in Development only; no Origin is accepted for non-browser
  clients), and use non-GET methods for every state change. A random web page must not be able to post
  transactions to the app's loopback port.
- Self-host fonts, scripts and styles; load nothing from CDNs.
- UI framework: follow the chosen design system. Do not introduce a second component library or ad-hoc controls
  without a clear need. Charts: use one chart approach (decide in `PLAN.md`) and keep it accessible (text/table
  alternative for the data).
- Validate configuration at startup without the network (base URL, token present, zone resolvable, ids positive, lists
  non-empty) and fail fast with a clear message. Checks that need Firefly (cash account and budgets exist) belong to
  `/health/ready` and the UI; Firefly being down must not stop the app from starting. Never log the token.
- Use structured logging (Serilog or built-in abstractions). Do not log request or response bodies or tokens. A
  transaction description is personal data: log counts and ids, not content.
- Health: `/health/live` (process only, no remote dependency) and `/health/ready` (can reach Firefly with the token).
  Support a `--health-check` command-line switch for the container healthcheck; it calls `/health/live`.
- Front-end conventions: strict TypeScript, no `any`, feature-oriented folders, server state through TanStack Query,
  keyboard handling in small tested hooks, no business rules duplicated from the server beyond input affordances (the
  server stays authoritative).

## Testing and quality

Prefer tests that exercise real behavior and boundaries:

- **Unit tests:** budget calculation, burndown series, colour scaling, money/date parsing and formatting, timestamp
  sequencing, validation, configuration validation, error mapping. Port the old `DataProcessor` behavior first.
- **Characteristics/integration tests:** the Firefly client and the BFF endpoints against a real, throwaway
  Firefly III container (Testcontainers, image tag pinned), seeded with synthetic accounts and transactions through the
  API. Cover pagination, 422 mapping, 401 handling, partial batch failure and ordering.
- **Architecture tests (NetArchTest):** dependency directions, `internal` by default, no primary constructors on
  classes, no `DateTime.Now`, no `Newtonsoft`, no string-built JSON where practical.
- **Browser tests (Playwright for .NET):** run against a real HTTP listener, never the in-memory `TestServer`. Drive
  entire entry batches with the keyboard only; assert focus movement, autosuggest accept-on-Enter/Tab, submit
  status and the resulting transactions in the throwaway Firefly. Install browsers once after a build. Check key layouts
  at about 1440 px and 1024 px.
- **Front-end unit tests (Vitest and Testing Library):** pure logic and hooks. They cannot prove real focus or layout
  behavior; cover that with the browser tests.
- Tests must never touch the real Firefly or the owner's secrets. Environment variables override user-secrets in test
  hosts: configure test hosts explicitly. Use synthetic data only.
- Cover invalid input, boundaries (first/last month, month length 28-31, zero income, negative budget), and failure
  paths, not only happy paths.
- Never pipe a long test run into `Select-Object -First N` (a cancelled run leaves the test host running and locks
  binaries). Write output to a file and read the summary from it.

### End-of-task routine

Run the smallest relevant checks, then expand when results warrant it:

1. Build with warnings as errors (and the XML-doc build when public/internal API docs changed).
2. Run the formatters and verify formatting (CSharpier and Biome).
3. Run targeted tests; run the full suite when the change affects shared behavior or project-wide contracts.
4. Inspect the final diff and ensure no generated artifacts, secrets, tokens or unrelated changes are included.
5. Update `PLAN.md` (milestone status, answered questions) and, at the end of every milestone, `HANDOVER.md`.

**Repository commands** (run from the repository root; confirmed in M0):

- Restore tools: `dotnet tool restore`
- Build: `dotnet build src/Burndown.sln -warnaserror` (the old project is not built under the new rules: the shared
  `src/Directory.Build.props`/`Directory.Packages.props` settings apply only to projects whose name starts with
  `Burndown.`, so `src/Burndown` is unaffected).
- XML-doc build: the same build with `-p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=false --no-incremental`
- Format: `dotnet csharpier format <project folders>` ; check: `dotnet csharpier check <project folders>`, where the
  folders are the `Burndown.*` project folders and `src/tests` (do not use the glob `src/Burndown.*`: it also matches
  `Burndown.sln`). Working command: `dotnet csharpier format src/Burndown.Common src/Burndown.Firefly
  src/Burndown.Overview src/Burndown.Entry src/Burndown.Web src/tests` (and `check`). A `.csharpierignore` entry for `src/Burndown/` also protects
  the old project. Never reformat `src/Burndown`.
- Test: `dotnet test --solution src/Burndown.sln` (the fast subset excludes the container-based and browser tests; see
  `HANDOVER.md`). Do not pass `--nologo` to `dotnet test`: in Microsoft.Testing.Platform mode it is forwarded to the
  test app and the run silently reports "Zero tests ran" (exit code 5).
- Front-end (in the client folder): `npm ci`, `npm run dev`, `npm run build`, `npm run lint`, `npm test`
- Publish container: `docker compose` file under `docker-compose/` (see Deployment)

Platform gotchas:

- Line endings are LF everywhere (`.editorconfig`, `.gitattributes`). Agent file tools may write CRLF; normalize new
  files.
- Windows PowerShell `>` redirection writes UTF-16: never use it to produce files.
- If Git reports "dubious ownership", use `git -c safe.directory=<repo path> ...`.
- Roslynator rules are enforced by the build (for example prefer `[0]` over `First()` on lists).
- `dotnet test --solution` with xUnit v3 needs `global.json` to select the Microsoft.Testing.Platform runner
  (`"test": { "runner": "Microsoft.Testing.Platform" }`, as in Motorsports); it is in place.
- When running the host from a built output (`dotnet Burndown.Web.dll`), the working directory must be the output
  folder, otherwise `appsettings.json` is not found and startup validation fails.
- The Vite build writes into `src/Burndown.Web/wwwroot` (git-ignored except `.gitkeep`, which the build restores). In
  Development the host serves it through the static-web-assets manifest, so tests must not assume the web root is
  empty; the host tests assert on `<html` only.
- Do not enable `InvariantGlobalization`: the `nl-BE` culture and the IANA time zone need ICU and tzdata (both are in
  the `chiseled-extra` image).
- The git root is `C:\David\Programmatuur\Burndown`; the solution file and old sources live under `src\`.

## Performance and reliability

- Measure before optimizing. Keep an optimization only if it shows a measured gain (time, allocations or Firefly round
  trips); otherwise revert it and note it in the handover.
- The overview should need a bounded number of Firefly requests per month view (budget inputs, current month
  withdrawals), requested in parallel where independent. Count round trips in tests.
- Entry must feel instant: filtering and focus movement never wait on the network; only description suggestions and
  submit do.
- Keep server memory small: a container memory limit with a matching managed heap cap
  (`DOTNET_GCHeapHardLimitPercent`). Ask the owner before raising a limit.
- Do not add retries blindly; they must be safe to replay (never retry a transaction POST automatically).

## Deployment and operations

- **Image:** multi-stage Dockerfile at `src/Burndown.Web/Dockerfile` (as in Motorsports; do **not** add a root
  `Dockerfile`: it would collide with the old root `dockerfile` on case-insensitive Windows); build the client, publish
  the host, run on
  `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` as non-root. No shell in the image: use the exec form
  healthcheck `["CMD", "dotnet", "Burndown.Web.dll", "--health-check"]`.
- **Listening:** port 9142 in the container while the old app exists (9042 afterwards); compose publishes it on
  `127.0.0.1` only. Use a different compose project/container name than the old app.
- **Firefly:** `Firefly__BaseUrl=http://host.docker.internal:8042`; add `extra_hosts: host.docker.internal:host-gateway`
  where Docker does not provide it.
- **Secrets:** `Firefly__Token` from a git-ignored `.env` file or the environment; user-secrets in development.
  Document how to create and rotate a Firefly personal access token (Profile → OAuth → Personal Access Tokens).
- **No database:** there is nothing to back up or migrate. Settings are configuration files.
- **Logging/limits:** structured console logs; set a container memory limit and measure it.
- The old publish artifacts (`publish.cmd`, `run.ps1`, the root `dockerfile`/`docker-compose.yml`, committed `dist/`)
  stay untouched until the final "remove the old app" milestone; never reproduce the old OAuth client secret.

## Open decisions

The single list of open decisions is `PLAN.md` ("Pending decisions" and "Open questions for the owner"). Do not keep a
second copy here. Resolve an item with the owner, record the answer in `PLAN.md`, and promote it into this file only if
it becomes a convention.

Decided: React + BFF stack; ids, categories and targets are configuration in `appsettings.json` (secrets in
user-secrets/`.env`); `PLAN.md` and `HANDOVER.md` live in the repository root.

## Project-specific decisions

- **Business rules:** see "Business rules" above.
- **Security/privacy:** local-only, single user, no login, real financial data: the real Firefly is never written to by
  an agent; tokens never in source, logs or tests; transaction content never logged.
- **Data retention:** the app stores nothing; Firefly III owns all data.
- **Versioning/release:** local Docker image built by the owner; version scheme per `PLAN.md`.
- **Known constraints:** Firefly's autocomplete and pagination limits; timestamps within a calendar date are strictly
  increasing in input order so Firefly shows entries as entered; the minimum month is 2023-12.
