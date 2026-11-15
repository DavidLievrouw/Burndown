# Burndown

A personal, single-user, keyboard-first efficiency layer on top of [Firefly III](https://www.firefly-iii.org/):

- **Entry:** bulk entry of expenses, income and transfers without touching the mouse, submitted row by row.
- **Overview:** the monthly burndown budget (table and chart).

The back-end is a .NET 10 backend-for-frontend (`src/Burndown.Web`); the front-end is a React + TypeScript single-page
app (`src/Burndown.Client`) that is served from the host's `wwwroot`. There is no database and no login: Firefly III is
the single source of truth and the app is reachable on the local machine only.

`PLAN.md` holds the product specification and decisions, `HANDOVER.md` the status. `src/Burndown` is the legacy
application (port 9042); it is untouched and runs side by side with this one (port **9142**).

## Run with Docker

1. Create a Firefly III personal access token (see below).
2. Copy `docker-compose/burndown/.env.example` to `docker-compose/burndown/.env` and set `FIREFLY_TOKEN`. The `.env`
   file is git-ignored. Optionally set `FIREFLY_BASE_URL` (default `http://host.docker.internal:8042`).
3. Build and start (from the repository root):

   ```powershell
   docker compose -f docker-compose/burndown/docker-compose.yml up -d --build
   ```

4. Open <http://127.0.0.1:9142> (or `http://localhost:9142`). Other host names are rejected (`AllowedHosts`).

The container publishes the port on `127.0.0.1` only, runs as a non-root user on a read-only file system with a 256 MB
memory limit, and reports its health through `dotnet Burndown.Web.dll --health-check` (which calls `/health/live`).
Never publish the port on another interface: the app has no login.

- `GET /health/live`: the process is up (no remote dependency).
- `GET /health/ready`: Firefly III is reachable with the token and the configured ids (cash account, budgets) exist.

Check the logs with `docker logs burndown-web`. The token is never logged.

### Configuration

Defaults are in `src/Burndown.Web/appsettings.json` (section `Burndown`: time zone, cash account id, categories, target
tags, budgets). Override a value in the container with an environment variable, for example
`Burndown__Entry__CashAccountId=16`. Configuration is validated at startup without the network; a wrong value stops the
app with a clear message. Checks that need Firefly belong to `/health/ready`.

The image tag is the version in `version.txt` (override with `BURNDOWN_VERSION`).

### Create or rotate the Firefly III token

1. In Firefly III open **Profile → OAuth → Personal Access Tokens** and choose **Create new token**.
2. Copy the token into `docker-compose/burndown/.env` as `FIREFLY_TOKEN`.
3. Recreate the container: `docker compose -f docker-compose/burndown/docker-compose.yml up -d`.
4. Delete the old token in the same Firefly III screen.

Tokens expire after about a year. An expired or invalid token shows as a clear "token invalid or expired" error in the
app and as `Unhealthy` on `/health/ready`. Never commit a token.

## Develop

Requirements: .NET SDK 10 (see `global.json`), Node.js 24, Docker (for the characteristics tests).

```powershell
dotnet tool restore
dotnet user-secrets set "Firefly:Token" "<token>" --project src/Burndown.Web
cd src/Burndown.Client; npm ci
```

- Host (binds to `http://127.0.0.1:9142`): `dotnet run --project src/Burndown.Web`
- Client dev server with proxy (`http://localhost:5173`): `npm run dev` in `src/Burndown.Client`
- Client build into the host's `wwwroot`: `npm run build`

> **Warning:** a token pointing at your real Firefly III lets the app write real transactions. Automated runs and tests
> use a throwaway Firefly III container with synthetic data only.

### Checks

```powershell
dotnet build src/Burndown.sln -warnaserror
dotnet csharpier check src/Burndown.Common src/Burndown.Firefly src/Burndown.Overview src/Burndown.Entry src/Burndown.Web src/tests
dotnet test --solution src/Burndown.sln --filter-not-trait "Category=Characteristics" --filter-not-trait "Category=Browser"
cd src/Burndown.Client; npm run lint; npx tsc --noEmit; npm test
```

The characteristics tests (`src/tests/Burndown.CharacteristicsTests`) start a throwaway Firefly III container and need
Docker. The browser tests (`src/tests/Burndown.BrowserTests`) drive the real SPA with Playwright against a scripted
fake Firefly; build the client first and install the browser once after a build
(`pwsh src/tests/Burndown.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium`).
`HANDOVER.md` has the details and the exact commands.
