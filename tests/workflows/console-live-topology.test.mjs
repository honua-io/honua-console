import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const read = path => readFileSync(new URL(`../../${path}`, import.meta.url), 'utf8');

test('the complete ordinary journey keeps Studio and agent coverage in their dedicated topology', () => {
  const workflow = read('.github/workflows/console-live-journey.yml');
  const ordinary = workflow.split('      - name: Exercise the complete authenticated Console live suite\n')[1]
    .split('      - name: Retain server diagnostics\n')[0];
  assert.match(ordinary, /--project=chromium(?:\s|$)/);
  assert.doesNotMatch(ordinary, /--grep|--grep-invert/);
  assert.match(workflow, /inputs\.suite == 'studio-customer' \|\| inputs\.suite == 'full'/);
  assert.match(workflow, /npm run e2e:live -- --project=studio-customer --reporter=list,json/);
  assert.match(workflow, /run-quickstart\.py/);
  assert.doesNotMatch(workflow, /continue-on-error:/);
});

test('each self-publishing live spec owns a distinct seeded source table', () => {
  const source = read('e2e/playwright/live/source-db.ts');
  const tables = new Map([...source.matchAll(/(\w+): process\.env\.\w+ \?\? '(public\.[\w]+)'/g)]
    .map(([, name, table]) => [name, table]));
  const published = new Set();
  const seeds = read('e2e/initdb/01-seed.sql') + read('e2e/initdb/03-dedicated-sources.sql');
  for (const spec of ['services-layers', 'data-flow-go-live', 'service-settings', 'service-types', 'folder-management']) {
    const script = read(`e2e/playwright/live/specs/${spec}.live.spec.ts`);
    const field = script.match(/SOURCE_DB\.(\w*[Tt]able)/)?.[1];
    const table = tables.get(field);
    assert.ok(table, `${spec} must select a declared source table`);
    assert.ok(!published.has(table), `${spec} shares the already published source ${table}`);
    assert.ok(seeds.includes(`CREATE TABLE IF NOT EXISTS ${table} (`), `${table} must be seeded`);
    assert.ok(seeds.includes(`INSERT INTO ${table} `), `${table} must contain the query fixture`);
    published.add(table);
  }
});
