# Burndown rewrite – handover

Read this first. Update it at the end of every milestone. No secrets, tokens or real financial data here.

## Status

| Milestone | Status |
|-----------|--------|
| Planning (AGENTS.md, PLAN.md) | Reviewed twice; all owner questions answered (D12, D15, D21); ready for M0 |
| M0 Scaffolding and tooling | Not started |
| M1 Firefly client and test infrastructure | Not started |
| M2 Overview back-end | Not started |
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

## Next: M0 – Scaffolding and tooling

See `PLAN.md`. Configuration defaults are settled: account `16` is Firefly's Cash account (setting `CashAccountId`),
budgets `13` = David and `14` = Tina. No open owner questions.

## Environment facts

- Branch: `rework`. Git root `C:\David\Programmatuur\Burndown`. Solution: `src\Burndown.sln` (old project `Burndown` plus,
  later, the new `Burndown.*` projects).
- Toolchain: .NET SDKs 8, 9 and 10.0.200, Docker 29.6 (Rancher Desktop), Node 24.19.
- Real Firefly III: `http://localhost:8042` (real data: read-only unless the owner asks, never written to by an agent).
- Old app: port 9042. New app: port 9142 until the old one is removed.
- Reference repos: `C:\_git\MovieDB`, `C:\_git\Motorsports` (never read their `prompt-history*` files).
- Uncommitted owner changes: `src/Burndown/appsettings.Development.json` (contains the old OAuth secret: never touch,
  stage or copy it). `src/Burndown.sln` is currently clean.

## Learnings

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
