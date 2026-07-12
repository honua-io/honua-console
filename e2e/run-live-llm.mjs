#!/usr/bin/env node
// Orchestrator for the live-LLM smoke lane (honua-console#283).
//
// Resolves a REAL AI generation provider, stands up a LOCAL honua-server wired to
// it, starts the Console bound to that server, runs the live-LLM Playwright specs
// with TOLERANT assertions, then tears everything down. NON-BLOCKING: with no
// resolvable provider the lane runs in gated mode (every spec skips, exit 0) — it
// is designed to fail only on a genuine generation regression, never on missing
// credentials.
//
// Provider resolution (first that applies wins; override with HONUA_LIVE_LLM_MODE):
//   openai  — a direct OpenAI-compatible key is present
//             (HONUA_LIVE_LLM_OPENAI_API_KEY or OPENAI_API_KEY). All four surfaces.
//   bedrock — AWS credentials resolve from the ambient chain; a LiteLLM sidecar
//             exposes an OpenAI-compatible endpoint backed by Bedrock. All four.
//   none    — nothing resolved; gated skip (proves clean gating).
//
// Usage: node e2e/run-live-llm.mjs
//   (also: npm run e2e:live-llm)
//
// Prerequisites: Docker running, dotnet SDK + npx/Playwright on PATH.
// Host ports used: 5674 (Console), 5681 (server), 5644 (PostGIS), 4000 (LiteLLM).

import { spawnSync, spawn } from 'node:child_process';
import http from 'node:http';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { existsSync, readFileSync, rmSync, mkdirSync, writeFileSync, appendFileSync } from 'node:fs';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(SCRIPT_DIR, '..');
const PW_DIR = join(SCRIPT_DIR, 'playwright');
const COMPOSE_FILE = join(PW_DIR, 'live-llm', 'docker-compose.yml');
const RESULTS_DIR = join(PW_DIR, 'live-llm', 'results');
const SURFACES_FILE = join(RESULTS_DIR, 'surfaces.jsonl');

const SERVER_URL = process.env.HONUA_CONSOLE_E2E_LIVE_LLM_SERVER_URL ?? 'http://127.0.0.1:5681';
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
const CONSOLE_PORT = process.env.HONUA_CONSOLE_E2E_LIVE_LLM_PORT ?? '5674';
const CONSOLE_BASE = `http://127.0.0.1:${CONSOLE_PORT}`;
// InvokeModel path (bedrock/invoke/...), NOT Converse: the honua strict json_schemas
// exceed Bedrock's Converse native-structured-output grammar limits, but the
// InvokeModel path satisfies them via Anthropic tool-use. See live-llm/config/litellm-config.yaml.
const DEFAULT_BEDROCK_MODEL = 'bedrock/invoke/us.anthropic.claude-haiku-4-5-20251001-v1:0';

function log(msg) { console.log(`[e2e-live-llm] ${msg}`); }

function run(cmd, args, opts = {}) {
  log(`${cmd} ${args.join(' ')}`);
  return spawnSync(cmd, args, { stdio: 'inherit', shell: false, ...opts }).status ?? 1;
}

function capture(cmd, args) {
  const r = spawnSync(cmd, args, { stdio: ['ignore', 'pipe', 'ignore'], shell: false, encoding: 'utf8' });
  return { status: r.status ?? 1, stdout: r.stdout ?? '' };
}

// Resolve AWS credentials from the ambient chain into plain env vars so the
// LiteLLM container gets them without a host-path mount (portable local + CI).
function resolveAwsCredsEnv() {
  const env = {};
  if (process.env.AWS_ACCESS_KEY_ID && process.env.AWS_SECRET_ACCESS_KEY) {
    env.AWS_ACCESS_KEY_ID = process.env.AWS_ACCESS_KEY_ID;
    env.AWS_SECRET_ACCESS_KEY = process.env.AWS_SECRET_ACCESS_KEY;
    if (process.env.AWS_SESSION_TOKEN) env.AWS_SESSION_TOKEN = process.env.AWS_SESSION_TOKEN;
    return env;
  }
  // Fall back to the AWS CLI's resolved credentials (shared profile, SSO, role).
  const r = capture('aws', ['configure', 'export-credentials', '--format', 'env']);
  if (r.status !== 0) return null;
  for (const line of r.stdout.split(/\r?\n/)) {
    const m = line.match(/^export (AWS_[A-Z_]+)=(.*)$/);
    if (m) env[m[1]] = m[2];
  }
  return env.AWS_ACCESS_KEY_ID ? env : null;
}

function resolveProvider() {
  const forced = process.env.HONUA_LIVE_LLM_MODE;
  const openaiKey = process.env.HONUA_LIVE_LLM_OPENAI_API_KEY || process.env.OPENAI_API_KEY;

  if ((forced === 'openai' || (!forced && openaiKey)) && openaiKey) {
    return {
      mode: 'openai',
      composeEnv: {
        HONUA_LIVE_LLM_PROVIDER: 'openai',
        HONUA_LIVE_LLM_ENDPOINT: process.env.HONUA_LIVE_LLM_OPENAI_ENDPOINT || 'https://api.openai.com/v1',
        HONUA_LIVE_LLM_MODEL: process.env.HONUA_LIVE_LLM_OPENAI_MODEL || 'gpt-4o-mini',
        HONUA_LIVE_LLM_API_KEY: openaiKey,
      },
      profiles: [],
    };
  }

  if (forced === 'bedrock' || !forced) {
    const aws = resolveAwsCredsEnv();
    if (aws) {
      return {
        mode: 'bedrock',
        composeEnv: {
          HONUA_LIVE_LLM_PROVIDER: 'local', // LiteLLM endpoint is http -> use the http-tolerant provider id
          HONUA_LIVE_LLM_ENDPOINT: 'http://localhost:4000/v1',
          HONUA_LIVE_LLM_MODEL: 'honua-llm',
          HONUA_LIVE_LLM_API_KEY: '',
          HONUA_LIVE_LLM_BEDROCK_MODEL: process.env.HONUA_LIVE_LLM_BEDROCK_MODEL || DEFAULT_BEDROCK_MODEL,
          AWS_REGION: process.env.AWS_REGION || process.env.AWS_DEFAULT_REGION || 'us-west-2',
          ...aws,
        },
        profiles: ['bedrock'],
      };
    }
  }

  return { mode: 'none', composeEnv: {}, profiles: [] };
}

function waitForUrl(url, timeoutMs) {
  return new Promise((resolve, reject) => {
    const deadline = Date.now() + timeoutMs;
    const attempt = () => {
      const req = http.get(url, (res) => {
        res.resume();
        if (res.statusCode < 400) resolve();
        else if (Date.now() < deadline) setTimeout(attempt, 1000);
        else reject(new Error(`${url} returned ${res.statusCode}`));
      });
      req.on('error', () => (Date.now() < deadline ? setTimeout(attempt, 1000) : reject(new Error(`${url} unreachable`))));
      req.setTimeout(2000, () => req.destroy());
    };
    attempt();
  });
}

function killTree(pid) {
  if (!pid) return;
  if (process.platform === 'win32') spawnSync('taskkill', ['/F', '/T', '/PID', String(pid)], { stdio: 'inherit' });
  else { try { process.kill(-pid, 'SIGKILL'); } catch {} }
}

function composeArgs(profiles, ...rest) {
  const args = ['compose', '-f', COMPOSE_FILE];
  for (const p of profiles) args.push('--profile', p);
  return args.concat(rest);
}

function writeSummary(mode) {
  const surfaces = [];
  if (existsSync(SURFACES_FILE)) {
    for (const line of readFileSync(SURFACES_FILE, 'utf8').split(/\r?\n/)) {
      if (line.trim()) { try { surfaces.push(JSON.parse(line)); } catch {} }
    }
  }
  const rows = surfaces.map((s) => `| ${s.surface} | ${s.status} | ${s.detail || ''} |`).join('\n');
  const md = [
    '## Live-LLM smoke lane',
    '',
    `**Provider mode:** \`${mode}\``,
    '',
    surfaces.length ? '| Surface | Result | Detail |\n| --- | --- | --- |\n' + rows
      : (mode === 'none'
        ? '_No provider resolved — lane ran in gated mode; all specs skipped (non-blocking)._'
        : '_No surfaces recorded._'),
    '',
  ].join('\n');
  log('\n' + md);
  if (process.env.GITHUB_STEP_SUMMARY) {
    try { appendFileSync(process.env.GITHUB_STEP_SUMMARY, md + '\n'); } catch {}
  }
}

// ── main ────────────────────────────────────────────────────────────────────
const provider = resolveProvider();
log(`resolved provider mode: ${provider.mode}`);

// Fresh surface ledger for this run.
try { rmSync(RESULTS_DIR, { recursive: true, force: true }); } catch {}
mkdirSync(RESULTS_DIR, { recursive: true });
writeFileSync(join(RESULTS_DIR, '.gitkeep'), '');

const childEnvBase = { ...process.env, ...provider.composeEnv };
const specEnv = {
  HONUA_LIVE_LLM_ENABLED: provider.mode === 'none' ? '' : '1',
  HONUA_LIVE_LLM_MODE: provider.mode,
  HONUA_CONSOLE_E2E_LIVE_LLM_SERVER_URL: SERVER_URL,
  HONUA_CONSOLE_E2E_LIVE_LLM_PORT: CONSOLE_PORT,
  HONUA_CONSOLE_E2E_ADMIN_KEY: ADMIN_KEY,
};

let consoleProc = null;
let stackUp = false;
let pwExit = 0;

try {
  if (provider.mode === 'none') {
    log('no provider — running the lane in gated mode (specs skip; proves clean gating).');
    const pw = spawnSync('npx', ['playwright', 'test', '--config', 'playwright.live-llm.config.ts'],
      { cwd: PW_DIR, stdio: 'inherit', shell: false, env: { ...process.env, ...specEnv } });
    pwExit = pw.status ?? 1;
  } else {
    // 1. Pull + start the stack (blocks on healthchecks).
    run('docker', composeArgs(provider.profiles, 'pull', '--quiet'), { env: childEnvBase });
    const upCode = run('docker', composeArgs(provider.profiles, 'up', '-d', '--wait'), { env: childEnvBase });
    if (upCode !== 0) {
      log('stack failed to start — logs follow');
      run('docker', composeArgs(provider.profiles, 'logs', '--tail=60'), { env: childEnvBase });
      throw new Error('stack up failed');
    }
    stackUp = true;
    await waitForUrl(`${SERVER_URL}/healthz/ready`, 120_000);
    log('honua-server ready.');

    // 2. Start the Console bound to the stack.
    log(`starting Console on ${CONSOLE_BASE} (server -> ${SERVER_URL})...`);
    const consoleOpts = {
      stdio: 'inherit', shell: false,
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_CLI_TELEMETRY_OPTOUT: '1',
        ASPNETCORE_URLS: CONSOLE_BASE, HONUA_SERVER_BASE_URL: SERVER_URL, HONUA_ADMIN_API_KEY: ADMIN_KEY },
    };
    if (process.platform === 'win32') consoleOpts.detached = true;
    consoleProc = spawn('dotnet',
      ['run', '--project', join(REPO_ROOT, 'src/Honua.Console.Web/Honua.Console.Web.csproj'), '--urls', CONSOLE_BASE],
      consoleOpts);
    if (process.platform !== 'win32') consoleProc.unref?.();
    await waitForUrl(`${CONSOLE_BASE}/version.json`, 180_000);
    log('Console ready.');

    // 3. Run the live-LLM specs.
    const pw = spawnSync('npx', ['playwright', 'test', '--config', 'playwright.live-llm.config.ts'],
      { cwd: PW_DIR, stdio: 'inherit', shell: false, env: { ...process.env, ...specEnv } });
    pwExit = pw.status ?? 1;
  }
} catch (err) {
  log(`error: ${err.message}`);
  pwExit = pwExit || 1;
} finally {
  if (consoleProc) { log('stopping Console...'); killTree(consoleProc.pid); }
  if (stackUp) { log('tearing down stack...'); run('docker', composeArgs(provider.profiles, 'down', '-v'), { env: childEnvBase }); }
  writeSummary(provider.mode);
}

process.exit(pwExit);
