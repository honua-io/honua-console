# Agent Browser Testing (Claude Code + Playwright MCP)

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Route truth: [Console Route Map](../console-route-map.md).
Run/bindings: [Local and Staging Startup](../deployment/LOCAL_AND_STAGING.md).

This is the root setup-and-run guide for driving the **live Honua Console UI in a real
browser with Claude Code**, using the [Playwright MCP](https://www.npmjs.com/package/@playwright/mcp)
server. It is the only browser-automation path in this repo: the existing
`npm run smoke:parity` and `dotnet test` suites are **HTTP/contract and bUnit component**
checks — they never open a browser. Use this when you want an agent to actually load
`/studio`, `/catalog`, `/operate`, `/share`, click through them, and screenshot/assert what
renders against a live honua-server.

## Topology

The supported layout is **app in WSL, browser + Claude Code on Windows**:

```
  Windows (native)                          WSL2 (Ubuntu)
  ----------------                          -------------
  Claude Code  ──spawns──▶  Playwright MCP ─┐
                            (Windows Chrome) │  http://localhost:5174
                                             └─────────────▶  Honua Console (dotnet run)
                                                                     │  server-side, in WSL
                                                                     ▼
                                                              honua-server (:8088)
                                                                     ▼
                                                              PostGIS (:5432 in-network)
```

Only the **Console port (5174)** needs to be reachable from Windows. The browser never talks
to honua-server or PostGIS directly — the Console calls those server-side, inside WSL. So you
do **not** need to expose 8088/5432 to Windows.

## Prerequisites

**In WSL** (where the stack runs):
- Docker (for PostGIS + honua-server). `docker version` should work.
- .NET 10 SDK and Node.js 20+ (`dotnet --version`, `node --version`).
- This repo checked out (you are reading its docs).

**On Windows** (where Claude drives the browser):
- [Claude Code for Windows](https://code.claude.com/docs) installed and signed in.
- Node.js 20+ on the Windows side (`node --version`, `where npx`) — Playwright MCP runs as a
  Windows process and downloads a Windows browser build the first time.

## Part 1 — Bring up the PostGIS-backed server + Console (in WSL)

The Console renders an explicit *missing-binding* surface unless a honua-server base URL is
configured, so a live backend is required for meaningful browser tests. The fast path uses the
prebuilt nightly server image (no source build).

> Admin-auth nuance: in `Development` the server requires a real API key — there is **no**
> dev-auth bypass (that only activates when `ASPNETCORE_ENVIRONMENT=Test`). Set
> `HONUA_ADMIN_PASSWORD` on the server and the **same value** as `HONUA_ADMIN_API_KEY` on the
> Console; the Console sends it as the `X-API-Key` header.

### 1a. Create a testbed folder with two files

`docker-compose.yml`:

```yaml
name: honua-console-testbed
services:
  postgres:
    image: postgis/postgis:17-3.5-alpine
    environment:
      POSTGRES_DB: honua_dev
      POSTGRES_USER: honua_user
      POSTGRES_PASSWORD: honua_password
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init-db.sql:/docker-entrypoint-initdb.d/init-db.sql:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U honua_user -d honua_dev"]
      interval: 5s
      timeout: 5s
      retries: 10

  honua:
    image: ghcr.io/honua-io/honua-server:nightly
    ports:
      - "8088:8080"   # REST/admin — only needed for the SDK check from WSL, not from Windows
      - "8089:8081"   # gRPC
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      HONUA_ADMIN_PASSWORD: "honua-console-dev-key"
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=honua_dev;Username=honua_user;Password=honua_password"
      Security__ConnectionEncryption__MasterKey: "local-dev-master-key-not-for-production-0123456789"
      Security__ConnectionEncryption__Salt: "MDEyMzQ1Njc4OWFiY2RlZg=="
      Kestrel__Endpoints__Http__Url: "http://+:8080"
      Kestrel__Endpoints__Http__Protocols: "Http1"
      Kestrel__Endpoints__Grpc__Url: "http://+:8081"
      Kestrel__Endpoints__Grpc__Protocols: "Http2"
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  postgres_data:
```

`init-db.sql` (PostGIS extensions the server expects):

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
CREATE EXTENSION IF NOT EXISTS postgis_tiger_geocoder;
CREATE SCHEMA IF NOT EXISTS honua;
```

> Ports 8088/8089 are offset from the canonical `docker-compose.yml` in the `honua-server` repo
> (which uses 8080) so this testbed does not collide with other local stacks. To build the
> server from your local source instead of the nightly image, use that repo's
> `docker-compose.yml` and add `HONUA_ADMIN_PASSWORD` to its `honua` service.

### 1b. Start the backend and wait for health

```bash
cd /path/to/testbed
docker compose up -d
python -m pip install honua-admin
python - <<'PY'
from honua_admin import HonuaAdminClient

with HonuaAdminClient(
    "http://127.0.0.1:8088",
    api_key="honua-console-dev-key",
) as admin:
    print(admin.get_version())
PY
```

The version response means the server is up and the key is valid.

### 1c. Run the Console, bound so Windows can reach it

Bind to `0.0.0.0` (not `127.0.0.1`) so the Windows side can connect reliably — WSL2 localhost
forwarding is more dependable when the listener is on all interfaces:

```bash
cd /home/<you>/honua-io/honua-console
HONUA_SERVER_BASE_URL=http://127.0.0.1:8088 \
HONUA_ADMIN_API_KEY=honua-console-dev-key \
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://0.0.0.0:5174
```

Leave this running (it is a long-running server — do **not** wrap it in the build lock).

## Part 2 — Make the Console reachable from Windows

Preferred: WSL2 forwards localhost to WSL services by default. Open
<http://localhost:5174/version.json> in the Windows browser.

If `localhost` does not resolve to the WSL service (some WSL/network configs disable
forwarding), use the WSL IP directly. In WSL run `hostname -I` (take the first address, e.g.
`172.x.x.x`), then open `http://172.x.x.x:5174/version.json` in the Windows browser.

Whichever URL returns `version.json` is the **base URL** you will give Claude/Playwright.

## Part 3 — Add Playwright MCP to Claude Code (on Windows)

From your project directory on Windows:

```powershell
claude mcp add --scope project playwright -- npx -y @playwright/mcp@latest
```

This writes a project-scoped `.mcp.json`:

```json
{
  "mcpServers": {
    "playwright": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@playwright/mcp@latest"]
    }
  }
}
```

Notes:
- **No `cmd /c` wrapper** is needed; Claude Code on Windows runs the command directly.
- **First start downloads ~200 MB** of browser binaries (cached afterward under
  `%USERPROFILE%\.cache\ms-playwright`). If the server times out on first connect, raise the
  startup timeout: PowerShell `$env:MCP_TIMEOUT = "60000"; claude`.
- Scope choices: `--scope user` (all your projects), `--scope project` (shared via committed
  `.mcp.json`), or omit `--scope` for local-only. A project-scoped `.mcp.json` triggers a
  one-time **"approve MCP servers for this project?"** trust prompt on first use.
- Useful flags go into `args` after `@playwright/mcp@latest`: `--headless` (no visible window),
  `--browser=chromium|firefox|webkit|msedge`, `--isolated` (fresh profile per session). Default
  is headed Chromium, which is handy for watching the agent work.

Verify the connection:

```powershell
claude mcp list        # expect: ✓ Connected   playwright
```

…or inside a session run `/mcp` and confirm `playwright` is listed with its `browser_*` tools.

## Part 4 — Run a browser test with Claude

Start Claude Code in the project and prompt it against your base URL, e.g.:

```
Using playwright, open http://localhost:5174/operate and tell me what renders.
Then click into each Operate section, screenshot each page, and report any error UI
("An unhandled error has occurred") or blank panels.
```

The first time Claude calls a browser tool you'll get a permission prompt
(*Allow `browser_navigate` from `playwright`?*). Approve it; you can pre-allow the
`playwright` tools in `.claude/settings.json` (via the `/update-config` skill) to cut repeat
prompts.

What to expect: the backend starts **empty** (fresh PostGIS volume), so Operate/Catalog show
real-but-empty data, not demo seed — that is the genuine live binding, not a failure. Seed a
connection/layer through the UI or the admin API if you want populated pages to assert against.

## Teardown

```bash
# stop the Console: Ctrl-C in its terminal (or kill the dotnet run process)
cd /path/to/testbed
docker compose down        # keep data
docker compose down -v      # also wipe the PostGIS volume
```

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| Windows browser can't reach `localhost:5174` | Bind the Console to `0.0.0.0` (Part 1c) and/or use the WSL IP from `hostname -I` (Part 2). |
| Every page renders then goes blank / "An unhandled error has occurred" | A Blazor circuit crash (e.g. an ambiguous-route regression). Check the `dotnet run` console output for the exception — this is an app bug, not a Playwright issue. |
| Operate/Catalog show "missing binding" | `HONUA_SERVER_BASE_URL` / `HONUA_ADMIN_API_KEY` not set on the Console, or the server isn't up. Re-check Part 1c and the `admin.get_version()` SDK check. |
| `401 API key required` from the server | `HONUA_ADMIN_API_KEY` (Console) must equal `HONUA_ADMIN_PASSWORD` (server). |
| `claude mcp list` shows `✗ Failed to connect` | Run `npx -y @playwright/mcp@latest` directly on Windows to see the real error (Node missing, first-run browser download, PATH). Raise `MCP_TIMEOUT`. |
| Pages are empty | Expected on a fresh DB — seed data first. |

## Reference

- Playwright MCP (Microsoft): <https://github.com/microsoft/playwright-mcp> — publishes the
  `@playwright/mcp` npm package this guide uses.
- [Claude Code MCP docs](https://code.claude.com/docs/en/mcp).
