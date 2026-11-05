import {test,expect} from '@playwright/test';

for(const width of [1440,390]) test(`workspace setup and conversation remain usable at ${width}px`,async({page},testInfo)=>{
  await page.setViewportSize({width,height:900});
  await page.goto('/');
  await expect(page.locator('[data-first-run-actions]')).toBeVisible();
  await expect(page.locator('[data-start-local]')).toHaveAttribute('href','/setup/local');
  if(width===390){
    // Hydration replaces the server-rendered card. Observe the current visible
    // element and its geometry together instead of retaining a detached box.
    await expect.poll(async()=>{
      const local=await page.locator('[data-start-local]').boundingBox();
      return local && local.width>0 && local.height>0 ? local.y+local.height : Number.POSITIVE_INFINITY;
    }).toBeLessThanOrEqual(900);
  }
  await expect(page.locator('[data-connect-existing]')).toHaveAttribute('href','/environments/new');
  await expect(page.locator('.console-home-operations')).not.toHaveAttribute('open','');
  await page.waitForFunction(()=>Boolean((window as any).honuaAgentWorkspace));
  if(width===390){
    const primary=page.getByRole('navigation',{name:'Primary',exact:true});
    await expect(primary).toBeHidden();
    const toggle=page.getByRole('button',{name:'Toggle navigation'});
    await toggle.focus();await page.keyboard.press('Enter');
    await expect(primary).toBeVisible();
    await page.keyboard.press('Enter');await expect(primary).toBeHidden();
  }
  const conversation=page.locator('[data-agent-workspace]');
  await expect(conversation.locator('.console-conversation-connection')).not.toHaveAttribute('open','');
  await expect(conversation.locator('.console-conversation-handoff')).not.toHaveAttribute('open','');
  await expect(conversation).not.toHaveAttribute('open','');
  await page.getByRole('button',{name:'Conversation',exact:true}).click();
  await expect(conversation.locator('[data-workspace-prompt]')).toBeVisible();
  const geometry=await page.evaluate(()=>{
    const main=document.querySelector('main')!.getBoundingClientRect();
    const chat=document.querySelector('[data-agent-workspace]')!.getBoundingClientRect();
    return {overlap:main.left<chat.right&&main.right>chat.left&&main.top<chat.bottom&&main.bottom>chat.top,scroll:document.documentElement.scrollWidth,width:innerWidth};
  });
  expect(geometry.overlap).toBe(width===390);expect(geometry.scroll).toBeLessThanOrEqual(geometry.width+1);
  if(width===390) {
    await expect(conversation).toHaveAttribute('aria-modal','true');
    expect(await page.locator('.console-shell').evaluate(node=>(node as HTMLElement).inert)).toBe(true);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button',{name:'Conversation',exact:true})).toBeFocused();
    await expect(conversation).not.toHaveAttribute('open','');
    expect(await page.locator('.console-shell').evaluate(node=>(node as HTMLElement).inert)).toBe(false);
  }
  await testInfo.attach(`workspace-${width}.png`,{body:await page.screenshot({fullPage:true}),contentType:'image/png'});
});

test('Add Data has contextual controls and no competing AI composer',async({page})=>{
  await page.goto('/operate/data/new');
  await expect(page.locator('[data-data-publish-flow]')).toBeVisible();
  await expect(page.locator('[data-workspace-open]')).toBeVisible();
  await expect(page.locator('[data-driver], [data-ai-driver], [data-intake-mode="Ai"]')).toHaveCount(0);
});

test('failed resource reads show one recovery state and no false empty tree',async({page})=>{
  await page.goto('/operate/data');
  await expect(page.locator('[data-connection-notice]')).toHaveCount(1);
  await expect(page.locator('[data-connection-notice]')).toBeVisible();
  await expect(page.locator('[data-tree-empty]')).toHaveCount(0);
  await expect(page.locator('[data-connection-notice] a')).toHaveAttribute('href','/environments');
});
