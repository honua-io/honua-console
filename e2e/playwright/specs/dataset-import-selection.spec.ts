import {test,expect} from '@playwright/test';

// Controlled SDK loader exercises the actual mounted prehash click orchestration.
// This is not a connected server, canonical SDK hash, or accepted import journey.
test('paused file check cannot retarget to a later selection while the SDK import waits',async({page})=>{
    let release: (()=>void) | undefined;
    const waiting = new Promise<void>(resolve=>release=resolve);
    let requested: (()=>void) | undefined;
    const importRequested = new Promise<void>(resolve=>requested=resolve);
    await page.route('**/_studio/console.js',async route=>{
        requested?.();await waiting;
        await route.fulfill({contentType:'text/javascript',body:`export async function hashDatasetImportFile(file) { window.__checkedImportFiles.push(file.name); return '${'a'.repeat(64)}'; }`});
    });
    await page.goto('/operate/resources/import/task');
    await page.evaluate(()=>{
        const workspace=(window as any).honuaAgentWorkspace;
        workspace.session={environmentId:'env',principalId:'human',environmentName:'Controlled test',releaseId:'release'};
        workspace.workspaceId='workspace';workspace.context={targetId:'target',revision:0,selectedResources:[]};
        (window as any).__checkedImportFiles=[];
        window.dispatchEvent(new CustomEvent('honua-workspace-state'));
    });
    const file=page.locator('[data-import-file]');
    await expect(file).toBeEnabled();
    await file.setInputFiles({name:'original.geojson',mimeType:'application/geo+json',buffer:Buffer.from('original')});
    await page.getByRole('button',{name:'Check complete file',exact:true}).click();
    await importRequested;
    await page.getByRole('button',{name:'Pause',exact:true}).click();
    await expect(file).toBeEnabled();
    await file.setInputFiles({name:'later.geojson',mimeType:'application/geo+json',buffer:Buffer.from('later')});
    release?.();
    await expect(page.locator('[data-import-status]')).toContainText('Choose your dataset');
    await expect.poll(()=>page.evaluate(()=>(window as any).__checkedImportFiles)).toEqual([]);
    await expect(page.getByRole('button',{name:'Check complete file',exact:true})).toBeEnabled();
});

test('accepted import polling rebinds a replaced workspace without resubmission',async({page})=>{
    await page.route('**/_studio/console.js',async route=>{
        await route.fulfill({contentType:'text/javascript',body:`
            export class DatasetImportTaskClient {}
            export class DatasetImportUploadClient {}
            export async function hashDatasetImportFile() { throw new Error('No file check expected'); }
            export class DatasetImportSubmissionClient {
                constructor(options) { this.options=options; window.__importClients++; }
                async read() { await this.options.headers(); return this.receipt(); }
                async reconcile() { await this.options.headers(); return this.receipt(); }
                receipt() { return {state:'accepted',operationId:'original-job',job:{jobId:'original-job',leaseId:'original-lease',status:'queued',outcomePending:true}}; }
                async submit() { window.__unexpectedSubmissions++; throw new Error('No submission expected'); }
            }
        `});
    });
    await page.goto('/operate/resources/import/task');
    await page.evaluate(()=>{
        const state=window as any, workspace=state.honuaAgentWorkspace;
        state.__importClients=0;state.__unexpectedSubmissions=0;state.__sessionReads=[];
        workspace.session={environmentId:'env',principalId:'human',releaseId:'release',csrfToken:'controlled-test'};
        workspace.workspaceId='workspace';workspace.context={targetId:'target',revision:1,
            task:{kind:'dataset-import',id:'task',revision:1,contentHash:'a'.repeat(64)}};
        workspace.api=async()=>{state.__sessionReads.push('original');return workspace.session;};
        window.dispatchEvent(new CustomEvent('honua-workspace-state'));
    });
    const status=page.locator('[data-import-status]');
    await expect(status).toHaveText('Confirming import outcome. Honua is checking whether the data was saved.');
    await expect(page.locator('[data-import-recover]')).toBeEnabled();
    await page.evaluate(()=>window.dispatchEvent(new CustomEvent('honua-workspace-state')));
    await expect.poll(()=>page.evaluate(()=>(window as any).__sessionReads.length)).toBeGreaterThanOrEqual(2);
    expect(await page.evaluate(()=>(window as any).__importClients)).toBe(1);
    await expect(page.locator('[data-import-recover]')).toBeEnabled();
    await page.evaluate(()=>{
        const state=window as any, old=state.honuaAgentWorkspace;
        const replacement={...old,presentationGuards:new Set()};
        replacement.api=async()=>{state.__sessionReads.push('replacement');return replacement.session;};
        state.honuaAgentWorkspace=replacement;
        window.dispatchEvent(new CustomEvent('honua-workspace-state'));
    });
    await expect.poll(()=>page.evaluate(()=>(window as any).__sessionReads.includes('replacement'))).toBe(true);
    await expect(status).toHaveText('Confirming import outcome. Honua is checking whether the data was saved.');
    expect(await page.evaluate(()=>(window as any).__importClients)).toBe(2);
    expect(await page.evaluate(()=>(window as any).__unexpectedSubmissions)).toBe(0);
});
