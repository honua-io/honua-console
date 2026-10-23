import { expect, test } from '@playwright/test';
import {loadReleaseProfile,releasePrompt} from './release-profile.mjs';
const profile=loadReleaseProfile(process.env.HONUA_FOSS4G_PROFILE_PROJECTION);
const variant=process.env.HONUA_FOSS4G_VARIANT || 'A';
import { attach, context, finished, identity, send, setup } from './live-context';

// Each case creates its own real workspace and map. Polls below observe eventual
// persistence/rendering; they never resend a prompt or retry a failed operation.
test('[IX-01:selected-map] this map uses the visually selected layer and canonical draft', async({page},info)=>{
  const fixture=await setup(page);
  const original=await context(page);
  const before=await fixture.readBody();
  expect(original.task?.id).toBe(fixture.draft.draftId);
  const title='Current map selected layer';
  const prompt=releasePrompt(profile,'IX-01','selected-map',variant);
  const captured=await send(page,prompt);await finished(page,prompt,captured.turn.turnId);
  expect(captured.turn.context.task.id).toBe(fixture.draft.draftId);
  expect(captured.projected.context.task.id).toBe(fixture.draft.draftId);
  expect(captured.projected.context.selectedResources).toEqual(fixture.expectedSelection);
  await expect.poll(async()=>(await fixture.readBody()).layers.find(layer=>layer.id===fixture.layer.id)?.title).toBe(title);
  const after=await fixture.readBody();
  expect(after.layers).toEqual(before.layers.map(layer=>layer.id===fixture.layer.id ? {...layer,title} : layer));
  expect(after.mapSpec).toEqual(before.mapSpec);
  await expect(page.locator('honua-studio-canvas [data-target-kind="layer"]')).toContainText(title);
  await expect.poll(fixture.paintedCount,{timeout:45_000}).toBeGreaterThan(0);
  expect((await identity(page))?.draftId).toBe(fixture.draft.draftId);
  expect(await fixture.readDraftBody(fixture.distractor.draftId)).toEqual(fixture.distractorBaseline);
  await attach(info,'IX-01:selected-map',fixture,['context-readback','model-turn','durable-readback'],{
    profileDigest:profile.profileDigest,variant,turnId:captured.turn.turnId,selectedResourceCount:original.selectedResources!.length,rendered:true});
});

test('[IX-04:visual-agent-visual] this layer keeps visual colors through prompted opacity and another visual edit',async({page},info)=>{
  const fixture=await setup(page);
  const sample=await fixture.read(`/api/studio/data${fixture.sourcePath}/query?where=1%3D1&outFields=*&returnGeometry=false&resultRecordCount=20&f=json`);
  const candidates=(fixture.metadata.fields as Array<{name:string}>).map(field=>({name:field.name,values:[...new Set<string|number>(
    (sample.features as Array<{attributes:Record<string,unknown>}>).map(feature=>feature.attributes[field.name])
      .filter((value):value is string|number=>typeof value==='string' || typeof value==='number' && Number.isFinite(value)))]}));
  const thematic=candidates.find(candidate=>candidate.values.length>=2);
  expect(thematic,'fixture requires two observed values in a source field').toBeTruthy();
  const renderLayer=async()=>(await fixture.readBody()).mapSpec?.layers.find(layer=>layer.id===fixture.layer.id);
  const initial=(await renderLayer())!;expect(['fill','line','circle','symbol']).toContain(initial.type);
  const prefix=initial.type==='symbol'?'text':initial.type;
  const colorProperty=`${prefix}-color`,opacityProperty=`${prefix}-opacity`;
  const color=(value:string|number)=>['match',['get',thematic!.name],value,'#eff3ff','#c6dbef'];
  const livePaint=(property:string)=>page.locator('honua-studio-canvas').evaluate((node,args)=>
    (node as HTMLElement & {mapView?:{map?:{getPaintProperty(id:string,property:string):unknown}}}).mapView?.map?.getPaintProperty(args.id,args.property),
    {id:fixture.layer.id,property});
  await page.getByRole('button',{name:'Edit map style',exact:true}).click();
  const editor=page.getByRole('region',{name:'Map style editor',exact:true});await expect(editor).toBeVisible();
  await expect(editor.frameLocator('iframe[title="Maputnik visual map style editor"]').locator('.maputnik-layout')).toBeVisible();
  await editor.getByRole('combobox',{name:'Map layer for styling',exact:true}).selectOption(fixture.layer.id);
  await editor.getByRole('combobox',{name:'Actual source field',exact:true}).selectOption(thematic!.name);
  await editor.getByRole('combobox',{name:'Thematic method',exact:true}).selectOption('categories');
  const values=editor.getByRole('textbox',{name:'Explicit thematic values as JSON array',exact:true});
  await values.fill(JSON.stringify([thematic!.values[0]]));
  await editor.getByRole('button',{name:'Preview thematic colors',exact:true}).click();
  await expect.poll(()=>livePaint(colorProperty)).toEqual(color(thematic!.values[0]));
  await editor.getByRole('button',{name:'Save map style',exact:true}).click();
  await expect.poll(async()=>(await renderLayer())?.paint?.[colorProperty]).toEqual(color(thematic!.values[0]));
  const original=await context(page);
  const prompt=releasePrompt(profile,'IX-04','visual-agent-visual',variant);
  const captured=await send(page,prompt);await finished(page,prompt,captured.turn.turnId);
  expect(captured.projected.context.task.id).toBe(fixture.draft.draftId);
  expect(captured.projected.context.selectedResources).toEqual(fixture.expectedSelection);
  await expect.poll(async()=>(await renderLayer())?.paint?.[opacityProperty]).toBe(0.55);
  expect((await renderLayer())?.paint?.[colorProperty]).toEqual(color(thematic!.values[0]));
  await expect.poll(()=>livePaint(opacityProperty)).toBe(0.55);
  await values.fill(JSON.stringify([thematic!.values[1]]));
  await editor.getByRole('button',{name:'Preview thematic colors',exact:true}).click();
  await editor.getByRole('button',{name:'Save map style',exact:true}).click();
  await expect.poll(async()=>(await renderLayer())?.paint?.[colorProperty]).toEqual(color(thematic!.values[1]));
  expect((await renderLayer())?.paint?.[opacityProperty]).toBe(0.55);
  await expect.poll(()=>livePaint(opacityProperty)).toBe(0.55);
  await page.getByRole('button',{name:'Return to map',exact:true}).click();
  await expect(editor).toHaveCount(0);await expect.poll(fixture.paintedCount,{timeout:45_000}).toBeGreaterThan(0);
  expect((await identity(page))?.draftId).toBe(fixture.draft.draftId);
  expect(await fixture.readDraftBody(fixture.distractor.draftId)).toEqual(fixture.distractorBaseline);
  await attach(info,'IX-04:visual-agent-visual',fixture,['context-readback','model-turn','durable-readback','visual-agent-roundtrip'],{
    profileDigest:profile.profileDigest,variant,turnId:captured.turn.turnId,scope:'map-local',opacity:0.55,visualColorsPreserved:true});
});

test('[IX-06:navigate-midturn] navigating during a real agent turn preserves its original map target',async({page},info)=>{
  const fixture=await setup(page);
  const original=await context(page);
  const title='Original map after navigation';
  const prompt=releasePrompt(profile,'IX-06','navigate-midturn',variant);
  const captured=await send(page,prompt);
  const inFlight=await page.evaluate(()=>(window as Window & {honuaAgentWorkspace:{agentSession?:{busy:boolean}}}).honuaAgentWorkspace.agentSession?.busy);
  expect(inFlight,'navigation coverage requires a genuine active agent turn, without artificial delay').toBe(true);
  // A real human opens a different canonical map while the original turn is active.
  const next=await fixture.createMap(`interaction-next-${Date.now().toString(36)}`);
  const nextBaseline=await fixture.readDraftBody(next.draftId);
  expect(next.draftId).not.toBe(fixture.draft.draftId);
  expect((await context(page)).task?.id).toBe(next.draftId);
  expect(await page.evaluate(()=>(window as Window & {honuaAgentWorkspace:{agentSession?:{busy:boolean}}}).honuaAgentWorkspace.agentSession?.busy),
    'map B must actually be active before the original agent turn finishes').toBe(true);
  expect(captured.turn.context.targetId).toBe(original.targetId);
  expect(captured.turn.context.task.id).toBe(fixture.draft.draftId);
  expect(captured.projected.context.task.id).toBe(fixture.draft.draftId);
  expect(captured.projected.context.selectedResources).toEqual(fixture.expectedSelection);
  await finished(page,prompt,captured.turn.turnId);
  await expect.poll(async()=>(await fixture.readBody()).layers.find(layer=>layer.id===fixture.layer.id)?.title).toBe(title);
  expect(await fixture.readDraftBody(next.draftId)).toEqual(nextBaseline);
  expect((await identity(page))?.draftId).toBe(next.draftId);
  expect((await context(page)).task?.id).toBe(next.draftId);
  await expect(page.locator('[data-workspace-target]')).toContainText(next.packageKey);
  await expect(page.locator('[data-workspace-messages]')).toContainText(prompt);
  await attach(info,'IX-06:navigate-midturn',fixture,['context-readback','model-turn','durable-readback','frozen-turn-target'],{
    profileDigest:profile.profileDigest,variant,turnId:captured.turn.turnId,targetId:original.targetId,navigationWhileActive:inFlight,nextRequestDraftId:next.draftId,nextMapUnchanged:true});
});

test('[AI-03:map-dashboard] map chart table and map filter save and reopen as one composition',async({page},info)=>{
  const {dashboard}=await import('./dashboard');
  await dashboard(page,info,profile,variant);
});
