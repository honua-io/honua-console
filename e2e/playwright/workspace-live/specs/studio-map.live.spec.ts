import { createHash, randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';

type DraftIdentity = { draftId: string; generation: number; packageKey: string };
type CatalogSource = {
  id: string;
  locator?: { url?: string; layerId?: number };
};
type StudioApp = HTMLElement & {
  workspaceDraftIdentity?: DraftIdentity;
  sourceCatalog?: CatalogSource[];
};
type WorkspaceWindow = Window & { honuaAgentWorkspace?: { workspaceId?: string } };
type RenderLayer = { id: string; type: string; paint?: Record<string, unknown> };
type Draft = {
  draftId: string;
  itemId: string;
  packageKey: string;
  envelope: { body: {
    layers?: Array<{ id: string; sourceId?: string }>;
    mapSpec?: { layers: RenderLayer[] };
    [key: string]: unknown;
  } };
};

async function identity(page: Page) {
  return page.locator('honua-studio-app').evaluate(element =>
    (element as StudioApp).workspaceDraftIdentity ?? null);
}

async function workspaceId(page: Page) {
  return page.evaluate(() => (window as WorkspaceWindow).honuaAgentWorkspace?.workspaceId ?? null);
}

// Mutations are real UI actions. Browser evaluation below only observes runtime
// state; it never injects a draft, fake session, tool result, or composition.
test('installed workspace: visual and prompted styling persist on the same rendered map', async ({ page }, info) => {
  const service = process.env.HONUA_WORKSPACE_E2E_SERVICE?.trim();
  const layerText = process.env.HONUA_WORKSPACE_E2E_LAYER;
  expect(service, 'Select a service on the dedicated evaluation deployment.').toBeTruthy();
  expect(layerText, 'Select its numeric layer id.').toMatch(/^\d+$/);
  const layerId = Number(layerText);
  expect(Number.isSafeInteger(layerId)).toBe(true);
  const servicePath = service!.split('/').map(encodeURIComponent).join('/');
  const featurePath = `/rest/services/${servicePath}/FeatureServer/${layerId}`;
  const packageKey = `workspace-live-${randomUUID()}`;

  const sessionResponse = await page.request.get('/api/agent-workspace/session');
  expect(sessionResponse.status(), 'Use a real paired/signed-in operator session.').toBe(200);
  const session = await sessionResponse.json();
  expect(session.authenticated).toBe(true);
  expect(session.nativeAgentAvailable, 'This journey requires the separately configured native agent.').toBe(true);
  expect(typeof session.environmentId).toBe('string');
  expect(typeof session.principalId).toBe('string');
  const headers = {
    'X-Honua-Environment': session.environmentId as string,
    'X-Honua-Session-Partition': session.principalId as string,
  };
  const read = async (path: string) => {
    const response = await page.request.get(path, { headers });
    expect(response.status(), `Authorized read failed for ${path.split('?')[0]}`).toBe(200);
    const body = await response.json();
    return body.data ?? body;
  };
  const readDraft = (id: string): Promise<Draft> =>
    read(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(id)}`);

  // Verify the selected data exists before asking the model to use it. Extent is
  // requested in WGS84 so the prompt and the observed map use the same bounds.
  const count = await read(`/api/studio/data${featurePath}/query?where=1%3D1&returnCountOnly=true&f=json`);
  expect(count.count).toBeGreaterThan(0);
  const extentResult = await read(`/api/studio/data${featurePath}/query?where=1%3D1&returnExtentOnly=true&outSR=4326&f=json`);
  const extent = extentResult.extent;
  expect(extent?.spatialReference?.wkid).toBe(4326);
  expect([extent.xmin, extent.ymin, extent.xmax, extent.ymax].every(Number.isFinite)).toBe(true);
  const paddingX = Math.max(extent.xmax - extent.xmin, 0.01) * 0.1;
  const paddingY = Math.max(extent.ymax - extent.ymin, 0.01) * 0.1;
  const bbox = [Math.max(-180, extent.xmin - paddingX), Math.max(-90, extent.ymin - paddingY),
    Math.min(180, extent.xmax + paddingX), Math.min(90, extent.ymax + paddingY)];

  await page.goto('/studio');
  await expect(page.locator('[data-studio-embed]')).toBeVisible();
  await expect(page.locator('[data-studio-status]')).toContainText('Connected to');
  await expect(page.locator('honua-studio-app')).toHaveCount(1);
  await expect(page.locator('[data-legacy-studio-retired]')).toHaveCount(0);
  const conversation = page.locator('[data-agent-workspace]');
  if (!(await conversation.evaluate(element => (element as HTMLDetailsElement).open)))
    await conversation.locator(':scope > summary').click();
  await conversation.getByRole('button', { name: 'Start conversation', exact: true }).click();
  await expect(conversation).toHaveAttribute('data-connected', 'true');
  const originalWorkspace = await workspaceId(page);
  expect(originalWorkspace).toBeTruthy();
  await expect(page.locator('textarea:visible')).toHaveCount(1);

  await page.getByRole('textbox', { name: 'New map package key' }).fill(packageKey);
  await page.getByRole('button', { name: 'New map', exact: true }).click();
  await expect.poll(() => identity(page).then(value => value?.packageKey)).toBe(packageKey);
  const originalDraft = (await identity(page))!;
  await expect(conversation.locator('[data-workspace-target]')).toContainText(packageKey);
  const sourceSuffix = `/rest/services/${servicePath}/FeatureServer`;
  const sources = () => page.locator('honua-studio-app').evaluate((element, expected) =>
    ((element as StudioApp).sourceCatalog ?? []).filter(source =>
      source.locator?.layerId === expected.layerId && source.locator?.url?.endsWith(expected.suffix)),
    { layerId, suffix: sourceSuffix });
  await expect.poll(() => sources().then(value => value.length)).toBe(1);
  const source = (await sources())[0];

  const prompt = `In the current map draft ${packageKey}, add the published source ${source.id} ` +
    `from service ${service}, layer ${layerId}. Set the map view to WGS84 bounds ${JSON.stringify(bbox)}. ` +
    'Use the existing source and current draft. Do not create or publish a service, publish the map, or edit source data.';
  await conversation.getByRole('textbox', { name: 'Message', exact: true }).fill(prompt);
  await conversation.getByRole('button', { name: 'Send', exact: true }).click();
  await expect(conversation.locator('[data-workspace-status]')).toHaveText(
    'Turn saved. Check operation receipts for completion.', { timeout: 180_000 });

  // Assistant prose is not evidence of a successful write. Read the canonical
  // draft independently and inspect features painted by the actual MapLibre map.
  await expect.poll(async () => (await readDraft(originalDraft.draftId)).envelope.body.layers
    ?.some(layer => layer.sourceId === source.id), { timeout: 30_000 }).toBe(true);
  const savedBody = (await readDraft(originalDraft.draftId)).envelope.body;
  const layerIds = savedBody.layers!.filter(layer => layer.sourceId === source.id).map(layer => layer.id);
  const paintedCount = () => page.locator('honua-studio-canvas').evaluate((element, ids) => {
    const canvas = element as HTMLElement & {
      mapView?: { map?: { getLayer(id: string): unknown; queryRenderedFeatures(options: { layers: string[] }): unknown[] } };
    };
    const map = canvas.mapView?.map;
    if (!map || !ids.every(id => map.getLayer(id))) return 0;
    return map.queryRenderedFeatures({ layers: ids }).length;
  }, layerIds);
  await expect.poll(paintedCount, { timeout: 45_000 }).toBeGreaterThan(0);

  // Choose two values from an actual authorized field, rather than supplying
  // invented thematic classes or writing a style through browser evaluation.
  const metadata = await read(`/api/studio/data${featurePath}?f=json`);
  const sample = await read(`/api/studio/data${featurePath}/query?where=1%3D1&outFields=*&returnGeometry=false&resultRecordCount=20&f=json`);
  const candidates = (metadata.fields as Array<{ name: string }>).map(field => ({
    name: field.name,
    values: [...new Set<string | number>((sample.features as Array<{ attributes: Record<string, unknown> }>)
      .map(feature => feature.attributes[field.name])
      .filter((value): value is string | number => typeof value === 'string' ||
        (typeof value === 'number' && Number.isFinite(value))))],
  }));
  const thematic = candidates.find(candidate => candidate.values.length >= 2);
  expect(thematic, 'The qualification layer needs two observed values in an authorized string or numeric field.').toBeTruthy();
  const layerIdForStyle = layerIds[0];
  await page.getByRole('button', { name: 'Edit map style', exact: true }).click();
  const editor = page.getByRole('region', { name: 'Map style editor', exact: true });
  await expect(editor).toBeVisible();
  await expect(editor.locator('iframe[title="Maputnik visual map style editor"]')).toBeVisible();
  await expect(editor.frameLocator('iframe[title="Maputnik visual map style editor"]')
    .locator('.maputnik-layout')).toBeVisible();
  await editor.getByRole('combobox', { name: 'Map layer for styling', exact: true }).selectOption(layerIdForStyle);
  const field = editor.getByRole('combobox', { name: 'Actual source field', exact: true });
  await expect(field.locator('option')).toContainText([thematic!.name]);
  await field.selectOption(thematic!.name);
  await editor.getByRole('combobox', { name: 'Thematic method', exact: true }).selectOption('categories');
  const values = editor.getByRole('textbox', { name: 'Explicit thematic values as JSON array', exact: true });
  const renderLayer = async () => (await readDraft(originalDraft.draftId)).envelope.body.mapSpec?.layers
    .find(layer => layer.id === layerIdForStyle);
  const initialRenderLayer = (await renderLayer())!;
  expect(['fill', 'line', 'circle', 'symbol']).toContain(initialRenderLayer.type);
  const paintPrefix = initialRenderLayer.type === 'symbol' ? 'text' : initialRenderLayer.type;
  const colorProperty = `${paintPrefix}-color`, opacityProperty = `${paintPrefix}-opacity`;
  const colorExpression = (value: string | number) => ['match', ['get', thematic!.name], value, '#eff3ff', '#c6dbef'];
  const livePaint = (property: string) => page.locator('honua-studio-canvas').evaluate((element, args) => {
    const canvas = element as HTMLElement & {
      mapView?: { map?: { getPaintProperty(id: string, property: string): unknown } };
    };
    return canvas.mapView?.map?.getPaintProperty(args.layerId, args.property);
  }, { layerId: layerIdForStyle, property });

  await values.fill(JSON.stringify([thematic!.values[0]]));
  await editor.getByRole('button', { name: 'Preview thematic colors', exact: true }).click();
  await expect.poll(() => livePaint(colorProperty)).toEqual(colorExpression(thematic!.values[0]));
  await editor.getByRole('button', { name: 'Save map style', exact: true }).click();
  await expect.poll(async () => (await renderLayer())?.paint?.[colorProperty])
    .toEqual(colorExpression(thematic!.values[0]));
  await expect(editor.getByRole('status')).toHaveText('Map style saved to the authorized draft.');

  const stylePrompt = `In current map ${packageKey}, change only render layer ${layerIdForStyle} ` +
    `paint property ${opacityProperty} to 0.55. Preserve its existing ${colorProperty} thematic expression ` +
    `over field ${thematic!.name}, all sources and all other layers. Save to this same draft; do not publish.`;
  await conversation.getByRole('textbox', { name: 'Message', exact: true }).fill(stylePrompt);
  await conversation.getByRole('button', { name: 'Send', exact: true }).click();
  await expect(conversation.getByRole('log')).toContainText(stylePrompt, { timeout: 180_000 });
  await expect(conversation.locator('[data-workspace-status]')).toHaveText(
    'Turn saved. Check operation receipts for completion.', { timeout: 180_000 });
  await expect.poll(async () => (await renderLayer())?.paint?.[opacityProperty]).toBe(0.55);
  expect((await renderLayer())?.paint?.[colorProperty]).toEqual(colorExpression(thematic!.values[0]));
  await expect.poll(() => livePaint(opacityProperty)).toBe(0.55);

  // A subsequent visual edit must start from the agent's current saved style.
  // Save flushes the real sandboxed Maputnik frame and persists through Studio.
  await values.fill(JSON.stringify([thematic!.values[1]]));
  await editor.getByRole('button', { name: 'Preview thematic colors', exact: true }).click();
  await editor.getByRole('button', { name: 'Save map style', exact: true }).click();
  await expect.poll(async () => (await renderLayer())?.paint?.[colorProperty])
    .toEqual(colorExpression(thematic!.values[1]));
  expect((await renderLayer())?.paint?.[opacityProperty]).toBe(0.55);
  await expect.poll(() => livePaint(colorProperty)).toEqual(colorExpression(thematic!.values[1]));
  await expect.poll(() => livePaint(opacityProperty)).toBe(0.55);
  await page.getByRole('button', { name: 'Return to map', exact: true }).click();
  await expect(editor).toHaveCount(0);
  await expect.poll(paintedCount, { timeout: 45_000 }).toBeGreaterThan(0);

  await page.getByRole('button', { name: 'Save version', exact: true }).click();
  await expect(page.locator('[data-studio-status]')).toHaveText('Saved version 1.');
  const persisted = await readDraft(originalDraft.draftId);
  const versions = await read(`/api/studio/v1/studio/content-items/${encodeURIComponent(persisted.itemId)}/versions`);
  expect(versions.versions).toHaveLength(1);
  expect(versions.versions[0].versionNumber).toBe(1);

  // Real Console navigation must preserve the same conversation and draft.
  await page.locator('a[href="/operate"]').first().click();
  await expect(page).toHaveURL(/\/operate$/);
  expect(await workspaceId(page)).toBe(originalWorkspace);
  await page.locator('a[href="/studio"]').first().click();
  await expect(page).toHaveURL(/\/studio$/);
  await expect.poll(() => identity(page).then(value => value?.draftId)).toBe(originalDraft.draftId);
  expect(await workspaceId(page)).toBe(originalWorkspace);
  await expect(conversation.getByRole('log')).toContainText(prompt);

  await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
  await page.getByRole('button', { name: 'Reopen version 1 as draft', exact: true }).click();
  await expect.poll(() => identity(page).then(value => Boolean(value?.draftId &&
    value.draftId !== originalDraft.draftId))).toBe(true);
  const reopenedIdentity = (await identity(page))!;
  expect(reopenedIdentity.packageKey).toBe(packageKey);
  const reopened = await readDraft(reopenedIdentity.draftId);
  expect(reopened.itemId).toBe(persisted.itemId);
  expect(reopened.envelope.body).toEqual(persisted.envelope.body);
  await expect.poll(paintedCount, { timeout: 45_000 }).toBeGreaterThan(0);
  await expect.poll(() => livePaint(colorProperty)).toEqual(colorExpression(thematic!.values[1]));
  await expect.poll(() => livePaint(opacityProperty)).toBe(0.55);
  expect(await workspaceId(page)).toBe(originalWorkspace);

  await info.attach('workspace-map-receipt.json', {
    contentType: 'application/json',
    body: Buffer.from(JSON.stringify({
      scope: 'installed native prompt → existing data → rendered map → visual/AI/visual style → save → navigate → reopen',
      packageKey, service, layerId, sourceId: source.id, featureCount: count.count,
      workspaceId: originalWorkspace, draftId: originalDraft.draftId,
      reopenedDraftId: reopenedIdentity.draftId, itemId: persisted.itemId,
      versionId: versions.versions[0].versionId,
      styling: { scope: 'map-local', field: thematic!.name, layerId: layerIdForStyle, colorProperty, opacityProperty },
      bodySha256: createHash('sha256').update(JSON.stringify(persisted.envelope.body)).digest('hex'),
      remainingQualification: ['fresh installation and pairing', 'import', 'shared-style governance', 'publication and audience',
        'recovery', 'external MCP clients', 'immutable release artifact provenance'],
    }, null, 2)),
  });
});
