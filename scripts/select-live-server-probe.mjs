import { execFileSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export const NATIVE_PROBE_OVERLAY = 'e2e/docker-compose.native-probe.yml';

// The chiseled serving image has no shell and declares `/app/honua-healthcheck` as its
// HEALTHCHECK; older published images declare a Bash probe. Follow what the image itself declares,
// so the live journey can start both without a per-run flag.
export function liveServerProbeOverlay(healthcheck) {
  const command = Array.isArray(healthcheck?.Test) ? healthcheck.Test : [];
  return command.some(part => typeof part === 'string' && part.includes('/app/honua-healthcheck'))
    ? NATIVE_PROBE_OVERLAY
    : '';
}

function main() {
  const image = process.env.HONUA_CONSOLE_E2E_SERVER_IMAGE;
  if (!image) throw new Error('Set HONUA_CONSOLE_E2E_SERVER_IMAGE before selecting its readiness probe');
  execFileSync('docker', ['pull', '--quiet', image], { stdio: ['ignore', 'ignore', 'inherit'] });
  const declared = execFileSync('docker', ['image', 'inspect', '--format', '{{json .Config.Healthcheck}}', image],
    { encoding: 'utf8' });
  const overlay = liveServerProbeOverlay(JSON.parse(declared));
  appendFileSync(process.env.GITHUB_ENV, `HONUA_CONSOLE_E2E_PROBE_OVERLAY=${overlay}\n`);
  console.log(overlay
    ? `Server image declares the native readiness probe; applying ${overlay}`
    : 'Server image declares a shell readiness probe; keeping the default Bash probe');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
