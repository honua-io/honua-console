import { randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { expect, test, request } from '@playwright/test';
import { sourceConnectionBody } from '../source-db';
import { accessibility, createRecipe, customerIdentity, identity, observeHealth, paintedFeatures, read, serverUrl, signIn, sourceFor, type Draft } from '../studio-customer';

// No seeded cookies, API-key customer mutations, route mocks, skips or UI-only success criteria.
// A fresh compose database and fresh Keycloak realm are required for each invocation.
const nonce = () => randomUUID().replaceAll('-', '').slice(0, 16);
const fixture = (name: string) => fileURLToPath(new URL(`../fixtures/${name}`, import.meta.url));

test('fresh install publisher imports, authors, saves and reopens a map', async ({ page }, info) => {
  const completed: string[] = [];
  const health = observeHealth(page);
  try {
    const customer = await customerIdentity();
    const session = await test.step('real OIDC sign-in as the install publisher', async () => {
      const session = await signIn(page);
      const lifetime = Date.parse(session.expiresAt) - Date.now();
      expect(lifetime, 'the real session must have a positive bearer lifetime').toBeGreaterThan(0);
      expect(lifetime, 'the testbed must issue a one-minute bearer').toBeLessThanOrEqual(65_000);
      completed.push('publisher-auth');
      return session;
    });
    const services: Array<{ service: string; layerId: number; title: string }> = [];
    // Provision the private source connection as test infrastructure only. Neither customer import nor
    // publication uses this verifier's key, and the key is absent from the Console container.
    const verifier = await request.newContext({ baseURL: serverUrl,
      extraHTTPHeaders: { 'X-API-Key': process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key' } });
    try {
      const connectionResponse = await verifier.post('/api/v1/admin/connections/', { data: sourceConnectionBody(`customer-${nonce()}`) });
      expect(connectionResponse.status()).toBe(201);
      const { data: connection } = await connectionResponse.json();
      for (const file of ['customer-regions.geojson', 'customer-sites.csv']) {
        await test.step(`customer imports and publishes ${file}, verified through both protocols`, async () => {
          const table = `customer_${nonce()}`, service = `customer-${nonce()}`, title = `Customer ${table}`;
          await page.goto('/operate/resources/import');
          const fileInput = page.locator('input[type="file"]');
          await expect.poll(() => fileInput.evaluate(element => '_blazorInputFileNextFileId' in element)).toBe(true);
          await fileInput.setInputFiles(fixture(file));
          await page.getByPlaceholder('parcels', { exact: true }).fill(table);
          await page.getByRole('button', { name: 'Import file', exact: true }).click();
          const importOutcome = page.getByRole('heading', { name: /^(File imported|Import failed)$/ });
          await expect(importOutcome).toBeVisible({ timeout: 90_000 });
          await expect(importOutcome, 'Import failed is a product failure, never an acceptable outcome').toHaveText('File imported');
          await expect(page.locator('[data-imported-table]')).toHaveText(table);
          const tablesResponse = await verifier.get(`/api/v1/admin/connections/${connection.connectionId}/tables`);
          expect(tablesResponse.ok()).toBe(true);
          const importedTables = (await tablesResponse.json()).tables.filter((entry: any) => entry.table === table || entry.table === `imported_${table}`);
          expect(importedTables, 'one physical imported table must exist').toHaveLength(1);
          const importedTable = importedTables[0];
          await page.goto('/operate/publishing/quick');
          await page.getByLabel('Connection', { exact: true }).selectOption(connection.connectionId);
          await expect(page.getByLabel('Table', { exact: true })).toBeEnabled({ timeout: 60_000 });
          await page.getByLabel('Table', { exact: true }).selectOption(`${importedTable.schema}.${importedTable.table}`);
          await page.getByPlaceholder('parcels-fs', { exact: true }).fill(service);
          await page.getByPlaceholder('Parcels', { exact: true }).fill(title);
          await page.getByRole('button', { name: 'Publish layer', exact: true }).click();
          await expect(page.getByRole('heading', { name: 'Layer published', exact: true })).toBeVisible();
          const layerId = Number(await page.locator('[data-published-layer-id]').textContent());
          expect(Number.isSafeInteger(layerId)).toBe(true);
          const prefix = `/rest/services/${service}/FeatureServer/${layerId}`;
          const metadataResponse = await verifier.get(`${prefix}?f=json`);
          expect(metadataResponse.ok()).toBe(true);
          const metadata = await metadataResponse.json();
          expect(metadata.fields).toEqual(expect.arrayContaining([
            expect.objectContaining({ name: 'name', type: 'esriFieldTypeString' }),
            expect.objectContaining({ name: 'category', type: 'esriFieldTypeString' }),
            expect.objectContaining({ name: 'population', type: expect.stringMatching(/Integer|SmallInteger|BigInteger/) }),
            expect.objectContaining({ name: 'rating', type: expect.stringMatching(/Double|Single/) }),
          ]));
          const query = await verifier.get(`${prefix}/query?where=1%3D1&outFields=*&f=json`);
          expect(query.ok()).toBe(true);
          expect((await query.json()).features.map((entry: any) => entry.attributes.rating).sort()).toEqual([1.5, 2.5]);
          const collections = await verifier.get('/ogc/features/collections?f=json');
          expect(collections.ok()).toBe(true);
          const matches = (await collections.json()).collections.filter((entry: any) => entry.title === title);
          expect(matches, 'OGC must identify this publication, not another service sharing its layer number').toHaveLength(1);
          const collection = matches[0];
          const itemsResponse = await verifier.get(`/ogc/features/collections/${encodeURIComponent(collection.id)}/items?f=json&limit=10`);
          expect(itemsResponse.ok()).toBe(true);
          const items = await itemsResponse.json();
          expect(items.features).toHaveLength(2);
          for (const feature of items.features) {
            expect(typeof feature.properties.name).toBe('string');
            expect(typeof feature.properties.population).toBe('number');
            expect(Number.isInteger(feature.properties.population)).toBe(true);
            expect(typeof feature.properties.rating).toBe('number');
            expect(feature.geometry.type).toBe(file.endsWith('.csv') ? 'Point' : 'Polygon');
          }
          expect(items.features.map((feature: any) => feature.properties.category).sort()).toEqual(['east', 'west']);
          expect(items.features.map((feature: any) => feature.properties.population).sort((a: number, b: number) => a - b)).toEqual([10, 20]);
          expect(items.features.map((feature: any) => feature.properties.rating).sort()).toEqual([1.5, 2.5]);
          services.push({ service, layerId, title });
          completed.push(`import-publish-${file}`);
        });
      }
    } finally { await verifier.dispose(); }

    await page.goto('/studio');
    await expect(page.locator('[data-studio-status]')).toContainText('Connected to');
    const conversation = page.locator('[data-agent-workspace]');
    if (!(await conversation.evaluate(element => (element as HTMLDetailsElement).open)))
      await conversation.locator(':scope > summary').click();
    await conversation.getByRole('button', { name: 'Start conversation', exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect(conversation).toHaveAttribute('data-connected', 'true');
    const packageKey = `customer-map-${nonce()}`;
    const source = await sourceFor(page, services[0].service, services[0].layerId);
    await createRecipe(page, source!.id, packageKey);
    await accessibility(page, info, 'recipe-map');
    const original = (await identity(page))!;
    const draftPath = (id: string) => `/api/studio/v1/studio/package-drafts/${encodeURIComponent(id)}`;
    const readDraft = (id: string): Promise<Draft> => read(page, session, draftPath(id));
    const first = await readDraft(original.draftId);
    expect(first.ownerId).toBeTruthy();
    const ownerSubject = first.ownerId.startsWith('subject:')
      ? decodeURIComponent(first.ownerId.split(':')[2].split('@tenant:')[0]) : first.ownerId;
    expect(ownerSubject, 'the server owner must be the actual IdP customer subject').toBe(customer.id);
    expect(first.createdBy).toBe(first.ownerId);
    expect(first.envelope.body.layers).toHaveLength(1);
    await expect.poll(() => paintedFeatures(page, first.envelope.body.layers[0].id), { timeout: 45_000 }).toBeGreaterThan(0);
    await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(2);
    await page.getByTestId('studio-control-filter-select').selectOption('west');
    await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(1);
    await expect(page.getByTestId('studio-widget-grid-row')).toContainText('West');
    await page.getByTestId('studio-control-filter-select').selectOption('east');
    await expect(page.getByTestId('studio-widget-grid-row')).toContainText('East');
    await page.getByTestId('studio-control-filter-select').selectOption('');
    await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(2);
    await page.getByRole('button', { name: `Add ${services[1].title} to map`, exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect.poll(async () => (await readDraft(original.draftId)).envelope.body.layers.length).toBeGreaterThanOrEqual(2);
    const composed = await readDraft(original.draftId);
    for (const layer of composed.envelope.body.layers)
      await expect.poll(() => paintedFeatures(page, layer.id), { timeout: 45_000 }).toBeGreaterThan(0);
    completed.push('two-layers-filter-widget');
    await test.step('real bearer expiry and automatic refresh preserve Studio', async () => {
      const initialExpiry = Date.parse(session.expiresAt);
      expect(initialExpiry - Date.now(), 'the testbed must issue a short-lived bearer').toBeLessThanOrEqual(65_000);
      await expect.poll(() => Date.now(), { timeout: 75_000, intervals: [1000] }).toBeGreaterThan(initialExpiry + 1000);
      const refreshed = await read(page, session, '/api/agent-workspace/session');
      expect(refreshed.authenticated).toBe(true);
      expect(Date.parse(refreshed.expiresAt)).toBeGreaterThan(initialExpiry);
      expect(refreshed.principalId).toBe(session.principalId);
      expect(refreshed.environmentId).toBe(session.environmentId);
      Object.assign(session, refreshed);
      expect((await identity(page))?.draftId).toBe(original.draftId);
      await page.getByTestId('studio-control-filter-select').selectOption('west');
      await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(1);
      await page.getByTestId('studio-control-filter-select').selectOption('');
      await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(2);
      await info.attach('session-refresh.json', { contentType: 'application/json',
        body: Buffer.from(JSON.stringify({ initialExpiry, refreshedExpiry: Date.parse(refreshed.expiresAt), samePrincipal: true })) });
      completed.push('session-expiry-recovery');
    });

    await page.getByRole('button', { name: 'Edit map style', exact: true }).click();
    const editor = page.getByRole('region', { name: 'Map style editor', exact: true });
    await expect(editor.locator('iframe')).toBeVisible();
    await editor.getByRole('combobox', { name: 'Actual source field', exact: true }).selectOption('category');
    await editor.getByRole('combobox', { name: 'Thematic method', exact: true }).selectOption('categories');
    await editor.getByRole('textbox', { name: 'Explicit thematic values, one per line', exact: true }).fill('west\neast');
    await editor.getByRole('button', { name: 'Preview thematic colors', exact: true }).click();
    await editor.getByRole('button', { name: 'Save map style', exact: true }).click();
    await expect(editor.getByRole('status')).toHaveText('Map style saved to the authorized draft.');
    const authored = await readDraft(original.draftId);
    expect(authored.ownerId).toBe(first.ownerId);
    const style = authored.envelope.body.mapSpec;
    const fill = style.layers.find((layer: any) => layer.type === 'fill');
    expect(fill.paint['fill-color']).toEqual(['match', ['get', 'category'], 'west', '#eff3ff', 'east', '#c6dbef', '#9ecae1']);
    // Use Maputnik's shipped full-style editor for the attribute-label layer. This is
    // a real paste/blur/save through the sandboxed UI, preserving all authorized sources.
    const frame = editor.frameLocator('iframe');
    const withLabels = structuredClone(style);
    withLabels.layers.push({ id: 'customer-attribute-labels', type: 'symbol', source: fill.source,
      ...(fill['source-layer'] ? { 'source-layer': fill['source-layer'] } : {}),
      layout: { 'text-field': ['get', 'name'], 'text-size': 14 }, paint: { 'text-color': '#222222' } });
    await frame.locator('[data-wd-key="nav:code-editor"]').click();
    const code = frame.locator('.maputnik-code-editor .cm-content[contenteditable="true"]');
    await code.fill(JSON.stringify(withLabels, null, 2));
    await code.press('Tab');
    await frame.getByRole('button', { name: 'Close', exact: true }).click();
    await editor.getByRole('button', { name: 'Save map style', exact: true }).click();
    await page.getByRole('button', { name: 'Return to map', exact: true }).click();
    const labelled = await readDraft(original.draftId);
    expect(labelled.envelope.body.mapSpec.layers).toEqual(expect.arrayContaining([
      expect.objectContaining({ type: 'symbol', layout: expect.objectContaining({ 'text-field': ['get', 'name'] }) }),
    ]));
    completed.push('authored-style-labels');
    await page.getByRole('button', { name: 'Save version', exact: true }).click();
    await expect(page.locator('[data-studio-status]')).toHaveText('Saved version 1.');
    const versions = await read(page, session, `/api/studio/v1/studio/content-items/${authored.itemId}/versions`);
    expect(versions.versions).toHaveLength(1);
    const version = await read(page, session, `/api/studio/v1/studio/content-items/${authored.itemId}/versions/${versions.versions[0].versionId}`);
    expect(version.ownerId).toBe(first.ownerId);
    expect(version.envelope.body).toEqual(labelled.envelope.body);
    await page.reload();
    await expect.poll(() => identity(page).then(value => value?.draftId), { timeout: 30_000 }).toBe(original.draftId);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Reopen version 1 as draft', exact: true }).click();
    await expect.poll(() => identity(page).then(value => value?.draftId)).not.toBe(original.draftId);
    const reopened = await readDraft((await identity(page))!.draftId);
    expect(reopened.itemId).toBe(authored.itemId);
    expect((await identity(page))?.packageKey).toBe(packageKey);
    expect(reopened.ownerId).toBe(first.ownerId);
    expect(reopened.envelope.body).toEqual(labelled.envelope.body);
    for (const layer of reopened.envelope.body.layers)
      await expect.poll(() => paintedFeatures(page, layer.id), { timeout: 45_000 }).toBeGreaterThan(0);
    completed.push('save-reload-reopen');
    await test.step('independent server rendering matches the saved authored colours', async () => {
      const witness = await request.newContext({ baseURL: serverUrl,
        extraHTTPHeaders: { 'X-API-Key': process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key' } });
      try {
        // A service-default export cannot witness a map-local style. Render the
        // saved expression through the independent MapServer dynamic-layer path.
        const savedFill = reopened.envelope.body.mapSpec.layers.find((layer: any) => layer.type === 'fill');
        const expression = savedFill.paint['fill-color'];
        expect(expression).toEqual(['match', ['get', 'category'], 'west', '#eff3ff', 'east', '#c6dbef', '#9ecae1']);
        const opacity = savedFill.paint['fill-opacity'] ?? 1;
        expect(typeof opacity).toBe('number');
        const symbol = (hex: string) => ({ type: 'esriSFS', style: 'esriSFSSolid',
          color: [1, 3, 5].map(offset => parseInt(hex.slice(offset, offset + 2), 16)).concat(Math.round(opacity * 255)),
          outline: { type: 'esriSLS', style: 'esriSLSSolid', color: [0, 0, 0, 0], width: 0 } });
        const rules = [2, 4].map(index => ({ value: expression[index], symbol: symbol(expression[index + 1]) }));
        const render = async (name: string, negative?: 'swapped' | 'empty') => {
          const uniqueValueInfos = negative === 'swapped'
            ? rules.map((rule, index) => ({ ...rule, symbol: rules[1 - index].symbol })) : rules;
          const dynamicLayers = [{ id: 0, source: { type: 'mapLayer', mapLayerId: services[0].layerId },
            ...(negative === 'empty' ? { definitionExpression: '1=0' } : {}),
            drawingInfo: { renderer: { type: 'uniqueValue', field1: expression[1][1],
              defaultSymbol: symbol(expression[6]), uniqueValueInfos } } }];
          const params = new URLSearchParams({ bbox: '-2.4,-1.2,2.4,1.2', bboxSR: '4326', imageSR: '4326',
            size: '320,160', format: 'png32', transparent: 'true', f: 'image', dynamicLayers: JSON.stringify(dynamicLayers) });
          const image = await witness.get(`/rest/services/${services[0].service}/MapServer/export?${params}`);
          expect(image.status()).toBe(200);
          expect(image.headers()['content-type']).toContain('image/png');
          const png = await image.body();
          await info.attach(`independent-server-render-${name}.png`, { contentType: 'image/png', body: png });
          return page.evaluate(async base64 => {
            const image = new Image(); image.src = `data:image/png;base64,${base64}`;
            await image.decode();
            const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
            const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0);
            return [67, 253, 160].map(x => Array.from(context.getImageData(x, 80, 1, 1).data));
          }, png.toString('base64'));
        };
        const pixels = await render('authored');
        const distance = (pixel: number[], expected: number[]) => Math.sqrt(expected.reduce((sum, value, index) => sum + (pixel[index] - value) ** 2, 0));
        expect(distance(pixels[0], [239, 243, 255]), 'west must use the customer-authored colour').toBeLessThan(10);
        expect(distance(pixels[1], [198, 219, 239]), 'east must use a distinct customer-authored colour').toBeLessThan(10);
        expect(Math.abs(pixels[0][3] - Math.round(opacity * 255))).toBeLessThanOrEqual(1);
        expect(distance(pixels[0], [198, 219, 239]), 'negative control: swapped classes must fail').toBeGreaterThan(30);
        expect(pixels[2][3], 'gap between polygons remains transparent').toBe(0);
        const swapped = await render('swapped-negative', 'swapped');
        expect(distance(swapped[0], [239, 243, 255])).toBeGreaterThan(30);
        expect(distance(swapped[0], [198, 219, 239])).toBeLessThan(10);
        const empty = await render('empty-negative', 'empty');
        expect(empty.map(pixel => pixel[3]), 'no features means no authored pixels').toEqual([0, 0, 0]);
        completed.push('independent-server-render');
      } finally { await witness.dispose(); }
    });
    await test.step('customer publication and signed-out audience', async () => {
      await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
      await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
      const publication = page.getByRole('region', { name: 'Map publication', exact: true });
      const route = `/customer-${nonce()}`;
      await publication.getByRole('textbox', { name: 'Publication route' }).fill(route);
      await publication.getByRole('combobox', { name: 'Publication audience' }).selectOption('public');
      await publication.getByRole('button', { name: 'Publish reviewed version' }).click();
      await expect(publication).toContainText('Awaiting separate human approval.', { timeout: 30_000 });
      const proposalId = /Awaiting separate human approval\. Proposal ([A-Za-z0-9_-]{1,128})\./
        .exec(await publication.textContent() ?? '')?.[1];
      expect(proposalId, 'publication must return a durable proposal identity').toBeTruthy();
      const proposalPath = `/api/processes/admin/proposals/${proposalId}`;
      const beforeSelfApproval = await read(page, session, proposalPath);
      expect(beforeSelfApproval.requestedBy).toBe(first.ownerId);
      // Use the customer's own real session to attempt approval. A later read
      // verifies the proposal was not decided, before the independent approver acts.
      const requester = await page.context().newPage();
      const requesterHealth = observeHealth(requester);
      try {
        await requester.goto(`/inbox?proposalId=${proposalId}`);
        const ownProposal = requester.locator(`[data-proposal-id="${proposalId}"]:has([data-proposal-status])`).last();
        await ownProposal.locator('[data-proposal-approve]').click();
        await expect(ownProposal.locator('[data-proposal-rbac-denied]')).toBeVisible();
        const afterSelfApproval = await read(page, session, proposalPath);
        expect(afterSelfApproval.status).toBe(beforeSelfApproval.status);
        expect(afterSelfApproval.resolvedBy).toBeFalsy();
      } finally { await requesterHealth.verify(info); await requester.close(); }
      const consoleOrigin = new URL(page.url()).origin;
      const approverContext = await page.context().browser()!.newContext({
        baseURL: consoleOrigin, ignoreHTTPSErrors: true,
        // Same explicit trusted-edge fixture as the existing two-operator live auth proof.
        // This is a distinct host partition and a separate real IdP login, never a shared cookie.
        extraHTTPHeaders: { 'X-Honua-Edge-Auth': 'live-proof-edge-secret',
          'X-Forwarded-User': 'bob-operator', 'X-Forwarded-Email': 'bob@live-proof.honua.test' },
      });
      try {
        const approver = await approverContext.newPage();
        const approverHealth = observeHealth(approver);
        try {
          const approverSession = await signIn(approver, 'bob', `/inbox?proposalId=${proposalId}`);
          expect(approverSession.principalId).not.toBe(session.principalId);
          const pending = await read(approver, approverSession, proposalPath);
          expect(pending.requestedBy).toBe(first.ownerId);
          const detail = approver.locator(`[data-proposal-id="${proposalId}"]:has([data-proposal-status])`).last();
          await detail.locator('[data-proposal-approve]').click();
          await expect(detail.locator('[data-proposal-action-message]')).toContainText('Approved');
          await expect.poll(async () => (await read(approver, approverSession, proposalPath)).status?.toLowerCase(),
            { timeout: 90_000 }).toBe('succeeded');
          const resolved = await read(approver, approverSession, proposalPath);
          expect(resolved.resolvedBy).toBeTruthy();
          expect(resolved.resolvedBy).not.toBe(resolved.requestedBy);
          completed.push('separate-approval');
        } finally { await approverHealth.verify(info); }
      } finally { await approverContext.close(); }
      await expect.poll(async () => {
        await publication.getByRole('button', { name: 'Refresh publication status' }).click();
        return publication.getByRole('link', { name: 'Open published map', exact: true }).isVisible();
      }, { timeout: 90_000 }).toBe(true);
      const href = await publication.getByRole('link', { name: 'Open published map', exact: true }).getAttribute('href');
      expect(href).toBeTruthy();
      const anonymous = await page.context().browser()!.newContext();
      try {
        const viewer = await anonymous.newPage();
        const viewerHealth = observeHealth(viewer);
        try {
          const response = await viewer.goto(new URL(href!, page.url()).href);
          expect(response?.status()).toBe(200);
          await expect(viewer.locator('[data-published-map]')).toBeVisible();
          await expect(viewer.locator('[data-map-status]')).toHaveText('Map ready.');
          await expect.poll(async () => Number(await viewer.locator('[data-map-canvas]').getAttribute('data-rendered-feature-count'))).toBeGreaterThan(0);
          const definition = await anonymous.request.get(new URL(`/api/v1/studio/published${route}`, serverUrl).href);
          expect(definition.status()).toBe(200);
          const document = await definition.json();
          expect(JSON.stringify(document)).toContain(packageKey);
          expect(JSON.stringify(document)).toContain('#eff3ff');
          const privateDraft = await anonymous.request.get(new URL(draftPath(reopened.draftId), page.url()).href, { maxRedirects: 0 });
          expect([401, 403]).toContain(privateDraft.status());
          await accessibility(viewer, info, 'public-map');
          completed.push('public-audience');
        } finally { await viewerHealth.verify(info); }
      } finally { await anonymous.close(); }
    });
    await accessibility(page, info, 'reopened-map');
    completed.push('accessibility');
    expect(health.successfulReads.length, 'widgets must read actual features through the host transport').toBeGreaterThan(0);
  } finally {
    await info.attach('customer-journey-progress.json', { contentType: 'application/json',
      body: Buffer.from(JSON.stringify({ completed, qualificationComplete: ['publisher-auth', 'import-publish-customer-regions.geojson', 'import-publish-customer-sites.csv', 'two-layers-filter-widget', 'session-expiry-recovery', 'authored-style-labels', 'save-reload-reopen', 'independent-server-render', 'separate-approval', 'public-audience', 'accessibility'].every(stage => completed.includes(stage)) }, null, 2)) });
    if (health) await health.verify(info);
  }
});

test('widget transport control: real recipe table and filter use the host session', async ({ page }, info) => {
  // An independent operator control isolates widget transport from customer import permissions.
  // It does not count towards the customer qualification above.
  const session = await signIn(page, 'alice');
  const health = observeHealth(page);
  const verifier = await request.newContext({ baseURL: serverUrl,
    extraHTTPHeaders: { 'X-API-Key': process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key' } });
  try {
    // This is an idempotently provisioned transport fixture, never import evidence.
    const catalogResponse = await verifier.get('/rest/services?f=json');
    expect(catalogResponse.status()).toBe(200);
    const existing = (await catalogResponse.json()).services.find((item: any) => item.name.startsWith('widget-'));
    let service: string, layerId: number;
    if (existing) {
      service = existing.name;
      const metadata = await verifier.get(`/rest/services/${service}/FeatureServer?f=json`);
      expect(metadata.status()).toBe(200);
      const layers = (await metadata.json()).layers;
      expect(layers).toHaveLength(1);
      layerId = layers[0].id;
    } else {
      service = `widget-${nonce()}`;
      const connection = await verifier.post('/api/v1/admin/connections/', { data: sourceConnectionBody(`widget-${nonce()}`) });
      expect(connection.status()).toBe(201);
      const connectionId = (await connection.json()).data.connectionId;
      const publication = await verifier.post(`/api/v1/admin/connections/${connectionId}/layers/`, { data: {
        schema: 'public', table: 'e2e_customer_regions', layerName: 'Widget regions', serviceName: service, enabled: true,
        geometryColumn: 'geom', primaryKey: 'id', srid: 4326,
      } });
      expect(publication.status()).toBe(201);
      layerId = (await publication.json()).data.layerId;
    }
    const features = await verifier.get(`/rest/services/${service}/FeatureServer/${layerId}/query?where=1%3D1&outFields=*&f=json`);
    expect(features.status()).toBe(200);
    expect((await features.json()).features).toHaveLength(2);
    await page.reload();
    await expect(page.locator('[data-studio-status]')).toContainText('Connected to');
    const source = await sourceFor(page, service, layerId);
    await createRecipe(page, source!.id, `widget-map-${nonce()}`);
    await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(2);
    await page.getByTestId('studio-control-filter-select').selectOption('west');
    await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(1);
    await expect(page.getByTestId('studio-widget-grid-row')).toContainText('West');
    expect(health.successfulReads.some(path => path.includes(`/FeatureServer/${layerId}/query`))).toBe(true);
    const original = (await identity(page))!;
    const draftPath = `/api/studio/v1/studio/package-drafts/${original.draftId}`;
    const beforeRefresh = await read(page, session, draftPath);
    const originalExpiry = Date.parse(session.expiresAt);
    await expect.poll(() => Date.now(), { timeout: 75_000, intervals: [1000] }).toBeGreaterThan(originalExpiry + 1000);
    const renewed = await read(page, session, '/api/agent-workspace/session');
    expect(Date.parse(renewed.expiresAt)).toBeGreaterThan(originalExpiry);
    expect(renewed.principalId).toBe(session.principalId);
    await info.attach('bearer-renewal-observation.json', { contentType: 'application/json',
      body: Buffer.from(JSON.stringify({ originalExpiry, renewedExpiry: Date.parse(renewed.expiresAt),
        observedAt: Date.now(), samePrincipal: true })) });
    Object.assign(session, renewed);
    await page.getByTestId('studio-control-filter-select').selectOption('east');
    await expect(page.getByTestId('studio-widget-grid-row')).toHaveCount(1);
    await expect(page.getByTestId('studio-widget-grid-row')).toContainText('East');
    expect((await identity(page))?.draftId).toBe(original.draftId);
    const afterRefresh = await read(page, session, draftPath);
    expect(afterRefresh.ownerId).toBe(beforeRefresh.ownerId);
    expect(afterRefresh.itemId).toBe(beforeRefresh.itemId);
    expect(afterRefresh.envelope.body.layers.map((layer: any) => [layer.id, layer.sourceId]))
      .toEqual(beforeRefresh.envelope.body.layers.map((layer: any) => [layer.id, layer.sourceId]));
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await expect(page.locator('[data-studio-status]')).toHaveText('This map has no saved versions yet.');
    const versionsAfterRefresh = await read(page, session, `/api/studio/v1/studio/content-items/${afterRefresh.itemId}/versions`);
    expect(versionsAfterRefresh.versions).toEqual([]);
    await info.attach('widget-transport-outcome.json', { contentType: 'application/json',
      body: Buffer.from(JSON.stringify({ service, layerId, transportPassed: true, fullRows: 2, westRows: 1,
        eastRowsAfterRefresh: 1, originalExpiry, renewedExpiry: Date.parse(renewed.expiresAt), sameDraftAfterRefresh: true })) });
  } finally {
    await verifier.dispose();
    await health.verify(info);
  }
});

test('Studio and its recipe panel meet axe and keyboard entry requirements', async ({ page }, info) => {
  await signIn(page);
  await expect(page.locator('honua-studio-app')).toBeVisible();
  await accessibility(page, info, 'studio-workspace');
  await page.getByTestId('recipe-toggle').focus();
  await expect(page.getByTestId('recipe-toggle')).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('recipe-panel')).toBeVisible();
  await page.getByTestId('recipe-package-key').focus();
  await page.keyboard.type(`keyboard-${nonce()}`);
  await page.keyboard.press('Tab');
  await expect(page.getByTestId('recipe-source')).toBeFocused();
  await accessibility(page, info, 'studio-recipe');
  await page.getByTestId('recipe-close').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('recipe-panel')).toBeHidden();
  await expect(page.getByTestId('recipe-toggle')).toBeFocused();
});

test('New map keyboard creation persists a real draft without browser errors', async ({ page }, info) => {
  const session = await signIn(page, 'alice');
  const health = observeHealth(page);
  try {
    await expect(page.locator('[data-studio-status]')).toContainText('Connected to');
    const key = `keyboard-map-${nonce()}`;
    await page.getByRole('textbox', { name: 'New map package key', exact: true }).focus();
    await page.keyboard.type(key);
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'New map', exact: true })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect.poll(() => identity(page).then(value => value?.packageKey)).toBe(key);
    const draft = await read(page, session, `/api/studio/v1/studio/package-drafts/${(await identity(page))!.draftId}`);
    expect(draft.packageKey).toBe(key);
    expect(draft.ownerId).toBeTruthy();
    expect(draft.envelope.body.layers).toEqual([]);
  } finally { await health.verify(info); }
});


test('import typing control: published GeoJSON retains scalar attribute fields', async ({}, info) => {
  // Separate from the user journey so its earliest permission refusal cannot hide import corruption.
  const verifier = await request.newContext({ baseURL: serverUrl,
    extraHTTPHeaders: { 'X-API-Key': process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key' } });
  try {
    const table = `typing_${nonce()}`, service = `typing-${nonce()}`;
    const importedResponse = await verifier.post('/api/v1/admin/import/upload', { multipart: {
      file: { name: 'customer-regions.geojson', mimeType: 'application/geo+json', buffer: await readFile(fixture('customer-regions.geojson')) },
      TableName: table,
    } });
    expect(importedResponse.status()).toBe(200);
    const imported = await importedResponse.json();
    expect(imported.success).toBe(true);
    const connectionResponse = await verifier.post('/api/v1/admin/connections/', { data: sourceConnectionBody(`typing-${nonce()}`) });
    expect(connectionResponse.status()).toBe(201);
    const connectionId = (await connectionResponse.json()).data.connectionId;
    const tablesResponse = await verifier.get(`/api/v1/admin/connections/${connectionId}/tables`);
    expect(tablesResponse.status()).toBe(200);
    const tables = (await tablesResponse.json()).tables.filter((entry: any) => entry.table === table || entry.table === `imported_${table}`);
    expect(tables).toHaveLength(1);
    const physical = tables[0];
    const publication = await verifier.post(`/api/v1/admin/connections/${connectionId}/layers/`, { data: {
      schema: physical.schema, table: physical.table, layerName: 'Typed imported regions', serviceName: service, enabled: true,
    } });
    expect(publication.status()).toBe(201);
    const layerId = (await publication.json()).data.layerId;
    const metadataResponse = await verifier.get(`/rest/services/${service}/FeatureServer/${layerId}?f=json`);
    expect(metadataResponse.status()).toBe(200);
    const metadata = await metadataResponse.json();
    await info.attach('imported-field-types.json', { contentType: 'application/json',
      body: Buffer.from(JSON.stringify({ service, layerId, fields: metadata.fields, geometryType: metadata.geometryType }, null, 2)) });
    expect(metadata.fields, 'an imported JSONB container must not replace the typed input attributes').toEqual(expect.arrayContaining([
      expect.objectContaining({ name: 'name', type: 'esriFieldTypeString' }),
      expect.objectContaining({ name: 'category', type: 'esriFieldTypeString' }),
      expect.objectContaining({ name: 'population', type: expect.stringMatching(/Integer|SmallInteger|BigInteger/) }),
      expect.objectContaining({ name: 'rating', type: expect.stringMatching(/Double|Single/) }),
    ]));
    const query = await verifier.get(`/rest/services/${service}/FeatureServer/${layerId}/query?where=1%3D1&outFields=*&f=json`);
    expect(query.status()).toBe(200);
    expect((await query.json()).features.map((feature: any) => feature.attributes.rating).sort()).toEqual([1.5, 2.5]);
  } finally { await verifier.dispose(); }
});
