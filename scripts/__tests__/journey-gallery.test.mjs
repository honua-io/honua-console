import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { captureMilestone, buildGallery } from '../journey-gallery.mjs';

// Keep all scratch evidence in this checkout; the test removes it even on failure.
const scratch = async action => {
  const directory = await mkdtemp(join(process.cwd(), '.journey-gallery-test-'));
  try { await action(directory); } finally { await rm(directory, { recursive: true, force: true }); }
};
const info = { project: { name: 'studio-customer' }, title: 'map authoring', retry: 0, attach: async () => {} };
test('milestones reject sign-in and credential pages before taking screenshots', async () => {
  for (const [url, passwords] of [['https://idp.example/realms/login', 0], ['https://console.example/auth/login', 0], ['https://console.example/studio', 1]]) {
    let captured = false;
    const page = { url: () => url, locator: () => ({ count: async () => passwords }), screenshot: async () => { captured = true; } };
    await assert.rejects(captureMilestone(page, info, 'sign-in'), /without credential controls/);
    assert.equal(captured, false);
  }
});
test('named screenshots and a portable gallery retain partial milestone evidence', () => scratch(async directory => {
  const page = { url: () => 'https://console.example/studio', locator: () => ({ count: async () => 0 }),
    screenshot: async ({ path, fullPage }) => { assert.equal(fullPage, true); await writeFile(path, 'png'); } };
  await captureMilestone(page, info, '01-map-created', directory);
  await writeFile(join(directory, 'not-evidence.txt'), 'ignored');
  const files = await buildGallery(directory);
  assert.deepEqual(files, ['studio-customer-map-authoring-0-01-map-created.png']);
  const html = await readFile(join(directory, 'index.html'), 'utf8');
  assert.match(html, /1 captured milestones/);
  assert.match(html, /href="studio-customer-map-authoring-0-01-map-created.png"/);
  assert.doesNotMatch(html, /not-evidence/);
}));
test('an empty gallery states that missing milestones remain unqualified', () => scratch(async directory => {
  assert.deepEqual(await buildGallery(directory), []);
  assert.match(await readFile(join(directory, 'index.html'), 'utf8'), /0 captured milestones.*Missing stages are unqualified/);
}));
