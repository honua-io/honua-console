import {createHash} from 'node:crypto';
import {expect,request,type Page} from '@playwright/test';
import {required,type setup} from './live-context';
import {assertHumanProposer} from './publication-witness.mjs';
import {publishedMapPath} from '../../../src/Honua.Console.Web/wwwroot/js/published-map-viewer.mjs';
type Fixture=Awaited<ReturnType<typeof setup>>;
type Expected={rows:string[];bars:string[];filteredRows:string[];filteredBars:string[];category:string;categoryField:string;identityField:string;body:unknown};

/** Trusted controller actor automation: normal human proposal/inbox UI under
 * two real sessions, then a credential-free audience. No approval policy/key
 * substitution, bootstrap auth, model approval, or claim of a user study. */
export async function publishDashboard(page:Page,fixture:Fixture,expected:Expected) {
    expect(process.env.HONUA_FOSS4G_ISOLATED_TARGET,'automated proposal/approval is restricted to an isolated fixture deployment').toBe('true');
    const approverState=required('HONUA_WORKSPACE_E2E_APPROVER_STATE');
    const route=required('HONUA_WORKSPACE_E2E_PUBLICATION_ROUTE');
    expect(route).toMatch(/^\/[a-z0-9][a-z0-9/_-]{0,126}$/);
    const browser=page.context().browser();expect(browser).toBeTruthy();
    const origin=new URL(page.url()).origin;
    const server=new URL(required('HONUA_WORKSPACE_E2E_SERVER_ORIGIN'));
    expect(['http:','https:']).toContain(server.protocol);expect(server.username+server.password+server.search+server.hash).toBe('');expect(server.pathname).toBe('/');
    const draft=await fixture.read(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(fixture.draft.draftId)}`);
    const versions=await fixture.read(`/api/studio/v1/studio/content-items/${encodeURIComponent(draft.itemId)}/versions`);
    expect(versions.versions).toHaveLength(1);
    const version=versions.versions[0];expect(version.versionNumber).toBe(1);expect(version.contentHash).toMatch(/^[a-f0-9]{64}$/);
    const exactVersion=await fixture.read(`/api/studio/v1/studio/content-items/${encodeURIComponent(draft.itemId)}/versions/${encodeURIComponent(version.versionId)}`);
    expect(exactVersion.envelope.body).toEqual(expected.body);
    const proposerResponse=await page.request.get('/api/agent-workspace/session');expect(proposerResponse.ok()).toBe(true);
    const proposer=await proposerResponse.json();expect(proposer.authenticated).toBe(true);
    // Approver credentials stay in a separate browser context, outside all model turns.
    const approverContext=await browser!.newContext({baseURL:origin,storageState:approverState});
    const witness=await request.newContext({baseURL:server.origin,extraHTTPHeaders:{'X-API-Key':required('HONUA_WORKSPACE_E2E_WITNESS_READ_KEY'),Accept:'application/json'}});
    const anonymous=await browser!.newContext({baseURL:origin,storageState:{cookies:[],origins:[]}});
    try {
        const approverResponse=await approverContext.request.get('/api/agent-workspace/session');expect(approverResponse.ok()).toBe(true);
        const approver=await approverResponse.json();expect(approver.authenticated).toBe(true);
        expect(approver.environmentId).toBe(proposer.environmentId);expect(approver.principalId).not.toBe(proposer.principalId);
        await page.getByRole('button',{name:'Saved versions',exact:true}).click();
        await page.getByRole('button',{name:'Review publication of version 1',exact:true}).click();
        const publication=page.getByRole('region',{name:'Map publication',exact:true});
        await expect(publication).toContainText(`Publish saved version 1 of ${fixture.draft.packageKey}`);
        await publication.getByRole('textbox',{name:'Publication route'}).fill(route);
        await publication.getByRole('combobox',{name:'Publication audience'}).selectOption('public');
        await publication.getByRole('button',{name:'Publish reviewed version'}).click();
        await expect(publication).toContainText('Awaiting separate human approval.',{timeout:30_000});
        const proposalId=/Awaiting separate human approval\. Proposal ([A-Za-z0-9_-]{1,128})\./.exec(await publication.textContent() || '')?.[1];
        expect(proposalId).toBeTruthy();
        const proposalPath=`/api/processes/admin/proposals/${encodeURIComponent(proposalId!)}`;
        const diff=[`itemId=${draft.itemId}`,`versionId=${version.versionId}`,`contentHash=${version.contentHash}`,`route=${route}`,'visibility=public'];
        const pending=await fixture.read(proposalPath);expect(pending.diff).toEqual(diff);expect(pending.status.toLowerCase()).toBe('awaitingapproval');
        assertHumanProposer(pending);
        const record=await page.locator('honua-studio-app').evaluate((node,binding)=>{
            const storage=(node as HTMLElement & {workspace?:{storage?:Storage}}).workspace?.storage;
            if(!storage || storage.length>10000) throw new Error('Canonical publication recovery storage is unavailable.');
            const matches=[];
            for(let index=0;index<storage.length;index++) {
                const key=storage.key(index);if(!key?.startsWith('honua.studio.publication.')) continue;
                const raw=storage.getItem(key);if(!raw || raw.length>16384) continue;const record=JSON.parse(raw);
                if(record.itemId===binding.itemId && record.versionId===binding.versionId && record.receipt?.proposalId===binding.proposalId)
                    matches.push({commandId:record.commandId,contentHash:record.contentHash,requestId:record.receipt.requestId,operationInstanceId:record.receipt.operationInstanceId});
            }
            if(matches.length!==1) throw new Error('Exactly one canonical publication command must match the reviewed version.');
            return matches[0];
        },{itemId:draft.itemId,versionId:version.versionId,proposalId});
        expect(record.contentHash).toBe(version.contentHash);expect(record.commandId).toBeTruthy();expect(record.operationInstanceId).toBeTruthy();
        const observeOperation=async()=>{
            const response=await witness.get(`/api/v1/operations/handles/${encodeURIComponent(record.operationInstanceId)}`,{maxRedirects:0});expect(response.ok()).toBe(true);
            const wire=await response.json();return wire.data ?? wire;
        };
        const originalOperation=await observeOperation();expect(originalOperation.operationInstanceId).toBe(record.operationInstanceId);
        expect(originalOperation.operationId).toBe('studio.content.create-publication-request');expect(originalOperation.proposalId).toBe(proposalId);
        expect((await anonymous.request.get(`/api/studio/viewer/published${route}`)).status(),'fresh fixture route must not be public before approval').toBe(404);
        const approvalPage=await approverContext.newPage();
        await approvalPage.goto(`/approvals?proposalId=${encodeURIComponent(proposalId!)}`);
        const panel=approvalPage.locator(`.proposal-approval-panel[data-proposal-id="${proposalId}"]`);
        await expect(panel.locator('[data-proposal-status]')).toHaveAttribute('data-proposal-status','AwaitingApproval');
        await expect(panel.locator('[data-proposal-diff] li')).toHaveText(diff);
        await expect(panel.locator('[data-proposal-approve]')).toBeEnabled();
        await panel.locator('[data-proposal-approve]').click();
        await expect(panel.locator('[data-proposal-action-message]')).toContainText('Approved');
        await expect.poll(async()=>(await fixture.read(proposalPath)).status.toLowerCase(),{timeout:120_000}).toBe('succeeded');
        const resolved=await fixture.read(proposalPath);
        expect(resolved.requestedBy).toBe(pending.requestedBy);assertHumanProposer(resolved);expect(resolved.diff).toEqual(diff);expect(typeof resolved.resolvedBy).toBe('string');
        expect(resolved.resolvedBy.trim()).toBeTruthy();expect(resolved.resolvedBy).not.toBe(pending.requestedBy);
        const finalOperation=await observeOperation();expect(finalOperation.operationInstanceId).toBe(record.operationInstanceId);
        expect(finalOperation.operationId).toBe(originalOperation.operationId);expect(finalOperation.proposalId).toBe(proposalId);
        const requestId=finalOperation.resourceIds?.requestId;expect(typeof requestId).toBe('string');expect(requestId).toMatch(/^[A-Za-z0-9_-]{1,128}$/);
        if(record.requestId!==undefined) expect(requestId).toBe(record.requestId);
        expect(finalOperation.resourceIds?.itemId).toBe(draft.itemId);expect(finalOperation.resourceIds?.versionId).toBe(version.versionId);
        const requestPath=`/api/studio/v1/studio/content-items/${encodeURIComponent(draft.itemId)}/versions/${encodeURIComponent(version.versionId)}/publish-requests/${encodeURIComponent(requestId)}`;
        await expect.poll(async()=>(await fixture.read(requestPath)).status.toLowerCase(),{timeout:60_000}).toBe('accepted');
        const accepted=await fixture.read(requestPath);
        expect(accepted.requestId).toBe(requestId);expect(accepted.itemId).toBe(draft.itemId);expect(accepted.versionId).toBe(version.versionId);
        expect(accepted.intent.route).toBe(route);expect(accepted.intent.visibility).toBe('public');
        const publicResponse=await anonymous.request.get(`/api/studio/viewer/published${route}`);expect(publicResponse.ok()).toBe(true);
        const wire=await publicResponse.json();const artifact=wire.data ?? wire;
        expect(artifact.itemId).toBe(draft.itemId);expect(artifact.versionId).toBe(version.versionId);expect(artifact.publicationId).toBe(requestId);
        expect(artifact.contentHash).toBe(version.contentHash);expect(artifact.family).toBe('map');expect(artifact.envelope.body).toEqual(expected.body);
        const publicPage=await anonymous.newPage();
        const audiencePath=publishedMapPath(accepted.intent.route);
        const audienceResponse=await publicPage.goto(audiencePath);expect(audienceResponse?.status()).toBe(200);
        expect(new URL(publicPage.url()).pathname).toBe(audiencePath);
        expect((await anonymous.cookies()).filter(cookie=>/auth|session|operator/i.test(cookie.name))).toEqual([]);
        await expect(publicPage.locator('[data-map-status]')).toContainText('Map ready.',{timeout:60_000});
        await expect(publicPage.locator('honua-studio-app,[data-agent-workspace]')).toHaveCount(0);
        const assertData=async(rows:string[],bars:string[])=>{
            const grid=publicPage.getByTestId('studio-widget-grid');await expect(grid).toHaveCount(1);
            await expect(grid.locator('thead th')).toHaveText([expected.categoryField,expected.identityField]);
            await expect.poll(async()=>(await grid.getByTestId('studio-widget-grid-row').evaluateAll(nodes=>nodes.map(node=>[...node.querySelectorAll('td')].map(cell=>cell.textContent || '')))).map(value=>JSON.stringify(value)).sort()).toEqual(rows);
            await expect.poll(async()=>(await publicPage.locator('.widget-chart-bar title').allTextContents()).sort()).toEqual(bars);
        };
        const map=publicPage.locator('[data-map-canvas]');
        await assertData(expected.rows,expected.bars);
        await expect.poll(async()=>Number(await map.getAttribute('data-rendered-feature-count'))).toBe(expected.rows.length);
        const filter=publicPage.getByTestId('studio-control-filter-select');await expect(filter).toHaveCount(1);await expect(filter).toHaveAccessibleName(expected.categoryField);
        await filter.selectOption(expected.category);await assertData(expected.filteredRows,expected.filteredBars);
        await expect.poll(async()=>Number(await map.getAttribute('data-rendered-feature-count'))).toBe(expected.filteredRows.length);
        await filter.selectOption('');await assertData(expected.rows,expected.bars);
        await expect.poll(async()=>Number(await map.getAttribute('data-rendered-feature-count'))).toBe(expected.rows.length);
        expect((await fixture.read(`/api/studio/v1/studio/content-items/${encodeURIComponent(draft.itemId)}/versions/${encodeURIComponent(version.versionId)}`)).envelope.body).toEqual(expected.body);
        return {proposalId,itemId:draft.itemId,versionId:version.versionId,contentHash:version.contentHash,requestId,route,
            requestedBySha256:createHash('sha256').update(resolved.requestedBy).digest('hex'),resolvedBySha256:createHash('sha256').update(resolved.resolvedBy).digest('hex'),
            approvalActorMode:'controller automation through separate human-principal sessions; no observed human review',audience:'anonymous',publicLinkedFiltering:true};
    } finally {await anonymous.close();await approverContext.close();await witness.dispose();}
}
