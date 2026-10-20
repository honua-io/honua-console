import { test, expect, type Page } from '@playwright/test';

type WorkspaceWindow = Window & {honuaAgentWorkspace: any; __qualificationSdk?: unknown; __qualificationTurns?: string[]};

async function session(page: Page) {
  // The shared installed-workspace config requires a real operator storage state.
  await page.goto('/');
  const response = await page.request.get('/api/agent-workspace/session');
  expect(response.status(), 'a real human server-bound session is required').toBe(200);
  const value = await response.json();
  expect(value.principalId).toBeTruthy();
  expect(value.environmentId).toBeTruthy();
  expect(value.csrfToken).toBeTruthy();
  expect(value.nativeAgentAvailable, 'native agent key must be provisioned, never skipped').toBe(true);
}

async function navigate(page: Page, path: string) {
  // Enhanced Blazor navigation preserves the host-owned SDK session, unlike page.goto/reload.
  await page.evaluate(path => (window as any).Blazor.navigateTo(path), path);
  await expect(page).toHaveURL(new RegExp(`${path.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')}$`));
}

async function conversation(page: Page) {
  return page.evaluate(async () => (window as WorkspaceWindow).honuaAgentWorkspace.conversation());
}

async function sendConsole(page: Page, prompt: string) {
  const before=await page.evaluate(()=> (window as WorkspaceWindow).__qualificationTurns?.length ?? 0);
  const drawer = page.locator('[data-agent-workspace]');
  await drawer.evaluate((node: HTMLDetailsElement) => {node.open = true;});
  await drawer.locator('[data-workspace-prompt]').fill(prompt);
  await drawer.getByRole('button', {name:'Send',exact:true}).click();
  await expect.poll(()=>page.evaluate(()=> (window as WorkspaceWindow).__qualificationTurns?.length ?? 0),{timeout:180_000}).toBe(before+1);
  expect(await page.evaluate(()=> (window as WorkspaceWindow).__qualificationTurns?.at(-1))).toBe('completed');
  await expect(drawer.locator('[data-workspace-status]')).toHaveText('Turn saved. Check operation receipts for completion.', {timeout:180_000});
}

test('primary conversation retains one real native session and draft context across views', async ({page}, testInfo) => {
  await session(page);
  await page.locator('[data-agent-workspace]').evaluate((node: HTMLDetailsElement)=>{node.open=true;});
  await page.locator('.console-conversation-connection').evaluate((node: HTMLDetailsElement)=>{node.open=true;});
  await page.locator('[data-workspace-id]').fill('');
  await page.locator('[data-workspace-connect]').click();
  await expect(page.locator('[data-workspace-status]')).toHaveText('Connected');
  const initial = await page.evaluate(()=>{
    const workspace=(window as WorkspaceWindow).honuaAgentWorkspace;
    return {workspaceId:workspace.workspaceId,viewId:workspace.viewId,targetId:workspace.context.targetId,tenantId:workspace.context.tenantId};
  });
  expect(initial.workspaceId).toBeTruthy();expect(initial.targetId).toBeTruthy();expect(initial.tenantId).toBeTruthy();

  await navigate(page,'/studio');
  const app=page.locator('honua-studio-app');await expect(app).toBeVisible();
  await expect(app.getByTestId('studio-chat-input')).toBeHidden();
  await expect(app.getByTestId('live-composition-toggle')).toBeHidden();
  await expect(app.getByTestId('app-view')).toBeHidden();
  await expect(app.locator('.app-brand')).toBeHidden();
  await expect(app.locator('.app-nav')).toBeHidden();
  await expect(page.locator('[data-workspace-prompt]')).toHaveCount(1);
  const packageKey=`workspace-browser-${Date.now().toString(36)}`;
  await page.getByRole('textbox',{name:'New map package key'}).fill(packageKey);
  await page.getByRole('button',{name:'New map',exact:true}).click();
  await expect.poll(()=>page.evaluate(()=> (window as WorkspaceWindow).honuaAgentWorkspace.context.task?.packageKey)).toBe(packageKey);
  const task=await page.evaluate(()=>structuredClone((window as WorkspaceWindow).honuaAgentWorkspace.context.task));
  expect(task.kind).toBe('studio-composition');
  // Copying references must not create a turn, save a form or retarget this tab.
  const referenceBefore=await page.evaluate(()=>{
    const workspace=(window as WorkspaceWindow).honuaAgentWorkspace;
    return {workspaceId:workspace.workspaceId,viewId:workspace.viewId,contextRevision:workspace.context.revision,targetId:workspace.context.targetId};
  });
  await page.locator('[data-workspace-copy-context]').click();
  await expect.poll(async()=>JSON.parse(await page.locator('[data-workspace-context-reference]').textContent() || '{}')).toEqual(referenceBefore);
  expect(await page.evaluate(()=> (window as WorkspaceWindow).honuaAgentWorkspace.context.task)).toEqual(task);


  await page.evaluate(()=>{
    const state=window as WorkspaceWindow;state.__qualificationTurns=[];
    const transport=state.honuaAgentWorkspace.agentSession;
    if(!transport) throw new Error('Native transport unavailable.');
    const submit=transport.submitTurn.bind(transport);
    // Observe actual outcomes; all requests and SDK execution remain unchanged.
    transport.submitTurn=async(...args:any[])=>{try {const result=await submit(...args);state.__qualificationTurns!.push(result.status);return result;} catch(error) {state.__qualificationTurns!.push('rejected');throw error;}};
  });
  const frozenResponse=page.waitForResponse(response=>new URL(response.url()).pathname.endsWith('/turns') && response.request().method()==='POST');
  const projectedResponse=page.waitForResponse(response=>new URL(response.url()).pathname==='/api/agent-workspace/agent/turn');
  const first=`Identify the current map package ${packageKey} and target. Do not call tools or change anything.`;
  await sendConsole(page,first);
  await expect.poll(async()=> (await conversation(page)).messages.filter((message:any)=>message.role==='assistant').length).toBeGreaterThan(0);
  const firstMessages=(await conversation(page)).messages;
  const firstUser=firstMessages.find((message:any)=>message.role==='user'&&message.content===first);
  expect(firstUser?.turnId).toBeTruthy();
  const captured=await (await frozenResponse).json();
  const projected=await (await projectedResponse).json();
  expect(projected.context.task).toMatchObject(task);
  expect(await page.evaluate(()=> (window as WorkspaceWindow).__qualificationTurns)).toEqual(['completed']);
  expect(captured.context.task).toMatchObject(task);
  expect(captured.context.targetId).toBe(initial.targetId);
  await page.evaluate(()=>{
    const state=window as WorkspaceWindow;state.__qualificationSdk=state.honuaAgentWorkspace.agentSession.sdkSession;
    if(!state.__qualificationSdk)throw new Error('No real native SDK session was created.');
  });

  // Only the primary composer is used, including while a Studio draft is active.
  await navigate(page,'/operate/publishing/quick');
  await expect(page.locator('[data-workspace-field="serviceName"]'),
    'The installed evaluation deployment must have a real readable data connection.').toBeVisible();
  const second=`Continue our ${packageKey} conversation. State its package key without calling tools.`;
  await sendConsole(page,second);
  await expect.poll(()=>page.evaluate(()=> (window as WorkspaceWindow).__qualificationTurns)).toEqual(['completed','completed']);
  await navigate(page,'/operate/publishing/quick');
  expect(await page.evaluate(()=>{
    const state=window as WorkspaceWindow;return state.__qualificationSdk===state.honuaAgentWorkspace.agentSession.sdkSession;
  })).toBe(true);
  await expect(page.locator('[data-workspace-messages]')).toContainText(first);
  await expect(page.locator('[data-workspace-messages]')).toContainText(second);

  // Human form edits stay dirty through a genuine authorized context observation.
  const field=page.locator('[data-workspace-field="serviceName"]');
  await field.fill(`dirty-${Date.now().toString(36)}`);const dirty=await field.inputValue();
  await page.evaluate(()=> (window as WorkspaceWindow).honuaAgentWorkspace.observeContext());
  await expect(field).toHaveValue(dirty);
  const dirtyRevision=await page.evaluate(()=> (window as WorkspaceWindow).honuaAgentWorkspace.context.revision);
  await page.locator('[data-workspace-copy-context]').click();
  await expect(page.locator('[data-workspace-status]')).toHaveText('Save your draft before copying its context.');
  await expect(field).toHaveValue(dirty);
  expect(await page.evaluate(()=> (window as WorkspaceWindow).honuaAgentWorkspace.context.revision)).toBe(dirtyRevision);

  await navigate(page,'/approvals');
  await expect(page.getByRole('heading',{name:'Approval inbox',exact:true})).toBeVisible();
  await expect(page.locator('[data-inbox-error]')).toHaveCount(0);
  await expect(page.locator('[data-awaiting-count]')).toBeVisible();
  // This benign journey creates no approval. It does not claim mutation/publication approval proof.
  await testInfo.attach('workspace-binding.json',{body:JSON.stringify({...initial,task,turnId:firstUser.turnId}),contentType:'application/json'});
});
