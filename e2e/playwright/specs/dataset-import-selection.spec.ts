import {createHash} from 'node:crypto';
import {test,expect,type Page} from '@playwright/test';

// Published ASP.NET assets use fingerprinted URLs through the import map.
const importSdkUrl = (url: URL) => /^\/_studio\/console(?:\.[A-Za-z0-9_-]+)?\.js$/.test(url.pathname);

async function controlledSdk(page: Page, body: string, beforeServe?: () => Promise<void>) {
    const integrity = `sha256-${createHash('sha256').update(body).digest('base64')}`;
    await page.route(importSdkUrl,async route=>{
        await beforeServe?.();
        await route.fulfill({contentType:'text/javascript',body});
    });
    await page.route(url=>url.pathname==='/operate/resources/import/task',async route=>{
        const response=await route.fetch();
        const html=await response.text();
        const baseHref=html.match(/<base\s+href="([^"]+)"/i)?.[1];
        const documentBase=new URL(baseHref ?? route.request().url(),route.request().url());
        let matched=0;
        // The published import map protects its modules with SRI. Bind only this
        // controlled fixture's entry to its actual digest; preserve every other
        // asset's integrity. Production assets and headers are unchanged.
        const controlled=html.replace(/(<script[^>]*\btype="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
            (_match,start,json,end)=>{
                const map=JSON.parse(json);
                for(const key of Object.keys(map.integrity || {}))
                    if(importSdkUrl(new URL(key,documentBase))) {map.integrity[key]=integrity;matched++;}
                return start+JSON.stringify(map)+end;
            });
        expect(matched,'Controlled SDK must replace an existing import-map integrity entry').toBeGreaterThan(0);
        // Await the real interactive boot before applying controlled workspace
        // state; otherwise hydration can replace the panel during an assertion.
        const manualBoot=controlled.replace(/<script src="([^"]*\/_framework\/blazor\.web\.[^"]+)"/,
            '<script autostart="false" src="$1"');
        expect(manualBoot).not.toBe(controlled);
        await route.fulfill({response,body:manualBoot});
    });
}

// Controlled SDK loader exercises the actual mounted prehash click orchestration.
// This is not a connected server, canonical SDK hash, or accepted import journey.
test('paused file check cannot retarget to a later selection while the SDK import waits',async({page})=>{
    let release: (()=>void) | undefined;
    const waiting = new Promise<void>(resolve=>release=resolve);
    let requested: (()=>void) | undefined;
    const importRequested = new Promise<void>(resolve=>requested=resolve);
    await controlledSdk(page,`window.__importSdkLoaded=true; export async function hashDatasetImportFile(file) { window.__checkedImportFiles.push(file.name); return '${'a'.repeat(64)}'; }`,async()=>{
        requested?.();await waiting;
    });
    await page.goto('/operate/resources/import/task');
    await page.evaluate(()=>(window as any).Blazor.start());
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
    await expect.poll(()=>page.evaluate(()=>(window as any).__importSdkLoaded)).toBe(true);
    await expect(page.locator('[data-import-status]')).toContainText('Choose your dataset');
    await expect.poll(()=>page.evaluate(()=>(window as any).__checkedImportFiles)).toEqual([]);
    await expect(page.getByRole('button',{name:'Check complete file',exact:true})).toBeEnabled();
});

test('accepted import polling rebinds a replaced workspace without resubmission',async({page})=>{
    await controlledSdk(page,`
            window.__importSdkLoaded=true;
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
        `);
    await page.goto('/operate/resources/import/task');
    await page.evaluate(()=>(window as any).Blazor.start());
    await page.evaluate(()=>{
        const state=window as any, workspace=state.honuaAgentWorkspace;
        state.__importClients=0;state.__unexpectedSubmissions=0;state.__sessionReads=[];
        workspace.session={environmentId:'env',principalId:'human',releaseId:'release',csrfToken:'controlled-test'};
        workspace.workspaceId='workspace';workspace.context={targetId:'target',revision:1,
            task:{kind:'dataset-import',id:'task',revision:1,contentHash:'a'.repeat(64)}};
        workspace.presentedImport=JSON.stringify(workspace.context.task);
        workspace.api=async(path: string)=>{
            if(path==='session'){state.__sessionReads.push('original');return workspace.session;}
            if(path.startsWith(`${workspace.workspaceId}?viewId=`)) return workspace.context;
            if(path===`${workspace.workspaceId}/conversation`) return {messages:[]};
            throw new Error(`Unexpected controlled workspace call: ${path}`);
        };
        window.dispatchEvent(new CustomEvent('honua-workspace-state'));
    });
    const status=page.locator('[data-import-status]');
    await expect.poll(()=>page.evaluate(()=>(window as any).__importSdkLoaded)).toBe(true);
    await expect(status).toHaveText('Confirming import outcome. Honua is checking whether the data was saved.');
    await expect(page.locator('[data-import-recover]')).toBeEnabled();
    await page.evaluate(()=>window.dispatchEvent(new CustomEvent('honua-workspace-state')));
    await expect.poll(()=>page.evaluate(()=>(window as any).__sessionReads.length)).toBeGreaterThanOrEqual(2);
    expect(await page.evaluate(()=>(window as any).__importClients)).toBe(1);
    await expect(page.locator('[data-import-recover]')).toBeEnabled();
    await page.evaluate(()=>{
        const state=window as any, old=state.honuaAgentWorkspace;
        const replacement=Object.assign(Object.create(Object.getPrototypeOf(old)),old,{presentationGuards:new Set()});
        replacement.api=async(path: string)=>{
            if(path==='session'){state.__sessionReads.push('replacement');return replacement.session;}
            if(path.startsWith(`${replacement.workspaceId}?viewId=`)) return replacement.context;
            if(path===`${replacement.workspaceId}/conversation`) return {messages:[]};
            throw new Error(`Unexpected controlled workspace call: ${path}`);
        };
        Object.defineProperty(window,'honuaAgentWorkspace',{value:replacement,configurable:true});
        window.dispatchEvent(new CustomEvent('honua-workspace-state'));
    });
    await expect.poll(()=>page.evaluate(()=>(window as any).__sessionReads.includes('replacement'))).toBe(true);
    await expect(status).toHaveText('Confirming import outcome. Honua is checking whether the data was saved.');
    expect(await page.evaluate(()=>(window as any).__importClients)).toBe(2);
    expect(await page.evaluate(()=>(window as any).__unexpectedSubmissions)).toBe(0);
});
