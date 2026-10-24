import { expect, test } from '@playwright/test';

// Actual vendored Studio + Console embed/conversation modules. HTTP state and the
// terminal native execution receiver are fixtures; this is not a live-model proof.
test('inspector identities reach the visible conversation and frozen native turn after preparation', async ({ page }) => {
    test.setTimeout(60_000);
    const task = { id: 'design-draft', revision: 3, kind: 'studio-composition', packageKey: 'design-package' };
    let context: any = { workspaceId: 'design-workspace', revision: 1, targetId: 'fixture-target', selectedResources: [], task };
    const session = {
        environmentId: 'design-environment', environmentName: 'Design fixture', principalId: 'design-human',
        csrfToken: 'fixture-csrf', expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        releaseId: 'fixture-release', sourcePathBase: '', nativeAgentAvailable: false,
    };
    const layers = ['a', 'b'].map(id => ({ id: `places-${id}`, sourceId: `service-${id}/0`, title: 'Places', visible: true }));
    const body = {
        mapPackageId: 'design-package', format: 'honua_map_package.v1', layers,
        view: { bbox: [-158, 21, -157, 22] },
        widgets: ['a', 'b'].map(id => ({ id: `table-${id}`, kind: 'table', title: 'Places', sourceId: `service-${id}/0`, config: { fields: ['name', 'category'] } })),
        controls: ['a', 'b'].map(id => ({ id: `category-${id}`, kind: 'filterSelect', title: 'Places', sourceId: `service-${id}/0`, config: { field: 'category', options: ['A', 'B'] } })),
        interactions: ['a', 'b'].map(id => ({
            id: `category-${id}-map`, on: { ref: `control:category-${id}`, event: 'change' },
            do: { ref: `layer:places-${id}`, verb: 'setFilter', args: { field: 'category', value: '$event.value' } },
        })),
    };
    const draft = { draftId: task.id, itemId: 'design-item', packageKey: task.packageKey, generation: task.revision,
        family: 'map', envelope: { family: 'map', schemaVersion: '1.0', format: body.format, body } };
    const turns: any[] = [];
    const preparationsAtCapture: any[] = [];
    let draftReads = 0;
    let releaseTurn!: () => void;
    const heldTurn = new Promise<void>(resolve => { releaseTurn = resolve; });
    const json = (value: unknown) => ({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
    const api = (value: unknown) => json({ success: true, data: value });

    await page.addInitScript(() => sessionStorage.clear());
    await page.route('**/api/agent-workspace/**', async route => {
        const request = route.request(), path = new URL(request.url()).pathname;
        if (path.endsWith('/session')) return route.fulfill(json(session));
        if (path.endsWith('/deploy-targets')) return route.fulfill(json([]));
        if (path.endsWith('/conversation')) return route.fulfill(json({ messages: turns.map(turn => ({ role: 'user', content: turn.prompt })) }));
        if (path.endsWith('/context')) {
            const update = request.postDataJSON();
            context = { ...context, ...update, revision: context.revision + 1 };
            return route.fulfill(json(context));
        }
        if (path.endsWith('/turns')) {
            const submitted = request.postDataJSON();
            preparationsAtCapture.push(await page.evaluate(() => (window as any).designFocusEvidence?.prepared.at(-1)));
            const turn = { ...submitted, context: structuredClone(context), status: 'accepted' };
            turns.push(turn);
            if (turns.length === 3) await heldTurn;
            return route.fulfill(json(turn));
        }
        if (path.endsWith('/design-workspace')) return route.fulfill(json(context));
        return route.fulfill({ status: 404, body: 'No fixture endpoint' });
    });
    await page.route('**/api/studio/**', async route => {
        const request = route.request(), path = new URL(request.url()).pathname;
        if (path.endsWith(`/package-drafts/${task.id}`)) { draftReads++; return route.fulfill(api(draft)); }
        if (path.endsWith('/catalog-mcp')) {
            const rpc = request.postDataJSON();
            let result: unknown;
            if (rpc.method === 'initialize') result = { protocolVersion: '2025-03-26', capabilities: {}, serverInfo: { name: 'design-fixture', version: '1' } };
            else if (rpc.method === 'notifications/initialized') return route.fulfill({ status: 202, body: '' });
            else if (rpc.params?.name === 'honua_list_layers') result = { structuredContent: {
                layerCount: 2, offset: 0, hasMore: false, nextOffset: null,
                layers: ['a', 'b'].map(id => ({ serviceId: `service-${id}`, serviceName: `Fixture${id.toUpperCase()}`, layerId: 0, name: 'Places', geometryType: 'Polygon' })),
            } };
            else if (rpc.params?.name === 'honua_describe_layer') {
                const id = rpc.params.arguments.serviceId === 'service-b' ? 'b' : 'a';
                result = { structuredContent: { serviceId: `service-${id}`, serviceName: `Fixture${id.toUpperCase()}`, layerId: 0,
                    geometryType: 'Polygon', srid: 4326, fieldCount: 2, fields: [{ name: 'name', type: 'String' }, { name: 'category', type: 'String' }] } };
            } else result = { structuredContent: { styles: [] } };
            return route.fulfill(json({ jsonrpc: '2.0', id: rpc.id, result }));
        }
        if (path.includes('/data/rest/services/')) return route.fulfill(json({
            type: 'FeatureCollection', numberMatched: 1,
            features: [{ type: 'Feature', id: 1, properties: { name: 'Private fixture record', category: 'A' },
                geometry: { type: 'Polygon', coordinates: [[[-157.9, 21.1], [-157.7, 21.1], [-157.7, 21.3], [-157.9, 21.3], [-157.9, 21.1]]] } }],
        }));
        return route.fulfill({ status: 404, body: 'No fixture endpoint' });
    });

    try {
        await page.goto('/studio');
        const conversation = page.locator('[data-agent-workspace]');
        await expect(conversation).toHaveAttribute('data-mounted', 'true');
        await conversation.evaluate((host: HTMLDetailsElement) => { host.open = true; });
        await conversation.locator('.console-conversation-connection > summary').click();
        await conversation.locator('[data-workspace-id]').fill('design-workspace');
        await conversation.locator('[data-workspace-connect]').click();
        await expect(conversation.locator('[data-workspace-status]')).toHaveText('Connected');
        await page.evaluate(async () => {
            // Missing-binding host mode intentionally omits only this outlet.
            // studio-embed.mjs owns app creation, cookie binding and all focus hooks.
            if (!document.querySelector('[data-studio-embed]')) {
                const host = document.createElement('section');
                host.dataset.studioEmbed = '';
                host.innerHTML = '<p data-studio-status role="status"></p><div data-studio-mount></div>';
                document.querySelector('main')!.append(host);
            }
            await import('/js/studio-embed.mjs');
        });
        await expect(page.locator('[data-studio-status]')).toHaveText('Connected to Design fixture');
        await expect(page.getByTestId('recipe-toggle')).toBeVisible();
        await expect(page.getByTestId('studio-chat-input')).toBeHidden();
        await expect.poll(() => page.locator('honua-studio-app').evaluate((app: any) => app.workspaceDraftIdentity)).toEqual({ draftId: task.id, generation: 3, packageKey: task.packageKey });

        await page.evaluate(() => {
            const app = document.querySelector('honua-studio-app') as any;
            const workspace = (window as any).honuaAgentWorkspace;
            const evidence = (window as any).designFocusEvidence = { prepared: [], native: [] };
            const prepare = app.prepareWorkspaceTurn.bind(app);
            app.prepareWorkspaceTurn = async () => {
                const identity = await prepare(); evidence.prepared.push(structuredClone(identity)); return identity;
            };
            // The receiver is the terminal fixture boundary: Console's capture,
            // shared-context PUT and immutable-turn POST are the real modules.
            workspace.agentSession = { attachDraft() {}, async submitTurn(turn: any) {
                evidence.native.push({ turn: structuredClone(turn), prepared: structuredClone(evidence.prepared.at(-1)) });
                return { status: 'complete' };
            } };
        });
        await page.getByTestId('design-inspector-toggle').click();
        const inspector = page.getByRole('region', { name: 'Data and interaction inspector' });
        await expect(inspector).toContainText('Described fields:');
        const label = conversation.locator('[data-workspace-selection-label]');
        const select = (ref: string) => inspector.locator(`button[data-ref="${ref}"]:not([data-relationship])`).first();
        const send = async (prompt: string, count: number) => {
            await conversation.locator('[data-workspace-prompt]').fill(prompt);
            await conversation.getByRole('button', { name: 'Send', exact: true }).click();
            await expect.poll(() => turns.length).toBe(count);
        };
        const focusOf = (turn: any) => JSON.parse(turn.prompt.split('Selected Studio authoring context (identifiers only; use the authorized draft):\n')[1]);

        await select('widget:table-b').focus(); await page.keyboard.press('Enter');
        await expect(label).toContainText('component table-b · source reference service-b/0');
        await send('Refine this table.', 1);
        await expect(conversation.locator('[data-workspace-status]')).toContainText('Turn saved');
        expect(focusOf(turns[0]).targets).toEqual([{ kind: 'component', id: 'table-b', sourceReference: 'service-b/0' }]);
        await select('control:category-b').click();
        await expect(label).toContainText('control category-b · source reference service-b/0');
        await send('Refine this filter.', 2);
        await expect(conversation.locator('[data-workspace-status]')).toContainText('Turn saved');
        expect(focusOf(turns[1]).targets).toEqual([{ kind: 'control', id: 'category-b', sourceReference: 'service-b/0' }]);

        await inspector.locator('[data-connection="category-b-map"][data-connection-origin="authored"] button[data-ref="layer:places-b"]').click();
        await expect(label).toContainText('layer places-b · source service-b/0 · interaction category-b-map');
        const beforePoll = draftReads;
        await expect.poll(() => draftReads).toBeGreaterThan(beforePoll);
        await expect(inspector).toBeVisible();
        await expect(label).toContainText('interaction category-b-map');
        await send('Refine this relationship.', 3);
        // Once accepted, later visual focus must not retarget the submitted turn.
        await select('widget:table-a').click();
        releaseTurn();
        await expect.poll(() => page.evaluate(() => (window as any).designFocusEvidence.native.length)).toBe(3);
        const frozen = focusOf(turns[2]);
        expect(frozen).toEqual({ draftId: task.id, revision: 3,
            targets: [{ kind: 'layer', id: 'places-b', sourceId: 'service-b/0' }],
            relationship: { interactionId: 'category-b-map', ownerRef: 'layer:places-b' } });
        const delivered = await page.evaluate(() => (window as any).designFocusEvidence.native);
        expect(delivered[2].turn.prompt).toBe(turns[2].prompt);
        for (const receipt of delivered) {
            expect(receipt.prepared).toEqual({ draftId: task.id, generation: 3, packageKey: task.packageKey });
            expect(receipt.turn.context.task).toEqual(task);
            expect(receipt.turn.prompt).not.toContain('Private fixture record');
        }
        for (const preparation of preparationsAtCapture) expect(preparation).toEqual({ draftId: task.id, generation: 3, packageKey: task.packageKey });

        await expect(label).toContainText('component table-a');
        await conversation.locator('[data-workspace-clear-selection]').click();
        await expect(label).toContainText('No object selected');
        expect(await page.locator('honua-studio-app').evaluate((app: any) => app.composition.selection)).toEqual([]);
        await send('Describe the current map.', 4);
        await expect.poll(() => page.evaluate(() => (window as any).designFocusEvidence.native.length)).toBe(4);
        expect(turns[3].prompt).toBe('Describe the current map.');
        expect(await page.locator('[data-workspace-prompt]:visible').count()).toBe(1);
        await page.screenshot({ path: 'test-results/studio-design-focus.png', fullPage: true });
    } finally { releaseTurn(); }
});
