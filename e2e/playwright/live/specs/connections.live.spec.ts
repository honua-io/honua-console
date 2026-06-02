import { test, expect } from '../admin-api';

// Live e2e for the Operate "Add Connection" flow (create + test) against a real honua-server.
// Targets the testbed databases reachable from the server host: PostGIS on localhost:5432.

// Unique per run so reruns don't collide on the unique-name constraint; cleaned up by the fixture.
const stamp = `${Date.now().toString(36)}`;

function fillConnectionForm(
  page: import('@playwright/test').Page,
  fields: { host: string; database: string; username: string; password: string },
) {
  return (async () => {
    await page.getByPlaceholder('db-prod.honua.internal').fill(fields.host);
    await page.getByPlaceholder('honua_geo').fill(fields.database);
    await page.getByPlaceholder('honua_reader').fill(fields.username);
    await page.getByPlaceholder('••••••••••••').fill(fields.password);
  })();
}

test.describe('Operate · Add Connection (live)', () => {
  test('PostGIS connection: draft test passes, create lands, detail test persists Healthy', async ({ page, admin }) => {
    const name = `e2e-postgis-${stamp}`;
    admin.trackConnectionName(name);

    await page.goto('/operate/connections/new');
    await page.getByPlaceholder('prod-postgis').fill(name);
    // Provider defaults to PostgreSQL/PostGIS (port 5432).
    await fillConnectionForm(page, {
      host: 'localhost',
      database: 'honua_dev',
      username: 'honua_user',
      password: 'honua_password',
    });

    // Draft test (POST /api/v1/admin/connections/test → PostgresConnectionDriver).
    await page.getByRole('button', { name: 'Test connection' }).click();
    await expect(page.getByText('Connection test passed')).toBeVisible();

    // Create → navigates to the new connection's detail page.
    await page.getByRole('button', { name: 'Create connection' }).click();
    await expect(page).toHaveURL(/\/operate\/connections\/[0-9a-fA-F-]{36}$/);

    // Independent server-side verification (different path than the create went through).
    const created = await admin.findConnectionByName(name);
    expect(created, 'connection should exist on the server').toBeTruthy();
    expect(created!.provider).toBe('postgis');
    expect(created!.databaseName).toBe('honua_dev');

    // Existing-connection test on the detail page persists health → badge flips to Passed.
    await page.getByRole('button', { name: 'Test connection' }).click();
    await expect(page.getByText('Connection test passed')).toBeVisible();
    await expect(page.getByText('Passed', { exact: true })).toBeVisible();

    const tested = await admin.findConnectionByName(name);
    expect(tested!.healthStatus).toBe('Healthy');
  });

  test('secret-reference connection: full connection string resolved from an env secret', async ({ page, admin }) => {
    // The server is launched with HONUA_TEST_DB_DSN holding a full PostGIS connection string for the testbed;
    // this exercises the "whole connection string as a secret" path (no host/credentials entered in the UI).
    const name = `e2e-secretref-${stamp}`;
    admin.trackConnectionName(name);

    await page.goto('/operate/connections/new');
    await page.getByPlaceholder('prod-postgis').fill(name);

    // Switch to secret-reference mode: the host/port/database/username/password fields are replaced by a
    // single reference field, and the store kind is derived from the reference prefix.
    await page.getByLabel('Credential source').selectOption('secret');
    await expect(page.getByPlaceholder('db-prod.honua.internal')).toHaveCount(0);
    await page.locator('[data-secret-reference]').fill('env:HONUA_TEST_DB_DSN');
    await expect(page.locator('[data-secret-type]')).toHaveText('env');

    // Draft test resolves the env secret (a full connection string) server-side → Healthy.
    await page.getByRole('button', { name: 'Test connection' }).click();
    await expect(page.getByText('Connection test passed')).toBeVisible();

    // Create → detail page. The server stores only the reference (external storage), never the secret.
    await page.getByRole('button', { name: 'Create connection' }).click();
    await expect(page).toHaveURL(/\/operate\/connections\/[0-9a-fA-F-]{36}$/);

    const created = await admin.findConnectionByName(name);
    expect(created, 'secret-ref connection should exist on the server').toBeTruthy();
    expect(created!.storageType).toBe('external');
  });

  test('duplicate connection name is blocked before any POST', async ({ page, admin }) => {
    const name = `e2e-dup-${stamp}`;
    // Seed an existing connection via the admin API (not the UI).
    await admin.createConnection({
      name,
      host: 'localhost',
      port: 5432,
      databaseName: 'honua_dev',
      username: 'honua_user',
      password: 'honua_password',
      provider: 'postgis',
      sslRequired: false,
      sslMode: 'Disable',
    });

    await page.goto('/operate/connections/new');
    await page.getByPlaceholder('prod-postgis').fill(name);
    await fillConnectionForm(page, {
      host: 'localhost',
      database: 'honua_dev',
      username: 'honua_user',
      password: 'honua_password',
    });
    await page.getByRole('button', { name: 'Create connection' }).click();

    // Client-side duplicate guard fires; stays on the form, shows the inline reason, no navigation.
    await expect(page).toHaveURL(/\/operate\/connections\/new$/);
    await expect(page.getByText(`A connection named '${name}' already exists.`)).toBeVisible();
  });
});
