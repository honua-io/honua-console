import {test,expect,type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';
import AxeBuilder from '@axe-core/playwright';
import {readConversationMarkup} from '../../../scripts/lib/conversation-markup.mjs';

async function openFixture(page:Page,path='/') {
  let html=readFileSync(new URL('../fixtures/publication-tree.html',import.meta.url),'utf8');
  html=html.replace('<header class="console-topbar">',`<span hidden data-console-location="${path}" data-console-authority-revision="0"></span><header class="console-topbar"><button data-conversation-toggle aria-controls="console-conversation-drawer" aria-expanded="false">Conversation</button>`);
  html=html.replace('<script src="/js/modal-surface.js">',`${readConversationMarkup()}<script src="/js/modal-surface.js">`);
  html=html.replace('</body>','<script src="/js/conversation-drawer.js"></script></body>');
  await page.route('**/drawer-fixture',route=>route.fulfill({contentType:'text/html',body:html}));
  await page.goto('/drawer-fixture');
}
async function commit(page:Page,path:string) {
  await page.locator('[data-console-location]').evaluate((marker,path)=>{
    marker.setAttribute('data-console-location',path);
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
  await commit(page,'/operate/layers/connection-one-layer-1');
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
  if(width===1440) await page.getByRole('link',{name:'Main action',exact:true}).focus();
  await page.setViewportSize({width:width===390?1440:390,height:900});
  if(width===1440) await expect(page.locator('[data-workspace-prompt]')).toBeFocused();
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(width===1440);
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  expect(await page.locator('.console-shell').evaluate((node:HTMLElement)=>node.inert)).toBe(false);
  await expect(toggle(page)).toBeFocused();
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

async function installPreferences(page:Page) {
  await page.evaluate(async()=>{
    const {mountWorkspacePreferences}=await import('/js/operator-preferences.mjs');
    const state:any={pending:[],authority:'original'};
    state.session=(environmentId:string)=>({authenticated:true,principalId:'opaque-operator',environmentId,
      expiresAt:new Date(Date.now()+60000).toISOString(),releaseId:'release',sourcePathBase:''});
    state.workspace={follow:true,session:state.session('original-environment'),authorityKey:()=>state.authority,
      api:()=>new Promise((resolve,reject)=>state.pending.push({resolve,reject}))};
    localStorage.setItem('honua.shell.preferences.v1:opaque-operator:original-environment',JSON.stringify({authority:JSON.stringify(['release','']),follow:false}));
    state.sync=mountWorkspacePreferences(state.workspace,document.querySelector('[data-agent-workspace]'),new AbortController().signal);
    (window as any).preferenceTest=state;
    state.sync();
  });
  await expect(page.locator('[data-workspace-follow]')).not.toBeChecked();
  await expect(page.locator('[data-workspace-follow]')).toBeEnabled();
}
async function expectFollowOff(page:Page) {
  await expect(page.locator('[data-workspace-follow]')).not.toBeChecked();
  expect(await page.evaluate(()=>(window as any).preferenceTest.workspace.follow)).toBe(false);
}

test('pending and superseded preference validation never turns the operator follow choice on',async({page})=>{
  await openFixture(page);await installPreferences(page);
  await page.evaluate(()=>{
    localStorage.setItem('honua.shell.preferences.v1:opaque-operator:new-environment',JSON.stringify({authority:JSON.stringify(['release','']),follow:false}));
    window.dispatchEvent(new CustomEvent('honua-console-navigation'));
    window.dispatchEvent(new CustomEvent('honua-console-navigation'));
  });
  await expectFollowOff(page);await expect(page.locator('[data-workspace-follow]')).toBeDisabled();
  await page.evaluate(()=>{const state=(window as any).preferenceTest;state.pending[1].resolve(state.session('new-environment'));});
  await expect(page.locator('[data-workspace-follow]')).toBeEnabled();
  await page.evaluate(()=>{const state=(window as any).preferenceTest;state.pending[0].resolve(state.session('old-environment'));});
  await expectFollowOff(page);
});

test('failed and invalid same-authority validation keep follow off',async({page})=>{
  await openFixture(page);await installPreferences(page);
  await page.evaluate(()=>window.dispatchEvent(new CustomEvent('honua-console-navigation')));
  await expectFollowOff(page);
  await page.evaluate(()=>(window as any).preferenceTest.pending[0].reject(new Error('Unavailable')));
  await expect(page.locator('[data-workspace-follow]')).toBeDisabled();await expectFollowOff(page);
  await page.evaluate(()=>{
    window.dispatchEvent(new CustomEvent('honua-console-navigation'));
    const state=(window as any).preferenceTest;state.pending[1].resolve({...state.session('original-environment'),authenticated:false});
  });
  await expectFollowOff(page);
});

test('same-URL authority refresh changes only the validated preference partition',async({page})=>{
  await openFixture(page,'/studio');await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  await installPreferences(page);
  await page.locator('[data-console-location]').evaluate(marker=>marker.setAttribute('data-console-authority-revision','1'));
  await expect(page.locator('[data-workspace-follow]')).toBeDisabled();await expectFollowOff(page);
  await page.evaluate(()=>{const state=(window as any).preferenceTest;state.pending[0].resolve(state.session('new-environment'));});
  await expect(page.locator('[data-workspace-follow]')).toBeChecked();
  await expect(drawer(page)).not.toHaveAttribute('open','');
  await toggle(page).click();await page.locator('[data-workspace-follow]').uncheck();
  const record=await page.evaluate(()=>JSON.parse(localStorage.getItem('honua.shell.preferences.v1:opaque-operator:new-environment')!));
  expect(record.follow).toBe(false);
});

test('a validated same-authority lease renews preferences without redundant session reads',async({page})=>{
  await page.clock.install();await openFixture(page);await installPreferences(page);
  await page.clock.fastForward(61000);
  await page.evaluate(()=>{
    const state=(window as any).preferenceTest;
    state.workspace.session=state.session('original-environment');state.sync();
  });
  await expect(page.locator('[data-workspace-follow]')).toBeEnabled();await expectFollowOff(page);
  expect(await page.evaluate(()=>(window as any).preferenceTest.pending.length)).toBe(0);
  await toggle(page).click();await page.locator('[data-workspace-follow]').check();
  await expect(page.locator('[data-workspace-follow]')).toBeEnabled();
  const record=await page.evaluate(()=>JSON.parse(localStorage.getItem('honua.shell.preferences.v1:opaque-operator:original-environment')!));
  expect(record.follow).toBe(true);
});

test('a pending validation cannot replace a newly adopted workspace authority',async({page})=>{
  await openFixture(page);await installPreferences(page);
  await page.evaluate(()=>{
    window.dispatchEvent(new CustomEvent('honua-console-navigation'));
    const state=(window as any).preferenceTest;state.authority='new-workspace';
    state.workspace.session=state.session('new-environment');state.sync();
  });
  await expect(page.locator('[data-workspace-follow]')).toBeChecked();
  await page.evaluate(()=>{const state=(window as any).preferenceTest;state.pending[0].resolve(state.session('original-environment'));});
  await expect(page.locator('[data-workspace-follow]')).toBeChecked();
});

for(const width of [1440,390]) test(`same-path query, fragment and hydration changes preserve explicit drawer choice at ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:900});await openFixture(page,'/studio');
  await page.evaluate(()=>history.replaceState(null,'','/studio?draft=1#first'));
  await page.locator('[data-console-location]').evaluate(marker=>marker.replaceWith(marker.cloneNode(true)));
  await expect(drawer(page)).toHaveAttribute('open','');
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  await page.evaluate(()=>history.replaceState(null,'','/studio?draft=2#second'));
  await page.locator('[data-console-location]').evaluate(marker=>marker.replaceWith(marker.cloneNode(true)));
  await expect(drawer(page)).not.toHaveAttribute('open','');
  await toggle(page).click();
  await page.locator('[data-console-location]').evaluate(marker=>marker.replaceWith(marker.cloneNode(true)));
  await expect(drawer(page)).toHaveAttribute('open','');
});

for(const width of [1440,390]) test(`actual Blazor query navigation preserves drawer choice at ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:900});await page.goto('/studio?draft=1');
  await page.waitForFunction(()=>Boolean((window as any).honuaAgentWorkspace));
  await expect(drawer(page)).toHaveAttribute('open','');
  await expect(page.locator('[data-console-location]')).toHaveAttribute('data-console-location','/studio');
  await page.getByRole('button',{name:'Close conversation',exact:true}).click();
  await page.evaluate(()=>(window as any).Blazor.navigateTo('/studio?draft=2'));
  await expect(page).toHaveURL(/\/studio\?draft=2$/);await expect(drawer(page)).not.toHaveAttribute('open','');
  await toggle(page).click();
  await page.evaluate(()=>(window as any).Blazor.navigateTo('/studio?draft=3'));
  await expect(page).toHaveURL(/\/studio\?draft=3$/);await expect(drawer(page)).toHaveAttribute('open','');
});

test('persistent drawer controls retain visible keyboard outlines in forced colors',async({page})=>{
  await page.emulateMedia({forcedColors:'active'});await openFixture(page);await toggle(page).click();
  await page.keyboard.press('Tab');
  for(const selector of ['[data-workspace-prompt]','[data-workspace-deploy-target]','[data-conversation-close]']) {
    const control=page.locator(selector);await control.focus();
    const outline=await control.evaluate(element=>{
      const style=getComputedStyle(element);
      return {visible:element.matches(':focus-visible'),style:style.outlineStyle,width:parseFloat(style.outlineWidth)};
    });
    expect(outline.visible).toBe(true);expect(outline.style).not.toBe('none');expect(outline.width).toBeGreaterThanOrEqual(2);
  }
});

test('fresh embedded map documents contain no authoring host or workspace session reads',async({page})=>{
  const sessions:string[]=[];
  page.on('request',request=>{if(new URL(request.url()).pathname==='/api/agent-workspace/session')sessions.push(request.method());});
  await page.goto('/embed/maps/missing-map');
  await expect(page.locator('.console-embed-page')).toBeVisible();
  await expect(page.locator('[data-agent-workspace]')).toHaveCount(0);
  await expect(page.getByRole('navigation',{name:'Primary',exact:true})).toHaveCount(0);
  await page.waitForTimeout(1500);
  expect(await page.evaluate(()=>Boolean((window as any).honuaAgentWorkspace))).toBe(false);
  expect(sessions).toEqual([]);
});

test('leaving the operator shell suspends preference reads without changing follow choice',async({page})=>{
  await openFixture(page);await installPreferences(page);
  await page.locator('[data-console-location]').evaluate(marker=>marker.remove());
  await expect(drawer(page)).not.toHaveAttribute('open','');
  await expectFollowOff(page);
  expect(await page.evaluate(()=>(window as any).preferenceTest.pending.length)).toBe(0);
});
