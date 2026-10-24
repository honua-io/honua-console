import { test, expect, type Page } from '@playwright/test';
import { installedSession, sendInstalled, finishedInstalled, executionEvidence } from '../workspace-live/execution';

type WorkspaceWindow = Window & {honuaAgentWorkspace: any; __qualificationWorkspace?: unknown};

async function session(page: Page) {
  // The shared installed-workspace config requires a real operator storage state.
  await page.goto('/');
  await installedSession(page);
}

async function navigate(page: Page, path: string) {
  // Enhanced Blazor navigation preserves the host-owned workspace, unlike page.goto/reload.
  await page.evaluate(path => (window as any).Blazor.navigateTo(path), path);
  await expect(page).toHaveURL(new RegExp(`${path.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')}$`));
}

async function conversation(page: Page) {
  return page.evaluate(async () => (window as WorkspaceWindow).honuaAgentWorkspace.conversation());
}

async function sendConsole(page: Page, prompt: string) {
  const drawer = page.locator('[data-agent-workspace]');
  if (!await drawer.evaluate((node: HTMLDetailsElement)=>node.open)) await drawer.locator(':scope > summary').click();
  const capture=await sendInstalled(page,prompt);
  await finishedInstalled(page,prompt,capture.turnId);
  return capture;
}

test('primary conversation retains one installed workspace and independently verified turns across views', async ({page}, testInfo) => {
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
    const state=window as WorkspaceWindow;
    state.__qualificationWorkspace=state.honuaAgentWorkspace;
    if(state.honuaAgentWorkspace.agentSession!==null)throw new Error('Installed execution cannot create a second SDK model session.');
  });
  const first=`Identify the current map package ${packageKey} and target. Do not call tools or change anything.`;
  const captured=await sendConsole(page,first);
  const firstMessages=(await conversation(page)).messages;
  const firstUser=firstMessages.find((message:any)=>message.role==='user'&&message.content===first);
  expect(firstUser?.turnId).toBe(captured.turnId);
  expect(captured.projected!.context.task).toMatchObject(task);
  expect(captured.turn.context.task).toMatchObject(task);
  expect(captured.turn.context.targetId).toBe(initial.targetId);

  // Only the primary composer is used, including while a Studio draft is active.
  await navigate(page,'/operate/publishing/quick');
  await expect(page.locator('[data-workspace-field="serviceName"]'),
    'The installed evaluation deployment must have a real readable data connection.').toBeVisible();
  const second='Continue our conversation. State the exact package key of the map we just discussed, without calling tools.';
  const continued=await sendConsole(page,second);
  expect(continued.workspaceId).toBe(initial.workspaceId);
  expect(continued.executionId).not.toBe(captured.executionId);
  const reply=(await conversation(page)).messages.find((message:any)=>message.id===continued.proof.witness.native.finalMessageId);
  expect(reply.content).toContain(packageKey);
  await navigate(page,'/operate/publishing/quick');
  expect(await page.evaluate(()=>{
    const state=window as WorkspaceWindow;return state.__qualificationWorkspace===state.honuaAgentWorkspace && state.honuaAgentWorkspace.agentSession===null;
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
  await testInfo.attach('workspace-binding.json',{body:JSON.stringify({...initial,task,turnId:firstUser.turnId,executions:executionEvidence(page)}),contentType:'application/json'});
});
