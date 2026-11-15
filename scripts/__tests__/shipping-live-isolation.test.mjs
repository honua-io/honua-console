import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { test } from 'node:test';

const runner = resolve('e2e/run-shipping-live.sh');
const commandStub = `#!${process.execPath}
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
const command = path.basename(process.argv[1]);
const env = Object.fromEntries(Object.entries(process.env).filter(([key]) =>
  /^(COMPOSE_|HONUA_(HTTP|GRPC|MANAGER|CONSOLE)_PORT|HONUA_CONSOLE_EDGE_|HONUA_SHIPPING_BASE_URL)/.test(key)));
const entry = { command, args, env, cwd: process.cwd() };
if (command === 'python3' && args[0].endsWith('package_quickstart.py')) {
  fs.mkdirSync(args[args.indexOf('--output') + 1], { recursive: true });
}
if (command === 'python3' && args[0] === 'quickstart.py') {
  entry.settings = fs.readFileSync('.env', 'utf8');
  entry.override = fs.existsSync('compose.override.yaml')
    ? fs.readFileSync('compose.override.yaml', 'utf8') : null;
}
if (command === 'python3' && args[0] === '-') entry.script = fs.readFileSync(0, 'utf8');
fs.appendFileSync(process.env.COMMAND_LOG, JSON.stringify(entry) + '\\n');
if (command === process.env.FAIL_COMMAND) process.exit(19);
`;

function replay(overrides = {}) {
  const root = mkdtempSync(join(tmpdir(), 'shipping-isolation-'));
  const bin = join(root, 'bin');
  mkdirSync(bin);
  mkdirSync(join(root, 'e2e/playwright'), { recursive: true });
  for (const name of ['python3', 'docker', 'curl', 'npx']) {
    writeFileSync(join(bin, name), commandStub, { mode: 0o755 });
  }
  const log = join(root, 'commands.jsonl');
  const env = {
    PATH: `${bin}:${process.env.PATH}`, HOME: process.env.HOME,
    SERVER_SOURCE: join(root, 'server'), SERVER_IMAGE: `server@sha256:${'a'.repeat(64)}`,
    EXECUTOR_IMAGE: `executor@sha256:${'b'.repeat(64)}`,
    CONSOLE_BUNDLE: join(root, 'console.zip'), CONSOLE_BUNDLE_SHA256: 'c'.repeat(64),
    COMMAND_LOG: log, ...overrides,
  };
  try {
    const result = spawnSync('bash', [runner], { cwd: root, env, encoding: 'utf8' });
    const commands = existsSync(log)
      ? readFileSync(log, 'utf8').trim().split('\n').map(line => JSON.parse(line)) : [];
    return { ...result, commands };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test('shipping replay assigns unique projects and preserves default ports', () => {
  const first = replay();
  const second = replay();
  assert.equal(first.status, 0, first.stderr);
  assert.equal(second.status, 0, second.stderr);
  const project = first.commands[0].env.COMPOSE_PROJECT_NAME;
  assert.match(project, /^shipping-live-[a-z0-9_-]+$/);
  assert.notEqual(second.commands[0].env.COMPOSE_PROJECT_NAME, project);
  const init = first.commands.find(entry => entry.args[0] === 'quickstart.py');
  assert.match(init.settings, /HONUA_HTTP_PORT=18080\n/);
  assert.match(init.settings, /HONUA_CONSOLE_PORT=18083\n/);
  assert.ok(first.commands.every(entry => entry.env.COMPOSE_PROJECT_NAME === project));
  assert.equal(existsSync(init.cwd), false);
});

test('shipping replay uses custom ports for initialization, readiness, browser and cleanup', () => {
  const result = replay({
    HONUA_SHIPPING_PORT: '28083', COMPOSE_PROJECT_NAME: 'verification-742',
    COMPOSE_FILE: '/another/stack.yml', COMPOSE_PROFILES: 'production', COMPOSE_PATH_SEPARATOR: ';',
    HONUA_HTTP_PORT: '18080', HONUA_CONSOLE_PORT: '18083',
    HONUA_CONSOLE_EDGE_SUBNET: '172.31.250.248/29',
    HONUA_CONSOLE_EDGE_ADDRESS: '172.31.250.250',
    HONUA_CONSOLE_EDGE_SERVER_ADDRESS: '172.31.250.251',
  });
  assert.equal(result.status, 0, result.stderr);
  const init = result.commands.find(entry => entry.args[0] === 'quickstart.py');
  assert.equal(init.settings, 'COMPOSE_PROJECT_NAME=verification-742\n' +
    'HONUA_HTTP_PORT=28080\nHONUA_GRPC_PORT=28081\nHONUA_MANAGER_PORT=28082\nHONUA_CONSOLE_PORT=28083\n');
  for (const entry of result.commands) {
    assert.equal(entry.env.COMPOSE_PROJECT_NAME, 'verification-742');
    assert.equal(entry.env.HONUA_HTTP_PORT, '28080');
    assert.equal(entry.env.HONUA_CONSOLE_PORT, '28083');
    assert.equal(entry.env.HONUA_CONSOLE_EDGE_SUBNET, '172.31.250.248/29');
    assert.equal(entry.env.HONUA_CONSOLE_EDGE_ADDRESS, '172.31.250.250');
    assert.equal(entry.env.HONUA_CONSOLE_EDGE_SERVER_ADDRESS, '172.31.250.251');
    assert.equal(entry.env.COMPOSE_FILE, undefined);
    assert.equal(entry.env.COMPOSE_PROFILES, undefined);
    assert.equal(entry.env.COMPOSE_PATH_SEPARATOR, undefined);
  }
  const curls = result.commands.filter(entry => entry.command === 'curl');
  assert.equal(curls.length, 2);
  assert.ok(curls.every(entry => entry.args.includes('https://localhost:28083/healthz/ready')));
  const browser = result.commands.find(entry => entry.command === 'npx');
  assert.equal(browser.env.HONUA_SHIPPING_BASE_URL, 'https://localhost:28083');
  assert.ok(result.commands.some(entry => entry.command === 'docker' && entry.args.includes('down')));
  assert.equal(existsSync(init.cwd), false);
});

test('browser failure still removes only the selected project', () => {
  const result = replay({ FAIL_COMMAND: 'npx', COMPOSE_PROJECT_NAME: 'verification-failure' });
  assert.equal(result.status, 19);
  const teardown = result.commands.find(entry => entry.args.includes('down'));
  assert.equal(teardown.env.COMPOSE_PROJECT_NAME, 'verification-failure');
  assert.equal(existsSync(teardown.cwd), false);
});

for (const port of ['1026', '65536', '028083', '28083;exit', 'https://localhost']) {
  test(`invalid shipping port ${port} fails before any external command`, () => {
    const result = replay({ HONUA_SHIPPING_PORT: port });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /HONUA_SHIPPING_PORT/);
    assert.deepEqual(result.commands, []);
  });
}

test('invalid Compose project fails before packaging or cleanup', () => {
  const result = replay({ COMPOSE_PROJECT_NAME: '../existing-stack' });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /COMPOSE_PROJECT_NAME/);
  assert.deepEqual(result.commands, []);
});

test('shipping replay adds the disposable override to quickstart files between init and start', () => {
  const result = replay();
  assert.equal(result.status, 0, result.stderr);
  const python = result.commands.filter(entry => entry.command === 'python3');
  const order = python.map(entry => entry.args.join(' '));
  const init = order.indexOf('quickstart.py --profile console --init-only');
  const update = order.indexOf('-');
  const start = order.indexOf('quickstart.py --profile console');
  assert.ok(init >= 0 && init < update && update < start, order.join(' | '));
  assert.match(python[init].override,
    /AllowedEnvironmentVariables__0: ConnectionStrings__DefaultConnection/);
  assert.match(python[update].script,
    /'COMPOSE_FILE': values\['COMPOSE_FILE'\] \+ separator \+ 'compose\.override\.yaml'/);
});
