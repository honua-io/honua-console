#!/usr/bin/env node
// Cross-platform runner for the console live e2e harness.
// Brings up the Docker stack, starts the Console (bound to the stack), runs Playwright live
// specs, tears down, and exits with the Playwright exit code so CI can fail the build correctly.
//
// Usage: node e2e/run-live.mjs
//   (also called by: npm run e2e:live, make e2e-live)
//
// Prerequisites: Docker running, dotnet SDK on PATH, npx/Playwright installed.
// Port 5176 must be free before running.

import { spawnSync, spawn } from 'node:child_process';
import http from 'node:http';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(SCRIPT_DIR, '..');
const COMPOSE_FILE = join(SCRIPT_DIR, 'docker-compose.yml');
const PW_DIR = join(SCRIPT_DIR, 'playwright');

const SERVER_URL = process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088';
const ADMIN_KEY  = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY  ?? 'honua-console-dev-key';
const CONSOLE_PORT = process.env.HONUA_CONSOLE_E2E_LIVE_PORT ?? '5176';
const CONSOLE_BASE = `http://127.0.0.1:${CONSOLE_PORT}`;

function run(cmd, args, opts = {}) {
  console.log(`\n[e2e-live] ${cmd} ${args.join(' ')}`);
  const result = spawnSync(cmd, args, { stdio: 'inherit', shell: false, ...opts });
  return result.status ?? 1;
}

// Poll a URL until it returns HTTP < 400, or throw after timeoutMs.
function waitForUrl(url, timeoutMs) {
  return new Promise((resolve, reject) => {
    const deadline = Date.now() + timeoutMs;
    function attempt() {
      const req = http.get(url, (res) => {
        res.resume();
        if (res.statusCode < 400) {
          resolve();
        } else if (Date.now() < deadline) {
          setTimeout(attempt, 1000);
        } else {
          reject(new Error(`${url} returned ${res.statusCode} — gave up after ${timeoutMs}ms`));
        }
      });
      req.on('error', () => {
        if (Date.now() < deadline) {
          setTimeout(attempt, 1000);
        } else {
          reject(new Error(`${url} not reachable after ${timeoutMs}ms`));
        }
      });
      req.setTimeout(2000, () => { req.destroy(); });
    }
    attempt();
  });
}

// Kill a spawned process and its child tree (handles dotnet run -> app sub-process on Windows).
function killTree(pid) {
  if (!pid) return;
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/F', '/T', '/PID', String(pid)], { stdio: 'inherit', shell: false });
  } else {
    try { process.kill(-pid, 'SIGKILL'); } catch {}
  }
}

// ── 1. Pull images ───────────────────────────────────────────────────────────
run('docker', ['compose', '-f', COMPOSE_FILE, 'pull', '--quiet']);

// ── 2. Start stack (blocks until all healthchecks pass) ─────────────────────
const upCode = run('docker', ['compose', '-f', COMPOSE_FILE, 'up', '-d', '--wait']);
if (upCode !== 0) {
  console.error('[e2e-live] stack failed to start — aborting');
  run('docker', ['compose', '-f', COMPOSE_FILE, 'logs', '--tail=50']);
  run('docker', ['compose', '-f', COMPOSE_FILE, 'down', '-v']);
  process.exit(upCode);
}

// ── 3. Start Console bound to the stack ─────────────────────────────────────
// Playwright's webServer config uses reuseExistingServer:true (the default for local runs),
// so we pre-start the Console here with the correct HONUA_SERVER_BASE_URL so Playwright's
// URL check finds it live. We spawn dotnet directly (not via shell) to avoid cmd.exe issues
// in Git Bash / Unix-like environments on Windows.
console.log(`\n[e2e-live] starting Console on ${CONSOLE_BASE} (server → ${SERVER_URL})...`);
const CONSOLE_PROJECT = join(REPO_ROOT, 'src/Honua.Console.Web/Honua.Console.Web.csproj');
const consoleProcOptions = {
  stdio: 'inherit',
  shell: false,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    ASPNETCORE_URLS: CONSOLE_BASE,          // belt-and-suspenders alongside --urls arg below
    HONUA_SERVER_BASE_URL: SERVER_URL,
    HONUA_ADMIN_API_KEY: ADMIN_KEY,
  },
};
// detached:true on Windows so the Console process group can be killed via killTree
if (process.platform === 'win32') consoleProcOptions.detached = true;
const consoleProc = spawn(
  'dotnet',
  ['run', '--project', CONSOLE_PROJECT, '--urls', CONSOLE_BASE],
  consoleProcOptions,
);
if (process.platform !== 'win32') consoleProc.unref?.();

let pwExit = 1;
try {
  await waitForUrl(`${CONSOLE_BASE}/version.json`, 180_000);
  console.log('[e2e-live] Console ready.');

  // ── 4. Run Playwright live specs ─────────────────────────────────────────
  console.log('\n[e2e-live] running live Playwright specs...');
  const pw = spawnSync(
    'npx',
    ['playwright', 'test', '--config', 'playwright.live.config.ts'],
    {
      cwd: PW_DIR,
      stdio: 'inherit',
      shell: false,
      env: {
        ...process.env,
        HONUA_CONSOLE_E2E_SERVER_URL: SERVER_URL,
        HONUA_CONSOLE_E2E_ADMIN_KEY:  ADMIN_KEY,
      },
    },
  );
  pwExit = pw.status ?? 1;
} finally {
  // ── 5. Stop Console ───────────────────────────────────────────────────────
  console.log('\n[e2e-live] stopping Console...');
  killTree(consoleProc.pid);

  // ── 6. Tear down Docker stack ─────────────────────────────────────────────
  console.log('\n[e2e-live] tearing down stack...');
  run('docker', ['compose', '-f', COMPOSE_FILE, 'down', '-v']);
}

process.exit(pwExit);
