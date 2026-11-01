import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';

const observed = {};
for (const component of ['console', 'server']) {
  const requested = process.env[`HONUA_CONSOLE_E2E_${component.toUpperCase()}_IMAGE`];
  if (!requested) throw new Error(`Missing ${component} image`);
  const container = execFileSync('docker', ['compose', '-f', 'e2e/docker-compose.yml', '-f', 'e2e/docker-compose.auth.yml',
    '-f', 'e2e/docker-compose.studio-customer.yml', 'ps', '-q', component === 'console' ? 'console' : 'honua-server'], { encoding: 'utf8' }).trim();
  const runtime = JSON.parse(execFileSync('docker', ['inspect', container], { encoding: 'utf8' }))[0];
  const image = JSON.parse(execFileSync('docker', ['image', 'inspect', requested], { encoding: 'utf8' }))[0];
  if (runtime.Image !== image.Id) throw new Error(`The running ${component} is not the requested image`);
  const digest = requested.includes('@') ? requested.split('@')[1] : null;
  if (digest && !image.RepoDigests?.some(ref => ref.split('@')[1] === digest))
    throw new Error(`The ${component} digest is not present in the image receipt`);
  observed[component] = { requested, imageId: image.Id, repoDigests: image.RepoDigests,
    sourceRevision: image.Config?.Labels?.['org.opencontainers.image.revision'] ?? null };
}
const version = JSON.parse(readFileSync('artifacts/studio-customer/console-version.json', 'utf8'));
if (observed.console.sourceRevision && version.commit !== observed.console.sourceRevision)
  throw new Error('Console runtime version differs from its image source label');
if (!process.env.HONUA_CONSOLE_E2E_CONSOLE_IMAGE.includes('@') && version.commit !== process.env.GITHUB_SHA)
  throw new Error('The source-built Console must report the exact PR revision');
writeFileSync('artifacts/studio-customer/runtime-identity.json', JSON.stringify({
  schemaVersion: 'honua.studio-customer-runtime/v1', console: observed.console, server: observed.server,
  consoleVersion: version, suiteSourceRevision: process.env.GITHUB_SHA,
  runUrl: `https://github.com/${process.env.GITHUB_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}`,
}, null, 2) + '\n');
