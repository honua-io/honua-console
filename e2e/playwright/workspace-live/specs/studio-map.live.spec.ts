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
type Draft = {
  draftId: string;
  itemId: string;
  packageKey: string;
  envelope: { body: { layers?: Array<{ id: string; sourceId?: string }>; [key: string]: unknown } };
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
test('installed workspace: prompt adds real data, renders it, saves and reopens the same map', async ({ page }, info) => {
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
  expect(await workspaceId(page)).toBe(originalWorkspace);

  await info.attach('workspace-map-receipt.json', {
    contentType: 'application/json',
    body: Buffer.from(JSON.stringify({
      scope: 'installed native prompt → existing data → rendered map → save → navigate → reopen',
      packageKey, service, layerId, sourceId: source.id, featureCount: count.count,
      workspaceId: originalWorkspace, draftId: originalDraft.draftId,
      reopenedDraftId: reopenedIdentity.draftId, itemId: persisted.itemId,
      versionId: versions.versions[0].versionId,
      bodySha256: createHash('sha256').update(JSON.stringify(persisted.envelope.body)).digest('hex'),
      remainingQualification: ['fresh installation and pairing', 'import', 'styling', 'publication and audience',
        'recovery', 'external MCP clients', 'immutable release artifact provenance'],
    }, null, 2)),
  });
});
