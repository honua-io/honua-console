import { isDeepStrictEqual } from 'node:util';
import { expect, type Page } from '@playwright/test';
import { readExecution } from '../../../src/Honua.Console.Web/wwwroot/js/workspace-executions.mjs';
import { checkedBinding, projectWitness, submittedDigest, validateProjection } from './private-execution-witness.mjs';

type Session = {authenticated:boolean;nativeAgentAvailable:boolean;environmentId:string;principalId:string};
type Capture = {workspaceId:string;turnId:string;executionId:string;requestDigest:string;turn:any;
  session:Session;binding:any;projected:{context:any}|null;proof:any};
const captures = new WeakMap<Page, Map<string,Capture>>();
const identifier = (value:unknown):value is string => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
const headers = (session:Session) => ({'X-Honua-Environment':session.environmentId,
  'X-Honua-Session-Partition':session.principalId});

export async function installedSession(page:Page) {
  const binding = await checkedBinding();
  const response = await page.request.get('/api/agent-workspace/session');
  expect(response.status(),'a real authenticated operator session is required').toBe(200);
  const session:Session = await response.json();
  expect(session.authenticated).toBe(true);
  expect(session.nativeAgentAvailable,'installed execution must not fall back to an in-browser SDK model').toBe(false);
  expect(session.environmentId).toBeTruthy();expect(session.principalId).toBeTruthy();
  return {session,binding};
}

async function readOriginal(page:Page,capture:Capture) {
  const response=await page.request.get('/api/agent-workspace/'+encodeURIComponent(capture.workspaceId)
    +'/turns/'+encodeURIComponent(capture.turnId),{headers:headers(capture.session)});
  expect(response.status(),'the original turn must remain readable with its original human authority').toBe(200);
  const text=await response.text();expect(Buffer.byteLength(text)).toBeLessThanOrEqual(1024*1024);
  const turn=JSON.parse(text);
  expect(turn.turnId).toBe(capture.turnId);
  expect(turn.context.workspaceId).toBe(capture.workspaceId);
  expect(turn.submission).toMatchObject({turnId:capture.turnId,requestDigest:capture.requestDigest,
    disposition:'accepted',executionId:capture.executionId});
  expect(isDeepStrictEqual(turn.context,capture.turn.context),'the admitted context is immutable after navigation').toBe(true);
  const execution=readExecution(turn.execution);
  expect(execution.executionId).toBe(capture.executionId);
  return {turn,execution,text};
}

export async function sendInstalled(page:Page,prompt:string) {
  const {session,binding}=await installedSession(page);
  const workspaceId=await page.evaluate(()=>(window as any).honuaAgentWorkspace.workspaceId);
  expect(identifier(workspaceId)).toBe(true);
  expect(await page.evaluate(()=>(window as any).honuaAgentWorkspace.agentSession)).toBeNull();
  const path='/api/agent-workspace/'+encodeURIComponent(workspaceId)+'/turns';
  const pending=page.waitForRequest(request=>new URL(request.url()).pathname===path && request.method()==='POST');
  const host=page.locator('[data-agent-workspace]');
  await host.getByRole('textbox',{name:'Message',exact:true}).fill(prompt);
  await host.getByRole('button',{name:'Send',exact:true}).click();
  const request=await pending;
  const bytes=request.postDataBuffer();
  expect(bytes,'the original exact submitted bytes must be observable').not.toBeNull();
  const payload=JSON.parse(bytes!.toString('utf8'));
  expect(payload.prompt).toBe(prompt);expect(identifier(payload.turnId)).toBe(true);
  const requestDigest=submittedDigest(bytes);
  const response=await request.response();
  // Read-only recovery observes exactly the original submitted ID. Never click
  // Send again when a response was lost or a refusal/unknown outcome occurred.
  const original=response?.ok() ? response : await page.request.get(
    path+'/'+encodeURIComponent(payload.turnId),{headers:headers(session)});
  expect(original.ok(),'the submitted request must have an authoritative original receipt').toBe(true);
  const turn=await original.json();
  expect(turn.turnId).toBe(payload.turnId);expect(turn.context.workspaceId).toBe(workspaceId);
  const execution=readExecution(turn.execution);
  expect(turn.submission).toMatchObject({turnId:payload.turnId,requestDigest,disposition:'accepted',
    executionId:execution.executionId});
  const capture:Capture={workspaceId,turnId:turn.turnId,executionId:execution.executionId,requestDigest,
    turn,session,binding,projected:null,proof:null};
  if (!captures.has(page)) captures.set(page,new Map());
  expect(captures.get(page)!.has(capture.turnId),'one submitted request cannot be captured twice').toBe(false);
  captures.get(page)!.set(capture.turnId,capture);
  return capture;
}

export async function activeInstalled(page:Page,turnId:string) {
  const capture=captures.get(page)?.get(turnId);
  expect(capture,'an active observation must name an original submitted turn').toBeTruthy();
  const observed=await readOriginal(page,capture!);
  return ['claimed','running'].includes(observed.execution.state);
}

export async function finishedInstalled(page:Page,prompt:string,turnId:string) {
  const capture=captures.get(page)?.get(turnId);
  expect(capture,'completion must name an original submitted turn').toBeTruthy();
  const deadline=Date.now()+180000;
  let observed=await readOriginal(page,capture!);
  while (observed.execution.state!=='completed' && Date.now()<deadline) {
    expect(['queued','claimed','running'],'this journey requires a completed turn; questions, approvals and recovery need their own assertions')
      .toContain(observed.execution.state);
    await page.waitForTimeout(1000);
    observed=await readOriginal(page,capture!);
  }
  expect(observed.execution.state).toBe('completed');
  expect(identifier(observed.execution.finalMessageId)).toBe(true);
  const history=await page.evaluate(async()=>(window as any).honuaAgentWorkspace.conversation());
  expect(history.messages.some((message:any)=>message.id===observed.execution.finalMessageId
    && message.turnId===turnId && message.role==='assistant'),'native completion must join its actual original final message').toBe(true);
  let projected:any;
  // Export may follow native completion. These bounded retries only read the
  // same original execution's private evidence; they cannot launch model work.
  for (let attempt=0;attempt<6;attempt++) {
    try {projected=await projectWitness(capture!.binding,capture,observed.text);break;}
    catch (error) {if(attempt===5)throw error;await page.waitForTimeout(2000);}
  }
  capture!.proof=validateProjection(projected,capture,observed.execution,capture!.binding);
  // Expose context only after the independent task-authenticated digest joins
  // it. The browser's own context read is never substituted for this proof.
  capture!.projected={context:capture!.turn.context};
  await expect(page.locator('[data-agent-workspace]').getByRole('log')).toContainText(prompt);
  await expect.poll(()=>page.evaluate(id=>(window as any).honuaAgentWorkspace.executions.current()
    .find((row:any)=>row.turnId===id)?.execution?.state,turnId)).toBe('completed');
  return capture!;
}

export function executionEvidence(page:Page) {
  return [...(captures.get(page)?.values() ?? [])].map(capture=>{
    expect(capture.proof,'every submitted turn must have independently verified worker evidence').toBeTruthy();
    return {workspaceId:capture.workspaceId,turnId:capture.turnId,executionId:capture.executionId,
      requestDigest:capture.requestDigest,reference:capture.proof.reference,
      journalDigest:capture.proof.witness.journalDigest,finalMessageId:capture.proof.witness.native.finalMessageId,
      contextDigest:capture.proof.witness.worker.contextDigest,releaseSource:capture.binding.release.commit};
  });
}
