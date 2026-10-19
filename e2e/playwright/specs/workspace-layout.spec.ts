import {test,expect} from '@playwright/test';

for(const width of [1440,390]) test(`workspace setup and conversation remain usable at ${width}px`,async({page},testInfo)=>{
  await page.setViewportSize({width,height:900});
  await page.goto('/');
  await expect(page.locator('[data-first-run-actions]')).toBeVisible();
  await expect(page.locator('[data-start-local]')).toHaveAttribute('href','/setup/local');
  if(width===390){
    const local=await page.locator('[data-start-local]').boundingBox();
    expect(local!.y+local!.height).toBeLessThanOrEqual(900);
  }
  await expect(page.locator('[data-connect-existing]')).toHaveAttribute('href','/environments/new');
  await expect(page.locator('.console-home-operations')).not.toHaveAttribute('open','');
  await page.waitForFunction(()=>Boolean((window as any).honuaAgentWorkspace));
  if(width===390){
    const primary=page.getByRole('navigation',{name:'Primary',exact:true});
    await expect(primary).toBeHidden();
    const toggle=page.getByRole('checkbox',{name:'Toggle navigation'});
    await toggle.focus();await page.keyboard.press('Space');
    await expect(primary).toBeVisible();
    await page.keyboard.press('Space');await expect(primary).toBeHidden();
  }
  const conversation=page.locator('[data-agent-workspace]');
  await expect(conversation.locator('.console-conversation-connection')).not.toHaveAttribute('open','');
  await expect(conversation.locator('.console-conversation-handoff')).not.toHaveAttribute('open','');
  if(width===1440) await expect(conversation).toHaveAttribute('open','');
  if(!await conversation.evaluate((node:HTMLDetailsElement)=>node.open)) await conversation.locator(':scope > summary').click();
  await expect(conversation.locator('[data-workspace-prompt]')).toBeVisible();
  const geometry=await page.evaluate(()=>{
    const main=document.querySelector('main')!.getBoundingClientRect();
    const chat=document.querySelector('[data-agent-workspace]')!.getBoundingClientRect();
    return {overlap:main.left<chat.right&&main.right>chat.left&&main.top<chat.bottom&&main.bottom>chat.top,scroll:document.documentElement.scrollWidth,width:innerWidth};
  });
  expect(geometry.overlap).toBe(false);expect(geometry.scroll).toBeLessThanOrEqual(geometry.width+1);
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
