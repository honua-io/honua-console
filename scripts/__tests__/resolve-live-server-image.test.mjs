import assert from 'node:assert/strict';
import { test } from 'node:test';
import { releasePinnedServerImage } from '../resolve-live-server-image.mjs';

const manifest = `components:
  honua-server:
    # certified source
    image: "ghcr.io/honua-io/honua-server:nightly-8628668"   # immutable per-sha tag
    digest: "sha256:${'a'.repeat(64)}"
    executor:
      image: "ghcr.io/honua-io/honua-server:nightly-compose-executor-8628668"
      digest: "sha256:${'b'.repeat(64)}"
  honua-console:
    image: "ghcr.io/honua-io/honua-console:nightly-c63b37fd1788"
    digest: "sha256:${'c'.repeat(64)}"
`;

test('the release-pinned server image is the server component, not its executor or another component', () => {
  assert.equal(
    releasePinnedServerImage(manifest),
    `ghcr.io/honua-io/honua-server:nightly-8628668@sha256:${'a'.repeat(64)}`);
});

test('a manifest without a pinned server fails instead of guessing', () => {
  assert.throws(() => releasePinnedServerImage('components:\n  honua-console:\n'), /no components.honua-server/);
  assert.throws(() => releasePinnedServerImage('components:\n  honua-server:\n    image: "x"\n'), /needs an image and a digest/);
});
