import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const MANIFEST_URL = 'https://raw.githubusercontent.com/honua-io/honua-release/trunk/platform-manifest.yaml';

// The manifest's honua-server component block: the 4-space `image:` and `digest:` keys belong to the
// server itself; deeper keys (the Compose executor) do not.
export function releasePinnedServerImage(manifest) {
  const lines = manifest.split('\n');
  const start = lines.findIndex(line => line === '  honua-server:');
  if (start < 0) throw new Error('platform-manifest.yaml has no components.honua-server entry');
  const end = lines.findIndex((line, index) => index > start && /^ {2}\S/.test(line));
  const block = lines.slice(start + 1, end < 0 ? undefined : end);
  const value = key => block
    .map(line => line.match(new RegExp(`^ {4}${key}: "([^"]+)"`)))
    .find(Boolean)?.[1];
  const image = value('image');
  const digest = value('digest');
  if (!image || !digest) throw new Error('components.honua-server needs an image and a digest');
  return `${image}@${digest}`;
}

async function main() {
  if (process.env.HONUA_CONSOLE_E2E_SERVER_IMAGE) {
    console.log(`Using the requested server image ${process.env.HONUA_CONSOLE_E2E_SERVER_IMAGE}`);
    return;
  }
  const response = await fetch(MANIFEST_URL);
  if (!response.ok) throw new Error(`Could not read ${MANIFEST_URL}: HTTP ${response.status}`);
  const image = releasePinnedServerImage(await response.text());
  appendFileSync(process.env.GITHUB_ENV, `HONUA_CONSOLE_E2E_SERVER_IMAGE=${image}\n`);
  console.log(`Using the release-pinned server image ${image}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
