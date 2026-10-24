import { createHash, randomUUID } from 'node:crypto';
import { expect, type Page, type TestInfo } from '@playwright/test';
import { sendInstalled, finishedInstalled, installedSession, executionEvidence } from '../workspace-live/execution';

export type Body = {
  layers: Array<{id:string; sourceId:string; title?:string}>;
  widgets?:Array<{id:string;kind:string;sourceId?:string;config?:Record<string,unknown>}>;
  controls?:Array<{id:string;kind:string;sourceId?:string;config?:Record<string,unknown>}>;
  mapSpec?: {layers:Array<{id:string;type:string;paint?:Record<string,unknown>}>};
};
type Identity = {draftId:string;generation:number;packageKey:string};
type Context = {targetId:string;revision:number;task?:{id:string;revision:number;packageKey?:string};selectedResources?:unknown[]};
type Runtime = Window & {honuaAgentWorkspace:{workspaceId:string;context:Context;agentSession?:{busy:boolean}}};
type App = HTMLElement & {workspaceDraftIdentity?:Identity;sourceCatalog?:Array<{id:string;locator?:{layerId:number;serviceId?:string;url?:string}}>};
export function required(name:string) {
  const value=process.env[name]?.trim();
  if (!value || value.length>256 || /[\r\n]/.test(value)) throw new Error(`${name} is required from the verified evaluation deployment manifest.`);
  return value;
}
export async function identity(page:Page) {
  return page.locator('honua-studio-app').evaluate(node=>(node as App).workspaceDraftIdentity ?? null);
}
export async function context(page:Page) {
  return page.evaluate(()=>structuredClone((window as Runtime).honuaAgentWorkspace.context));
}
export const send=sendInstalled;
export const finished=finishedInstalled;
export async function setup(page:Page) {
  const service=required('HONUA_WORKSPACE_E2E_SERVICE');
  const layerText=required('HONUA_WORKSPACE_E2E_LAYER');
  expect(layerText).toMatch(/^\d+$/);
  const layerId=Number(layerText);
  expect(Number.isSafeInteger(layerId)).toBe(true);
  const artifacts={server:required('HONUA_WORKSPACE_E2E_SERVER_SHA'),studio:required('HONUA_WORKSPACE_E2E_STUDIO_SHA'),model:required('HONUA_WORKSPACE_E2E_MODEL_ID'),console:''};
  const version=await page.request.get('/version.json');expect(version.status()).toBe(200);
  artifacts.console=(await version.json()).commit;
  expect(artifacts.console,'installed Console artifact must identify its commit').toMatch(/^[a-f0-9]{40}$/);
  await page.goto('/studio');
  const {session}=await installedSession(page);
  expect(session.environmentId).toBeTruthy();expect(session.principalId).toBeTruthy();
  const headers={'X-Honua-Environment':session.environmentId,'X-Honua-Session-Partition':session.principalId};
  const read=async(path:string)=>{
    const response=await page.request.get(path,{headers});
    expect(response.status(),`authorized read: ${path.split('?')[0]}`).toBe(200);
    const value=await response.json();return value.data ?? value;
  };
  const sourcePath=`/rest/services/${service.split('/').map(encodeURIComponent).join('/')}/FeatureServer/${layerId}`;
  const count=await read(`/api/studio/data${sourcePath}/query?where=1%3D1&returnCountOnly=true&f=json`);
  expect(count.count,'fixture must contain actual features').toBeGreaterThan(0);
  const metadata=await read(`/api/studio/data${sourcePath}?f=json`);
  expect(metadata.name,'fixture must have a human readable published layer name').toBeTruthy();
  const extent=(await read(`/api/studio/data${sourcePath}/query?where=1%3D1&returnExtentOnly=true&outSR=4326&f=json`)).extent;
  expect(extent?.spatialReference?.wkid).toBe(4326);
  const bounds=[extent.xmin,extent.ymin,extent.xmax,extent.ymax];expect(bounds.every(Number.isFinite)).toBe(true);
  const host=page.locator('[data-agent-workspace]');
  if (!await host.evaluate(node=>(node as HTMLDetailsElement).open)) await host.locator(':scope > summary').click();
  await host.getByRole('button',{name:'Start conversation',exact:true}).click();
  await expect(host).toHaveAttribute('data-connected','true');
  await expect(page.locator('textarea:visible')).toHaveCount(1);
  const createMap=async(packageKey:string)=>{
    await page.getByRole('textbox',{name:'New map package key'}).fill(packageKey);
    await page.getByRole('button',{name:'New map',exact:true}).click();
    await expect.poll(()=>identity(page).then(value=>value?.packageKey)).toBe(packageKey);
    const created=(await identity(page))!;
    await expect.poll(()=>context(page).then(value=>value.task?.id)).toBe(created.draftId);
    return created;
  };
  const readDraftBody=async(id:string):Promise<Body>=>(await read(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(id)}`)).envelope.body;
  const distractor=await createMap(`interaction-distractor-${randomUUID()}`);
  const distractorBaseline=await readDraftBody(distractor.draftId);
  const packageKey=`interaction-${randomUUID()}`;
  const draft=await createMap(packageKey);
  const readBody=async():Promise<Body>=>(await read(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(draft.draftId)}`)).envelope.body;
  const suffix=sourcePath.slice(0,sourcePath.lastIndexOf('/'));
  const sources=()=>page.locator('honua-studio-app').evaluate((node,expected)=>(node as App).sourceCatalog?.filter(source=>source.locator?.layerId===expected.layerId && source.locator.url?.endsWith(expected.suffix)) ?? [],{layerId,suffix});
  await expect.poll(()=>sources().then(value=>value.length)).toBe(1);
  const source=(await sources())[0];
  // Setup names an actual published human-facing layer; subsequent test prompts use only visual context.
  const prompt=`Add the published layer named "${metadata.name}" from service "${service}" to this map. Set its view to these WGS84 bounds: ${JSON.stringify(bounds)}. Use the existing published data. Do not publish anything or change source data.`;
  const setupTurn=await send(page,prompt);await finished(page,prompt,setupTurn.turn.turnId);
  await expect.poll(async()=>(await readBody()).layers?.filter(layer=>layer.sourceId===source.id).length).toBe(1);
  const body=await readBody();const layer=body.layers.find(layer=>layer.sourceId===source.id)!;
  const row=page.locator('honua-studio-canvas [data-target-kind="layer"]').filter({hasText:layer.title || layer.id});
  await expect(row).toHaveCount(1);await row.click();await expect(row).toHaveAttribute('aria-pressed','true');
  const paintedCount=()=>page.locator('honua-studio-canvas').evaluate((node,id)=>{
    const map=(node as HTMLElement & {mapView?:{map?:{getLayer(id:string):unknown;queryRenderedFeatures(options:{layers:string[]}):unknown[]}}}).mapView?.map;
    return map?.getLayer(id) ? map.queryRenderedFeatures({layers:[id]}).length : 0;
  },layer.id);
  await expect.poll(paintedCount,{timeout:45_000}).toBeGreaterThan(0);
  expect(source.locator?.serviceId,'published source must expose exact service identity').toBeTruthy();
  const expectedSelection=[{kind:'layer',id:String(layerId),serviceId:source.locator!.serviceId}];
  await expect.poll(()=>context(page).then(value=>value.selectedResources)).toEqual(expectedSelection);
  return {page,artifacts,draft,layer,source,read,readBody,sourcePath,metadata,paintedCount,createMap,readDraftBody,distractor,distractorBaseline,expectedSelection};
}
export async function attach(info:TestInfo,scenarioId:string,fixture:Awaited<ReturnType<typeof setup>>,observations:string[],details:Record<string,unknown>) {
  // No transport headers, credentials, feature attributes, model transcript or raw server bodies.
  const evidence={schemaVersion:'interaction-evidence.v1',scenarioId,kind:'live',entrypoint:'console',artifacts:fixture.artifacts,artifactSources:{console:'observed /version.json commit',studio:'operator-supplied deployment manifest expectation; not observed provenance',server:'operator-supplied deployment manifest expectation; not observed provenance',model:'operator-supplied verified deployment manifest expectation; not observed provenance'},observations,
    executions:executionEvidence(fixture.page),draftId:fixture.draft.draftId,bodySha256:createHash('sha256').update(JSON.stringify(await fixture.readBody())).digest('hex'),...details};
  const body=JSON.stringify(evidence);expect(Buffer.byteLength(body)).toBeLessThan(16_384);
  await info.attach('interaction-evidence',{contentType:'application/json',body:Buffer.from(body)});
}
