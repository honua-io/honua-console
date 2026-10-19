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
