import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { NATIVE_PROBE_OVERLAY, liveServerProbeOverlay } from '../select-live-server-probe.mjs';

const read = path => readFileSync(new URL(`../../${path}`, import.meta.url), 'utf8');

test('the chiseled rc.2 image gets the native readiness probe overlay', () => {
  const chiseled = { Test: ['CMD', '/app/honua-healthcheck', '--ready'], Interval: 30000000000 };
  assert.equal(liveServerProbeOverlay(chiseled), NATIVE_PROBE_OVERLAY);
});

test('an image with a Bash probe, or no declared probe, keeps the default compose probe', () => {
  const bash = { Test: ['CMD', 'bash', '-c', 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf ...'] };
  assert.equal(liveServerProbeOverlay(bash), '');
  assert.equal(liveServerProbeOverlay(null), '');
  assert.equal(liveServerProbeOverlay({}), '');
});

test('the overlay replaces only the server readiness command with the native probe', () => {
  const overlay = read(NATIVE_PROBE_OVERLAY);
  assert.match(overlay, /honua-server:\n\s+healthcheck:\n\s+test: \["CMD", "\/app\/honua-healthcheck", "--ready"\]/);
});

test('every live-journey compose command applies the selected probe overlay last', () => {
  const workflow = read('.github/workflows/console-live-journey.yml');
  const commands = workflow.split('\n').filter(line => line.includes('docker compose -f e2e/docker-compose.yml'));
  assert.ok(commands.length >= 7);
  for (const command of commands) {
    assert.match(command, /docker-compose\.(auth|studio-customer)\.yml \$\{HONUA_CONSOLE_E2E_PROBE_OVERLAY:\+-f "\$HONUA_CONSOLE_E2E_PROBE_OVERLAY"\} /,
      command.trim());
  }
  assert.equal(workflow.match(/node scripts\/select-live-server-probe\.mjs/g)?.length, 2);
});

test('each live-journey job selects the probe before it first starts compose', () => {
  const workflow = read('.github/workflows/console-live-journey.yml');
  const jobs = workflow.split('\njobs:\n')[1].split(/\n  [\w-]+:\n/)
    .filter(job => job.includes('docker compose -f e2e/docker-compose.yml'));
  assert.equal(jobs.length, 2);
  for (const job of jobs) {
    const select = job.indexOf('node scripts/select-live-server-probe.mjs');
    assert.ok(select >= 0 && select < job.indexOf('docker compose -f e2e/docker-compose.yml'));
  }
});
