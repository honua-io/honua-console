import { test, expect } from '../admin-api';

// Live content coverage for the publishing WIZARD at /operate/publishing.
//
// The live lane already proves the quick-publish FORM at /operate/publishing/quick end to end
// (services-layers.live.spec.ts). The wizard one route up had no content assertion at all — only a
// heading check (nav-no-ai) and a rollback-dialog check on a different panel (trust-feedback). It
// was therefore free to render a hardcoded service tree, a hardcoded source table and a review
// screen quoting an invented field count, layer slot and URL, while POSTing to a connection id
// that existed in no deployment. Every existing assertion passed throughout.
//
// A route that reads from the server needs at least one assertion that what it shows CAME from the
// server. These compare the wizard's own rendering against the admin API's answer for the same
// question, so a fixture cannot satisfy them.

const stamp = Date.now().toString(36);
const SOURCE_TABLE = 'public.e2e_layer_src';

test.describe('Operate · Publishing wizard (live)', () => {
  test('the service tree lists services the admin API reports', async ({ page, admin }) => {
    test.setTimeout(120_000);

    const services = await admin.getJson('/rest/services?f=json');
    const serviceNames: string[] = (services.services ?? [])
      .map((s: any) => String(s.name ?? '').split('/').pop())
      .filter(Boolean);

    await page.goto('/operate/publishing');

    const rows = page.locator('[data-publish-tree-row]');
    const emptyState = page.locator('[data-publish-tree-empty]');

    if (serviceNames.length === 0) {
      // No services published yet: the wizard must say so rather than offer rows. This branch is an
      // assertion, not a skip — a surface that renders nothing real must still prove it renders the
      // honest empty state.
      await expect(emptyState).toBeVisible();
      await expect(rows).toHaveCount(0);
      return;
    }

    await expect(rows.first()).toBeVisible({ timeout: 30_000 });

    // Every row the wizard offers must be a service the server actually exposes.
    const renderedNames = await rows.evaluateAll((nodes) =>
      nodes.map((node) => node.getAttribute('data-publish-tree-row') ?? ''),
    );
    expect(renderedNames.length).toBeGreaterThan(0);
    for (const rendered of renderedNames) {
      expect(serviceNames, `${rendered} is rendered by the wizard but not exposed by the server`)
        .toContain(rendered);
    }
  });

  test('the table picker lists tables the connection actually exposes', async ({ page, admin }) => {
    test.setTimeout(300_000);

    const connName = `e2e-wizard-conn-${stamp}`;
    const conn = await admin.createConnection({
      name: connName,
      host: 'localhost',
      port: 5544,
      databaseName: 'honua_dev',
      username: 'honua_user',
      password: 'honua_password',
      provider: 'postgis',
      sslRequired: false,
      sslMode: 'Disable',
    });
    admin.trackConnectionName(connName);

    // Warm server-side discovery before driving the UI (a cold scan on a fresh connection can lag).
    await expect
      .poll(
        async () => {
          const t = await admin.getJson(`/api/v1/admin/connections/${conn.connectionId}/tables`);
          return (t.tables ?? []).some((x: any) => `${x.schema}.${x.table}` === SOURCE_TABLE);
        },
        { timeout: 90_000, intervals: [1000, 2000, 5000] },
      )
      .toBeTruthy();

    await page.goto('/operate/publishing');

    // Step 1 -> Layer. Any service (or a new one) satisfies the step; the table picker is the subject.
    const firstService = page.locator('[data-publish-tree-row]').first();
    if ((await firstService.count()) > 0) {
      await firstService.click();
    } else {
      await page.locator('button.publish-segment-option', { hasText: 'Create new service' }).click();
      await page.locator('[data-new-service-name]').fill(`e2e-wizard-svc-${stamp}`);
    }
    await page.locator('button.publish-wizard-next').click();

    await page.locator('[data-connection-picker]').selectOption(conn.connectionId);

    const tablePicker = page.locator('[data-table-picker]');
    await expect(tablePicker).toBeEnabled({ timeout: 60_000 });
    await expect(tablePicker.locator(`option[value="${SOURCE_TABLE}"]`)).toHaveCount(1);

    // The rail describes the SELECTED table using the server's own metadata for it.
    await tablePicker.selectOption(SOURCE_TABLE);
    const tables = await admin.getJson(`/api/v1/admin/connections/${conn.connectionId}/tables`);
    const source = (tables.tables ?? []).find(
      (x: any) => `${x.schema}.${x.table}` === SOURCE_TABLE,
    );
    expect(source, `${SOURCE_TABLE} must be discoverable`).toBeTruthy();

    const rail = page.locator('.publish-wizard-rail');
    await expect(rail).toContainText(SOURCE_TABLE);
    if (source.srid) {
      await expect(rail).toContainText(String(source.srid));
    }
    if (source.geometryColumn) {
      await expect(rail).toContainText(String(source.geometryColumn));
    }
  });

  test('the review step never claims a layer slot or URL before the server assigns one', async ({
    page,
  }) => {
    await page.goto('/operate/publishing');

    // Whatever the deployment holds, the wizard must not print a published route or slot id up
    // front — those were the fixtures that made an unpublished review look like a live publication.
    const markup = await page.content();
    expect(markup).not.toContain('honua.example.gov');
    expect(markup).not.toContain('prod-postgis');
    expect(markup).not.toContain('parcels_2024');
  });
});
