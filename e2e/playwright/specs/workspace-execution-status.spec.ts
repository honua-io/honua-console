import {expect,test as base,type Page} from '@playwright/test';

// Real mounted Console controls with a controlled native API boundary. These
// browser checks do not substitute for installed Cognito/model qualification.
async function mountedWorkspace(page:Page) {
  await page.route(url=>url.pathname === '/operate',async route=>{
    const response=await route.fetch();const html=await response.text();
    const manual=html.replace(/<script src="([^"]*_framework\/blazor\.web\.[^"]+)"/,
      '<script autostart="false" src="$1"');
    expect(manual).not.toBe(html);await route.fulfill({response,body:manual});
  });
  await page.goto('/operate');
  const original=await page.locator('[data-agent-workspace]').elementHandle();
  await page.evaluate(()=>(window as any).Blazor.start());
  await expect.poll(()=>original!.evaluate(element=>element.isConnected)).toBe(false);
  await page.evaluate(()=>{
    const workspace=(window as any).honuaAgentWorkspace;
    const state=(window as any).executionTest={state:'queued',revision:1,denied:false,posts:0,operations:[]};
    workspace.session={environmentId:'demo',principalId:'human',environmentName:'Demo',
      releaseId:'candidate',sourcePathBase:'',expiresAt:new Date(Date.now()+600_000).toISOString()};
    workspace.workspaceId='workspace';
    const submitted={workspaceId:'workspace',targetId:'target',viewId:'original-view',revision:3,
      selectedResources:[{kind:'layer',id:'7',displayName:'Parcels'}],task:null};
    workspace.context={...submitted};
    workspace.selectionProvider=()=>workspace.context.selectedResources;
    workspace.api=async(path:string,method='GET',body?:any,_binding?:any,serialized?:string)=>{
      if (method==='POST') state.posts++;
      if (path==='session') {
        if (state.sessionStatus) {
          state.refusedReads=(state.refusedReads || 0)+1;
          throw Object.assign(new Error('Session denied'),{status:state.sessionStatus});
        }
        return {...workspace.session};
      }
      if (path.endsWith('/deploy-targets')) return [];
      if (path.endsWith('/conversation') && state.conversationDenied) throw Object.assign(new Error('Denied'),{status:403});
      if (path.endsWith('/conversation')) return {messages:[
        {role:'user',turnId:'original-turn',content:'Make a map of these parcels'},
        {role:'assistant',turnId:'original-turn',content:'All done!'}]};
      if (path==='workspace/turns' && method==='POST') {
        state.reply=body;
        if (state.refuseNext) {
          state.refuseNext=false;
          const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(serialized));
          state.refused={turnId:body.turnId,status:'not-dispatched',submission:{turnId:body.turnId,
            requestDigest:`sha256:${Array.from(new Uint8Array(digest),byte=>byte.toString(16).padStart(2,'0')).join('')}`,
            disposition:'not-dispatched',code:'context-changed'}};
          throw Object.assign(new Error('Current context changed'),{status:409});
        }
        state.replyTurn={turnId:body.turnId,context:workspace.context,status:'accepted',execution:{
          schemaVersion:1,executionId:'reply-execution',state:'queued',stateRevision:1,
          createdAt:'2026-10-23T10:02:00Z',updatedAt:'2026-10-23T10:02:00Z',operations:[],artifacts:[]}};
        return state.replyTurn;
      }
      const later=state.history?.find((entry:any)=>path.endsWith(`/turns/${entry.turnId}`));
      if (later) return later;
      if (state.refused && path.endsWith(`/turns/${state.refused.turnId}`)) return state.refused;
      if (state.replyTurn && path.endsWith(`/turns/${state.replyTurn.turnId}`)) return state.replyTurn;
      if (path.endsWith('/turns/original-turn')) {
        if (state.denied) throw Object.assign(new Error('Denied'),{status:403});
        return {turnId:'original-turn',context:submitted,status:'accepted',execution:{
          schemaVersion:1,executionId:'original-execution',state:state.state,stateRevision:state.revision,
          createdAt:'2026-10-23T10:00:00Z',updatedAt:'2026-10-23T10:01:00Z',
          claimExpiresAt:'2000-01-01T00:00:00Z',operations:state.operations,artifacts:[],
          ...(state.question ? {question:state.question} : {})}};
      }
      if (path.endsWith('/context') && method==='PUT')
        return {...workspace.context,...body,revision:workspace.context.revision+1};
      if (path.startsWith('workspace?viewId=')) {
        const status=state.contextStatus;
        if (state.contextGate) {
          state.contextStarted=true;
          await state.contextGate;
        }
        if (status) {
          state.refusedReads=(state.refusedReads || 0)+1;
          throw Object.assign(new Error('Context denied'),{status});
        }
        return workspace.context;
      }
      throw new Error(`Unexpected request: ${method} ${path}`);
    };
    workspace.notify(workspace);
  });
  await page.locator('[data-agent-workspace]').evaluate((element:any)=>{element.open=true;});
  await page.locator('[data-workspace-check-executions]').click();
}

// Cold Blazor hydration is fixture setup; preserve the interaction test's
// separate 30-second budget and each assertion's existing timeout.
const test=base.extend<{workspace:Page}>({
  workspace:[async({page},use)=>{
    await mountedWorkspace(page);
    await use(page);
  },{timeout:60_000}],
});

test('native progress remains distinct from prose and preserves keyboard focus through approval updates',async({workspace:page})=>{
  const progress=page.locator('[data-workspace-executions]');
  await expect(progress).toContainText('Waiting for agent');
  await expect(progress).toContainText('Parcels');
  await page.evaluate(()=>{
    (window as any).executionTest.state='running';(window as any).executionTest.revision++;
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(progress).toContainText('Working');
  await expect(progress).not.toContainText('Completed');
  await expect(page.locator('[data-workspace-messages]')).toContainText('All done!');
  await page.evaluate(async()=>{
    const state=(window as any).executionTest;state.state='awaiting-approval';state.revision++;
    state.operations=[{kind:'operation-proposal',id:'operation',operationId:'studio.item.publish',
      proposalId:'proposal-1',status:'RequiresApproval',href:'https://untrusted.example'}];
    await (window as any).honuaAgentWorkspace.select([{kind:'layer',id:'8',displayName:'Different layer'}]);
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(progress).toContainText('Awaiting approval from another person');
  await expect(progress).toContainText('Parcels');
  const link=progress.getByRole('link',{name:'Open approval'});
  await expect(link).toHaveAttribute('href','/inbox?proposalId=proposal-1');
  await link.focus();
  await page.evaluate(()=>{
    (window as any).executionTest.revision++;
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(link).toBeFocused();
  await expect.poll(()=>page.evaluate(()=>(window as any).executionTest.posts)).toBe(0);
  await expect(progress.locator('a[href^="https:"]')).toHaveCount(0);
});

test('losing access removes private links without displaying prior completion',async({workspace:page})=>{
  await page.evaluate(()=>{
    const state=(window as any).executionTest;state.state='awaiting-approval';state.revision++;
    state.operations=[{kind:'operation-proposal',id:'operation',proposalId:'proposal-1',status:'RequiresApproval'}];
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  const progress=page.locator('[data-workspace-executions]');
  await expect(progress.getByRole('link',{name:'Open approval'})).toBeVisible();
  await page.evaluate(()=>{
    (window as any).executionTest.denied=true;
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(progress).toContainText('cannot currently be read');
  await expect(progress.getByRole('link')).toHaveCount(0);
  await expect(progress).not.toContainText('Completed');
  await expect(progress).not.toContainText('Parcels');
});

test('conversation access loss clears previous messages and result links',async({workspace:page})=>{
  await page.evaluate(()=>{
    const state=(window as any).executionTest;state.state='awaiting-approval';state.revision++;
    state.operations=[{kind:'operation-proposal',id:'operation',proposalId:'proposal-1',status:'RequiresApproval'}];
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  const progress=page.locator('[data-workspace-executions]');
  await expect(progress.getByRole('link',{name:'Open approval'})).toBeVisible();
  await page.evaluate(()=>{
    (window as any).executionTest.conversationDenied=true;
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(progress).toContainText('Reconnect');
  await expect(progress.getByRole('link')).toHaveCount(0);
  await expect(page.locator('[data-workspace-messages]')).toBeEmpty();
});

async function askQuestion(page:Page) {
  await page.evaluate(()=>{
    const state=(window as any).executionTest;state.state='awaiting-input';state.revision++;
    state.question={id:'question-1',providerRequestId:'provider-request',providerToolCallId:'tool-call',
      text:'Which parcels should the map show?',contextDigest:'a'.repeat(64)};
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(page.locator('[data-workspace-executions]')).toContainText('Awaiting your answer');
}

test('a typed clarification stays attached to its original target through navigation and explicit reply',async({workspace:page})=>{
  await askQuestion(page);
  const prompt=page.locator('[data-workspace-prompt]');await prompt.fill('Residential parcels');
  await page.getByRole('button',{name:'Reply to question',exact:true}).click();
  await expect(prompt).toHaveValue('Residential parcels');await expect(prompt).toBeFocused();
  const reply=page.getByRole('region',{name:'Reply to agent question'});
  await expect(reply).toContainText('Which parcels should the map show?');
  await expect(reply).toContainText('layer 7');
  await expect(page.getByRole('link',{name:'Open approval'})).toHaveCount(0);
  await page.evaluate(()=>(window as any).honuaAgentWorkspace.select([{kind:'layer',id:'8'}]));
  await page.getByRole('button',{name:'Send',exact:true}).click();
  await expect(page.locator('[data-workspace-status]')).toContainText('Return to the original question target');
  await expect(prompt).toHaveValue('Residential parcels');
  expect(await page.evaluate(()=>(window as any).executionTest.posts)).toBe(0);
  await page.evaluate(()=>(window as any).honuaAgentWorkspace.select([{kind:'layer',id:'7'}]));
  await page.getByRole('button',{name:'Review current target',exact:true}).click();
  await expect(page.locator('[data-workspace-status]')).toContainText('Current target reviewed');
  await page.getByRole('button',{name:'Send',exact:true}).click();
  await expect(prompt).toHaveValue('');await expect(reply).toBeHidden();
  const body=await page.evaluate(()=>(window as any).executionTest.reply);
  expect(body.replyToQuestionId).toBe('question-1');expect(body.replyToTurnId).toBe('original-turn');
  expect(body.replyToExecutionId).toBe('original-execution');expect(body.turnId).not.toBe('original-turn');
  expect(body.prompt).toBe('Residential parcels');expect(body.approved).toBeUndefined();
});

test('losing question access hides its private context and keeps the unsent answer',async({workspace:page})=>{
  await askQuestion(page);
  await page.getByRole('button',{name:'Reply to question',exact:true}).click();
  const prompt=page.locator('[data-workspace-prompt]');await prompt.fill('Keep this answer');
  await page.evaluate(()=>{
    (window as any).executionTest.denied=true;
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  const reply=page.getByRole('region',{name:'Reply to agent question'});
  await expect(reply).toContainText('cannot currently be read');
  await expect(reply).not.toContainText('Which parcels');await expect(reply).not.toContainText('layer 7');
  await expect(prompt).toHaveValue('Keep this answer');
  await page.getByRole('button',{name:'Send',exact:true}).click();
  await expect(page.locator('[data-workspace-status]')).toContainText('Denied');
  await expect(prompt).toHaveValue('Keep this answer');
  expect(await page.evaluate(()=>(window as any).executionTest.posts)).toBe(0);
});

test('a verified native refusal keeps the typed answer and permits one explicit corrected submission',async({workspace:page})=>{
  await askQuestion(page);
  await page.getByRole('button',{name:'Reply to question',exact:true}).click();
  const prompt=page.locator('[data-workspace-prompt]');await prompt.fill('Residential parcels');
  await page.evaluate(()=>{(window as any).executionTest.refuseNext=true;});
  await page.getByRole('button',{name:'Send',exact:true}).click();
  await expect(page.locator('[data-workspace-status]')).toContainText('Review the current selection');
  await expect(page.locator('[data-workspace-executions]')).toContainText('Your request was not started');
  await expect(prompt).toHaveValue('Residential parcels');
  await expect(page.getByRole('region',{name:'Reply to agent question'})).toBeVisible();
  expect(await page.evaluate(()=>(window as any).executionTest.posts)).toBe(1);
  await prompt.fill('Residential parcels in the reviewed selection');
  await page.getByRole('button',{name:'Send',exact:true}).click();
  await expect(prompt).toHaveValue('');
  const observations=await page.evaluate(()=>({posts:(window as any).executionTest.posts,
    refused:(window as any).executionTest.refused.turnId,accepted:(window as any).executionTest.reply.turnId,
    originalRows:(window as any).honuaAgentWorkspace.executions.current().map((row:any)=>row.turnId)}));
  expect(observations.posts).toBe(2);expect(observations.accepted).not.toBe(observations.refused);
  expect(observations.originalRows).toContain(observations.refused);
});


test('an older question remains reachable after eleven completed turns and a history reload',async({workspace:page})=>{
  await askQuestion(page);
  await page.evaluate(()=>{
    const workspace=(window as any).honuaAgentWorkspace,state=(window as any).executionTest;
    state.history=Array.from({length:11},(_,i)=>({turnId:`later-${i}`,status:'accepted',
      context:{...workspace.context,selectedResources:[]},execution:{schemaVersion:1,executionId:`later-execution-${i}`,
        state:'completed',stateRevision:1,createdAt:'2026-10-23T10:02:00Z',updatedAt:'2026-10-23T10:02:00Z',operations:[],artifacts:[]}}));
    for (const turn of state.history) workspace.executions.adopt(workspace.executions.begin(turn.turnId),turn);
    workspace.notify(workspace);
  });
  const progress=page.locator('[data-workspace-executions]');
  const answer=progress.getByRole('button',{name:'Reply to question',exact:true});
  await expect(answer).toBeVisible();
  await expect(progress.getByRole('region',{name:'Agent request',exact:true})).toHaveCount(11);
  await page.evaluate(()=>{
    const workspace=(window as any).honuaAgentWorkspace;
    workspace.executions.scopes.clear();
    workspace.notify(workspace);
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(answer).toBeVisible();
  const prompt=page.locator('[data-workspace-prompt]');await prompt.fill('Keep this answer for the original question');
  await answer.click();
  await expect(page.getByRole('region',{name:'Reply to agent question'})).toContainText('Which parcels should the map show?');
  await expect(prompt).toHaveValue('Keep this answer for the original question');
  await expect(prompt).toBeFocused();
  expect(await page.evaluate(()=>(window as any).honuaAgentWorkspace.questionReplies.current().turnId)).toBe('original-turn');
  expect(await page.evaluate(()=>(window as any).executionTest.posts)).toBe(0);
  await page.evaluate(()=>{
    (window as any).executionTest.denied=true;
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect(answer).toHaveCount(0);
  await expect(progress).not.toContainText('Which parcels');
  await expect(prompt).toHaveValue('Keep this answer for the original question');
});


for (const refusal of [
  {source:'context',status:401},
  {source:'context',status:403},
  {source:'context',status:404},
  {source:'session renewal',status:401}
]) test('automatic polling clears private execution data after '+refusal.source+' '+refusal.status,async({workspace:page})=>{
  await page.evaluate(()=>{
    (window as any).executionTest.operations=[
      {kind:'operation-proposal',id:'operation',proposalId:'proposal-1',status:'RequiresApproval'}
    ];
  });
  await askQuestion(page);
  const progress=page.locator('[data-workspace-executions]');
  await expect(progress.getByRole('link',{name:'Open approval'})).toBeVisible();
  await expect(progress).toContainText('Parcels');
  await page.getByRole('button',{name:'Reply to question',exact:true}).click();
  const prompt=page.locator('[data-workspace-prompt]');await prompt.fill('Residential parcels');
  await expect(page.locator('[data-workspace-reply-description]')).toContainText('Which parcels');
  await page.evaluate(refusal=>{
    const workspace=(window as any).honuaAgentWorkspace;
    const state=(window as any).executionTest;
    window.addEventListener('honua-workspace-conversation',(event:any)=>{state.projectedMessages=event.detail;});
    if (refusal.source==='context') state.contextStatus=refusal.status;
    else {
      state.sessionStatus=refusal.status;
      workspace.session.expiresAt=new Date(Date.now()-1000).toISOString();
    }
  },refusal);
  // No manual refresh: the mounted scheduler must notice revocation itself.
  await expect.poll(()=>page.evaluate(()=>(window as any).executionTest.refusedReads || 0)).toBeGreaterThan(0);
  await expect(progress).toContainText('Reconnect');
  await expect(progress.getByRole('link')).toHaveCount(0);
  await expect(progress).not.toContainText('Parcels');
  await expect(progress).not.toContainText('Which parcels');
  await expect(progress.getByRole('button',{name:'Reply to question',exact:true})).toHaveCount(0);
  await expect(page.locator('[data-workspace-messages]')).toBeEmpty();
  await expect(page.locator('[data-workspace-reply-description]')).not.toContainText('Which parcels');
  await expect(page.locator('[data-workspace-reply-description]')).toContainText('typed answer is kept');
  await expect(prompt).toHaveValue('Residential parcels');
  expect(await page.evaluate(()=>(window as any).executionTest.projectedMessages)).toEqual([]);
  expect(await page.evaluate(()=>(window as any).executionTest.posts)).toBe(0);
});

test('a refused poll from the previous authority preserves the new authorized observation',async({workspace:page})=>{
  await askQuestion(page);
  await page.evaluate(()=>{
    const state=(window as any).executionTest;
    state.contextStatus=403;
    state.contextGate=new Promise<void>(resolve=>{state.releaseContext=resolve;});
  });
  await expect.poll(()=>page.evaluate(()=>(window as any).executionTest.contextStarted)).toBe(true);
  await page.evaluate(()=>{
    const workspace=(window as any).honuaAgentWorkspace;
    workspace.session={...workspace.session,principalId:'another-authorized-human'};
    document.querySelector<HTMLButtonElement>('[data-workspace-check-executions]')!.click();
  });
  await expect.poll(()=>page.evaluate(()=>{
    const workspace=(window as any).honuaAgentWorkspace;
    return workspace.executions.current()[0]?.execution?.question?.text;
  })).toBe('Which parcels should the map show?');
  await page.evaluate(()=>{
    const state=(window as any).executionTest;
    state.contextStatus=undefined;state.contextGate=undefined;state.releaseContext();
  });
  await expect.poll(()=>page.evaluate(()=>(window as any).executionTest.refusedReads || 0)).toBe(1);
  await expect(page.locator('[data-workspace-executions]')).toContainText('Which parcels');
  await expect(page.locator('[data-workspace-messages]')).toContainText('Make a map');
  expect(await page.evaluate(()=>(window as any).honuaAgentWorkspace.executions.current()[0].error)).toBeNull();
});
