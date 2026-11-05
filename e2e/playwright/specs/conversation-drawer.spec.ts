import {test,expect,type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';
import AxeBuilder from '@axe-core/playwright';

function drawerMarkup() {
  const directory=new URL('../../../src/Honua.Console.Shell/Layout/',import.meta.url);
  return readFileSync(new URL('ConsoleConversationDrawer.razor',directory),'utf8')
    .replace(/<ConsoleConversation(\w+) \/>/g,(_,name)=>readFileSync(new URL(`ConsoleConversation${name}.razor`,directory),'utf8'));
}
async function openFixture(page:Page,path='/') {
  let html=readFileSync(new URL('../fixtures/publication-tree.html',import.meta.url),'utf8');
  html=html.replace('<header class="console-topbar">',`<span hidden data-console-location="${path}" data-console-location-revision="0"></span><header class="console-topbar"><button data-conversation-toggle aria-controls="console-conversation-drawer" aria-expanded="false">Conversation</button>`);
  html=html.replace('<script src="/js/modal-surface.js">',`${drawerMarkup()}<script src="/js/modal-surface.js">`);
  html=html.replace('</body>','<script src="/js/conversation-drawer.js"></script></body>');
  await page.route('**/drawer-fixture',route=>route.fulfill({contentType:'text/html',body:html}));
  await page.goto('/drawer-fixture');
}
async function commit(page:Page,path:string) {
  await page.locator('[data-console-location]').evaluate((marker,path)=>{
    marker.setAttribute('data-console-location',path);
    marker.setAttribute('data-console-location-revision',String(Number(marker.getAttribute('data-console-location-revision'))+1));
  },path);
}
const drawer=(page:Page)=>page.locator('[data-agent-workspace]');
const toggle=(page:Page)=>page.getByRole('button',{name:'Conversation',exact:true});

for(const width of [1440,390]) test(`Studio visits own the default; manual dismissal lasts for the visit at ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:900});await openFixture(page);
  await expect(drawer(page)).not.toHaveAttribute('open','');
  await commit(page,'/studio');await expect(drawer(page)).toHaveAttribute('open','');
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  await commit(page,'/studio/drafts');await expect(drawer(page)).not.toHaveAttribute('open','');
  await commit(page,'/operate/data');await expect(drawer(page)).not.toHaveAttribute('open','');
  await commit(page,'/studio/apps/map-one/preview');await expect(drawer(page)).toHaveAttribute('open','');
  await commit(page,'/maps/map-one');await expect(drawer(page)).not.toHaveAttribute('open','');
});

test('direct Studio entry opens once and hydration does not reopen a dismissed drawer',async({page})=>{
  await openFixture(page,'/studio');await expect(drawer(page)).toHaveAttribute('open','');
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  await page.locator('[data-console-location]').evaluate(marker=>marker.replaceWith(marker.cloneNode(true)));
  await expect(drawer(page)).not.toHaveAttribute('open','');
  await commit(page,'/studio/proof');await expect(drawer(page)).not.toHaveAttribute('open','');
});

test('one host retains messages, prompt, pending reply and file selection across route replacement',async({page})=>{
  await openFixture(page);await toggle(page).click();
  await drawer(page).evaluate(host=>{
    (window as any).originalDrawer=host;
    host.querySelector('[data-workspace-messages]')!.textContent='Pending original-context turn';
    host.querySelector('[data-workspace-reply]')!.removeAttribute('hidden');
    const file=document.createElement('input');file.type='file';file.setAttribute('data-lifetime-file','');host.append(file);
  });
  await page.locator('[data-lifetime-file]').setInputFiles({name:'selected.csv',mimeType:'text/csv',buffer:Buffer.from('name\nA')});
  await page.locator('[data-workspace-prompt]').fill('Unsent original-context text');
  await commit(page,'/operate/layers/connection-one/service-one/1');
  await toggle(page).click();
  expect(await drawer(page).evaluate(host=>host===(window as any).originalDrawer)).toBe(true);
  await expect(page.locator('[data-workspace-prompt]')).toHaveValue('Unsent original-context text');
  await expect(page.locator('[data-workspace-messages]')).toHaveText('Pending original-context turn');
  await expect(page.locator('[data-workspace-reply]')).toBeVisible();
  expect(await page.locator('[data-lifetime-file]').evaluate((input:HTMLInputElement)=>input.files?.[0].name)).toBe('selected.csv');
});

test('mobile drawer traps focus, returns it, and yields to the tree without leaking inert state',async({page})=>{
  await page.setViewportSize({width:390,height:900});await openFixture(page);await toggle(page).click();
  await expect(drawer(page)).toHaveAttribute('role','dialog');
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(true);
  const close=page.getByRole('button',{name:'Close conversation',exact:true});
  await close.focus();await page.keyboard.press('Shift+Tab');
  expect(await drawer(page).evaluate(host=>host.contains(document.activeElement))).toBe(true);
  await page.keyboard.press('Tab');await expect(close).toBeFocused();
  await page.keyboard.press('Escape');await expect(toggle(page)).toBeFocused();
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(false);
  await page.getByRole('button',{name:'Browse services',exact:true}).click();
  await page.locator('[data-conversation-toggle]').evaluate((button:HTMLButtonElement)=>button.click());
  await expect(page.locator('.console-publication-sidebar')).toBeHidden();
  await expect(drawer(page)).toHaveAttribute('aria-modal','true');
  await commit(page,'/operate/data');await expect(drawer(page)).not.toHaveAttribute('open','');
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(false);
});

for(const width of [1440,390]) test(`drawer reflows and has no serious accessibility findings at ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:900});await openFixture(page);await toggle(page).click();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  const scan=await new AxeBuilder({page}).include('#console-conversation-drawer').withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();
  expect(scan.violations.filter(item=>item.impact==='serious'||item.impact==='critical')).toEqual([]);
  await page.setViewportSize({width:width===390?1440:390,height:900});
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(width===1440);
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(false);
});

test('actual Blazor navigation preserves the same host and unsent draft',async({page})=>{
  await page.goto('/');await page.waitForFunction(()=>Boolean((window as any).honuaAgentWorkspace));
  await toggle(page).click();await page.locator('[data-workspace-prompt]').fill('Keep this draft');
  await drawer(page).evaluate(host=>{(window as any).originalDrawer=host;});
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  await page.locator('[data-start-local]').click();await expect(page).toHaveURL(/\/setup\/local$/);
  await toggle(page).click();
  expect(await drawer(page).evaluate(host=>host===(window as any).originalDrawer)).toBe(true);
  await expect(page.locator('[data-workspace-prompt]')).toHaveValue('Keep this draft');
});
