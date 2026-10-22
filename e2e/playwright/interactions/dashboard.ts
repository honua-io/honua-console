import {expect,type Page,type TestInfo} from '@playwright/test';
import {attach,context,finished,identity,required,send,setup} from './live-context';
import {releasePrompt} from './release-profile.mjs';

// This measures the shipped map-family composition, not a native dashboard
// package. It requires shared map/chart/table filtering in the installed bundle.
export async function dashboard(page:Page,info:TestInfo,profile:any,variant:string) {
    const fixture=await setup(page);
    const categoryField=required('HONUA_WORKSPACE_E2E_CATEGORY_FIELD');
    const identityField=fixture.metadata.objectIdField;
    expect(typeof identityField).toBe('string');
    expect(fixture.metadata.fields.some((field:{name:string})=>field.name===categoryField)).toBe(true);
    const all=await fixture.read(`/api/studio/data${fixture.sourcePath}/query?where=1%3D1&outFields=${encodeURIComponent(`${categoryField},${identityField}`)}&returnGeometry=false&resultRecordCount=100&f=json`);
    const count=await fixture.read(`/api/studio/data${fixture.sourcePath}/query?where=1%3D1&returnCountOnly=true&f=json`);
    expect(count.count,'dashboard fixture must be complete and fit one bounded widget/table page').toBeLessThanOrEqual(50);
    expect(all.features).toHaveLength(count.count);expect(all.exceededTransferLimit).not.toBe(true);
    const rows=all.features.map((feature:{attributes:Record<string,unknown>})=>feature.attributes);
    expect(rows.every((row:Record<string,unknown>)=>typeof row[categoryField]==='string' && row[categoryField]!=='' && row[identityField]!=null)).toBe(true);
    const categories=new Map<string,number>();
    for(const row of rows) categories.set(row[categoryField],(categories.get(row[categoryField]) || 0)+1);
    expect(categories.size).toBeGreaterThanOrEqual(2);
    const prompt=releasePrompt(profile,'AI-03','map-dashboard',variant)
        .replaceAll('{categoryField}',categoryField).replaceAll('{identityField}',identityField);
    const captured=await send(page,prompt);await finished(page,prompt,captured.turn.turnId);
    expect(captured.projected.context.task.id).toBe(fixture.draft.draftId);
    const readComposition=async()=>{
        const body=await fixture.readBody();
        return {charts:body.widgets?.filter(widget=>widget.kind==='chart' && widget.sourceId===fixture.source.id),
            tables:body.widgets?.filter(widget=>widget.kind==='table' && widget.sourceId===fixture.source.id),
            filters:body.controls?.filter(control=>control.kind==='filterSelect' && control.sourceId===fixture.source.id)};
    };
    await expect.poll(async()=>{
        const value=await readComposition();return [value.charts?.length,value.tables?.length,value.filters?.length];
    }).toEqual([1,1,1]);
    const expectedRows=rows.map((row:Record<string,unknown>)=>[String(row[categoryField]),String(row[identityField])]).map(value=>JSON.stringify(value)).sort();
    const expectedBars=[...categories].map(([category,total])=>`${category}: ${total}`).sort();
    const assertData=async(expectedGrid=expectedRows,expectedChart=expectedBars)=>{
        const grid=page.getByTestId('studio-widget-grid');await expect(grid).toHaveCount(1);
        await expect(grid.locator('thead th')).toHaveText([categoryField,identityField]);
        await expect.poll(async()=>{
            const displayed=await grid.getByTestId('studio-widget-grid-row').evaluateAll(nodes=>nodes.map(node=>
                [...node.querySelectorAll('td')].map(cell=>cell.textContent || '')));
            return displayed.map(value=>JSON.stringify(value)).sort();
        }).toEqual(expectedGrid);
        const chart=page.getByTestId('studio-widget-chart');await expect(chart).toHaveCount(1);
        await expect.poll(async()=>(await chart.locator('.widget-chart-bar title').allTextContents()).sort()).toEqual(expectedChart);
    };
    await assertData();await expect.poll(fixture.paintedCount,{timeout:45_000}).toBeGreaterThan(0);
    const filter=page.getByTestId('studio-control-filter-select');await expect(filter).toHaveCount(1);
    await expect(filter).toHaveAccessibleName(categoryField);
    const category=[...categories.keys()][0];
    const filteredRows=rows.filter((row:Record<string,unknown>)=>row[categoryField]===category)
        .map((row:Record<string,unknown>)=>JSON.stringify([String(row[categoryField]),String(row[identityField])])).sort();
    const filteredBars=[`${category}: ${categories.get(category)}`];
    const assertFilter=async()=>{
        await filter.selectOption(category);
        await expect.poll(()=>page.locator('honua-studio-canvas').evaluate((node,args)=>{
            const map=(node as HTMLElement & {mapView?:{map?:{getFilter(id:string):unknown;queryRenderedFeatures(options:{layers:string[]}):Array<{properties?:Record<string,unknown>}>}}}).mapView?.map;
            const rendered=map?.queryRenderedFeatures({layers:[args.id]}) || [];
            return {filter:map?.getFilter(args.id),count:rendered.length,allMatch:rendered.length>0 && rendered.every(feature=>feature.properties?.[args.field]===args.category)};
        },{id:fixture.layer.id,field:categoryField,category})).toMatchObject({filter:['==',['get',categoryField],category],allMatch:true});
        // A filter expression alone is not a rendered-data oracle.
        await expect.poll(()=>page.locator('honua-studio-canvas').evaluate((node,id)=>
            (node as HTMLElement & {mapView?:{map?:{queryRenderedFeatures(options:{layers:string[]}):unknown[]}}}).mapView?.map?.queryRenderedFeatures({layers:[id]}).length || 0,
            fixture.layer.id),{timeout:45_000}).toBeGreaterThan(0);
    };
    const clearFilter=async()=>{
        await filter.selectOption('');
        await expect.poll(()=>page.locator('honua-studio-canvas').evaluate((node,id)=>
            (node as HTMLElement & {mapView?:{map?:{getFilter(id:string):unknown}}}).mapView?.map?.getFilter(id)==null,fixture.layer.id)).toBe(true);
        await assertData();
    };
    await assertFilter();
    await assertData(filteredRows,filteredBars);
    await clearFilter();
    await page.getByRole('button',{name:'Save version',exact:true}).click();
    await expect(page.locator('[data-studio-status]')).toHaveText('Saved version 1.');
    const saved=await fixture.readBody();
    await page.getByRole('button',{name:'Saved versions',exact:true}).click();
    await page.getByRole('button',{name:'Reopen version 1 as draft',exact:true}).click();
    await expect.poll(()=>identity(page).then(value=>Boolean(value?.draftId && value.draftId!==fixture.draft.draftId))).toBe(true);
    const reopened=(await identity(page))!;
    expect(await fixture.readDraftBody(reopened.draftId)).toEqual(saved);
    await expect.poll(()=>context(page).then(value=>value.task?.id)).toBe(reopened.draftId);
    await assertData();await assertFilter();await assertData(filteredRows,filteredBars);
    await clearFilter();
    const originalDraft=await fixture.read(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(fixture.draft.draftId)}`);
    expect(originalDraft.family || originalDraft.envelope.family).toBe('map');
    await attach(info,'AI-03:map-dashboard',fixture,['context-readback','model-turn','durable-readback','dashboard-render','dashboard-linked-filter','saved-version-reopen'],{
        profileDigest:profile.profileDigest,variant,turnId:captured.turn.turnId,family:'map',reopenedDraftId:reopened.draftId,
        displayedRows:rows.length,aggregateCounts:[...categories.values()].sort(),filterScope:'linked map/chart/table',
        qualificationGaps:['publication and separate approval']});
}
