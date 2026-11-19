#!/usr/bin/env node
// Orchestrator for the live-LLM smoke lane.
//
// Resolves a REAL AI provider, starts the Console with its /support GIS assistant
// wired to that provider, runs the live-LLM Playwright spec, then tears everything
// down. NON-BLOCKING by design: with no resolvable provider the lane runs in gated
// mode (the spec skips, exit 0). With a provider, a missing or failing assistant
// is a real regression and fails the lane.
//
// There is no honua-server in this lane: under ADR-0076 studio generation is
// client-side inference, covered by the sdk-js mcp-eval-live lane.
//
// Provider resolution (first that applies wins; override with HONUA_LIVE_LLM_MODE):
//   openai  — a direct OpenAI-compatible key is present
//             (HONUA_LIVE_LLM_OPENAI_API_KEY or OPENAI_API_KEY). No containers.
//   bedrock — AWS credentials resolve from the ambient chain; a LiteLLM sidecar
//             exposes an OpenAI-compatible endpoint backed by Bedrock.
//   none    — nothing resolved; gated skip (proves clean gating).
//
// Usage: node e2e/run-live-llm.mjs
//   (also: npm run e2e:live-llm)
//
// Prerequisites: dotnet SDK + npx/Playwright on PATH; Docker for bedrock mode.
// Host ports used: 5674 (Console), 4000 (LiteLLM, bedrock mode).

import { spawnSync, spawn } from 'node:child_process';
import http from 'node:http';
import { randomBytes } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { existsSync, readFileSync, rmSync, mkdirSync, writeFileSync, appendFileSync } from 'node:fs';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(SCRIPT_DIR, '..');
const PW_DIR = join(SCRIPT_DIR, 'playwright');
const COMPOSE_FILE = join(PW_DIR, 'live-llm', 'docker-compose.yml');
const RESULTS_DIR = join(PW_DIR, 'live-llm', 'results');
const SURFACES_FILE = join(RESULTS_DIR, 'surfaces.jsonl');

const CONSOLE_PORT = process.env.HONUA_CONSOLE_E2E_LIVE_LLM_PORT ?? '5674';
const CONSOLE_BASE = `http://127.0.0.1:${CONSOLE_PORT}`;
// InvokeModel path (bedrock/invoke/...); see live-llm/config/litellm-config.yaml.
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
      stack: false,
      composeEnv: {},
      consoleEnv: {
        HONUA_LLM_BASE_URL: process.env.HONUA_LIVE_LLM_OPENAI_ENDPOINT || 'https://api.openai.com/v1',
        HONUA_LLM_MODEL: process.env.HONUA_LIVE_LLM_OPENAI_MODEL || 'gpt-4o-mini',
        HONUA_LLM_API_KEY: openaiKey,
      },
    };
  }

  if (forced === 'bedrock' || !forced) {
    const aws = resolveAwsCredsEnv();
    if (aws) return bedrockProvider(aws);
  }

  return { mode: 'none', stack: false, composeEnv: {}, consoleEnv: {} };
}

function bedrockProvider(aws) {
  // Per-run gateway key: LiteLLM refuses to start without one, and with it
  // nothing but this run's Console can spend the Bedrock credential.
  const gatewayKey = `sk-${randomBytes(24).toString('hex')}`;
  return {
    mode: 'bedrock',
    stack: true,
    composeEnv: {
      LITELLM_MASTER_KEY: gatewayKey,
      HONUA_LIVE_LLM_BEDROCK_MODEL: process.env.HONUA_LIVE_LLM_BEDROCK_MODEL || DEFAULT_BEDROCK_MODEL,
      AWS_REGION: process.env.AWS_REGION || process.env.AWS_DEFAULT_REGION || 'us-west-2',
      ...aws,
    },
    consoleEnv: {
      HONUA_LLM_BASE_URL: 'http://127.0.0.1:4000/v1',
      HONUA_LLM_MODEL: 'honua-llm',
      HONUA_LLM_API_KEY: gatewayKey,
    },
  };
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

function composeArgs(...rest) {
  return ['compose', '-f', COMPOSE_FILE, ...rest];
}

// Bedrock mode only: start the LiteLLM gateway (blocks on its healthcheck).
function startGateway(env) {
  run('docker', composeArgs('pull', '--quiet'), { env });
  const upCode = run('docker', composeArgs('up', '-d', '--wait'), { env });
  if (upCode !== 0) {
    log('LiteLLM failed to start — logs follow');
    run('docker', composeArgs('logs', '--tail=60'), { env });
    throw new Error('stack up failed');
  }
  log('LiteLLM gateway ready.');
}

// Start the legacy regression fixture with the support assistant on the provider.
// Returns before readiness so the caller can always stop it.
function startConsole(consoleEnv) {
  log(`starting Console on ${CONSOLE_BASE} (assistant model ${consoleEnv.HONUA_LLM_MODEL})...`);
  // detached: own process group, so killTree's kill(-pid) reaches dotnet's child.
  const proc = spawn('dotnet',
    ['run', '--project', join(REPO_ROOT, 'tests/Honua.Console.LegacyHost/Honua.Console.LegacyHost.csproj'), '--urls', CONSOLE_BASE],
    {
      stdio: 'inherit', shell: false, detached: true,
      env: { ...process.env, ...consoleEnv, ASPNETCORE_ENVIRONMENT: 'Development',
        DOTNET_CLI_TELEMETRY_OPTOUT: '1', ASPNETCORE_URLS: CONSOLE_BASE },
    });
  // A spawn failure (no dotnet on PATH) surfaces as the readiness timeout, not a crash past teardown.
  proc.on('error', (err) => log(`Console failed to start: ${err.message}`));
  if (process.platform !== 'win32') proc.unref?.();
  return proc;
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
  HONUA_CONSOLE_E2E_LIVE_LLM_PORT: CONSOLE_PORT,
};

let consoleProc = null;
let stackUp = false;
let pwExit = 0;

function runSpecs() {
  const pw = spawnSync('npx', ['playwright', 'test', '--config', 'playwright.live-llm.config.ts'],
    { cwd: PW_DIR, stdio: 'inherit', shell: false, env: { ...process.env, ...specEnv } });
  return pw.status ?? 1;
}

async function runLane() {
  if (provider.mode === 'none') {
    log('no provider — running the lane in gated mode (specs skip; proves clean gating).');
    return runSpecs();
  }
  // Set before starting, so teardown also runs after a failed start.
  stackUp = provider.stack;
  if (stackUp) startGateway(childEnvBase);
  consoleProc = startConsole(provider.consoleEnv);
  await waitForUrl(`${CONSOLE_BASE}/version.json`, 180_000);
  log('Console ready.');
  return runSpecs();
}

try {
  pwExit = await runLane();
} catch (err) {
  log(`error: ${err.message}`);
  pwExit = pwExit || 1;
} finally {
  if (consoleProc) { log('stopping Console...'); killTree(consoleProc.pid); }
  if (stackUp) { log('tearing down stack...'); run('docker', composeArgs('down', '-v'), { env: childEnvBase }); }
  writeSummary(provider.mode);
}

process.exit(pwExit);
