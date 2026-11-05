import {test,expect,type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';
import AxeBuilder from '@axe-core/playwright';

async function openFixture(page: Page) {
  await page.route('**/publication-tree-fixture',route=>route.fulfill({contentType:'text/html',
    body:readFileSync(new URL('../fixtures/publication-tree.html',import.meta.url),'utf8')}));
  await page.goto('/publication-tree-fixture');
}

async function addLongTree(page: Page) {
  return page.getByRole('tree').evaluate(tree=>{
    const group=tree.querySelector('[role="group"]')!;
    const template=group.lastElementChild!;
    for(let index=0;index<100;index++) {
      const wrapper=template.cloneNode(true) as HTMLElement;
      const row=wrapper.querySelector('[role="treeitem"]') as HTMLElement;
      row.dataset.publicationKey=`service:long-${index}`;
      row.dataset.treeLabel=`Long service ${index}`;
      row.setAttribute('aria-label',`Long service ${index}`);
      row.querySelector('a')!.textContent=`Long service ${index}`;
      row.querySelector('a')!.setAttribute('href',`#long-${index}`);
      group.append(wrapper);
    }
    [...group.children].forEach((wrapper,index)=>{
      const row=wrapper.querySelector('[role="treeitem"]')!;
      row.setAttribute('aria-posinset',String(index+1));row.setAttribute('aria-setsize',String(group.children.length));
    });
    const spacer=document.createElement('div');spacer.style.height='3000px';
    document.querySelector('main')!.append(spacer);
    return 'Long service 99';
  });
}

test('tree arrows move focus, lazy right enters children, and Enter alone opens links',async({page})=>{
  await openFixture(page);
  const root=page.getByRole('treeitem',{name:'Services',exact:true});
  const alpha=page.getByRole('treeitem',{name:'Alpha',exact:true});
  await root.focus();
  await page.keyboard.press('ArrowDown');
  await expect(alpha).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(alpha).toHaveAttribute('aria-expanded','true');
  await expect(alpha).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('treeitem',{name:'Bridges',exact:true})).toBeFocused();
  expect(new URL(page.url()).hash).toBe('');
  await page.keyboard.press('ArrowLeft');
  await expect(alpha).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(alpha).toHaveAttribute('aria-expanded','false');
  await page.keyboard.press('End');
  await expect(page.getByRole('treeitem',{name:'Beta',exact:true})).toBeFocused();
  await page.keyboard.press('Home');
  await expect(root).toBeFocused();
  await page.keyboard.press('b');
  await expect(page.getByRole('treeitem',{name:'Beta',exact:true})).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/#beta$/);
});

test('paging focuses newly exposed rows and roving focus survives DOM replacement',async({page})=>{
  await openFixture(page);
  await page.getByRole('button',{name:'Show more services'}).click();
  const gamma=page.getByRole('treeitem',{name:'Gamma',exact:true});
  await expect(gamma).toBeFocused();
  await gamma.evaluate(row=>row.replaceWith(row.cloneNode(true)));
  await expect(gamma).toBeFocused();
  await expect(page.locator('[role="treeitem"][tabindex="0"]')).toHaveCount(1);
  await expect(gamma).toHaveAttribute('aria-posinset','3');
  await expect(gamma).toHaveAttribute('aria-setsize','3');
});

for(const width of [320,390]) test(`mobile tree contains focus and restores it at ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:844});
  await openFixture(page);
  const opener=page.getByRole('button',{name:'Browse services'});
  await expect(page.getByRole('tree')).not.toBeVisible();
  await opener.click();
  await expect(page.getByRole('dialog',{name:'Service navigation'})).toBeVisible();
  await expect(page.getByRole('textbox',{name:'Filter services and loaded layers'})).toBeFocused();
  expect(await page.locator('.console-workspace').evaluate(element=>element.inert)).toBe(true);
  await page.keyboard.press('Shift+Tab');
  await expect(page.getByRole('button',{name:'Show more services'})).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('textbox')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(opener).toBeFocused();
  await expect(opener).toHaveAttribute('aria-expanded','false');
  expect(await page.locator('.console-workspace').evaluate(element=>element.inert)).toBe(false);
  await expect(page.getByRole('tree')).not.toBeVisible();
});

test('committed navigation and resize close the mobile overlay without leaving inert content',async({page})=>{
  await page.setViewportSize({width:390,height:844});
  await openFixture(page);
  await page.getByRole('button',{name:'Browse services'}).click();
  await page.locator('[data-publication-navigation]').evaluate(element=>element.setAttribute('data-tree-revision','1'));
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await page.locator('.console-workspace').evaluate(element=>element.inert)).toBe(false);
  await page.getByRole('button',{name:'Browse services'}).click();
  await page.setViewportSize({width:1440,height:900});
  await expect(page.getByRole('tree')).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await page.locator('.console-workspace').evaluate(element=>element.inert)).toBe(false);
});


test('outside pointer input prevents focus theft when the tree DOM is replaced',async({page})=>{
  await openFixture(page);
  await page.getByRole('treeitem',{name:'Alpha',exact:true}).focus();
  await page.getByRole('heading',{name:'Controller fixture'}).click();
  await expect(page.locator('body')).toBeFocused();
  await page.getByRole('tree').evaluate(tree=>tree.replaceWith(tree.cloneNode(true)));
  await expect(page.locator('body')).toBeFocused();
});

test('modified arrows remain unhandled and browser Back remains available',async({page})=>{
  await page.goto('/version.json');
  await openFixture(page);
  await page.getByRole('treeitem',{name:'Alpha',exact:true}).focus();
  const prevented=page.evaluate(()=>new Promise<boolean>(resolve=>{
    window.addEventListener('keydown',event=>{
      if(event.altKey && event.key==='ArrowLeft') resolve(event.defaultPrevented);
    });
  }));
  await page.keyboard.press('Alt+ArrowLeft');
  expect(await prevented).toBe(false);
  await expect(page.getByRole('treeitem',{name:'Alpha',exact:true})).toBeFocused();
  await page.goBack();
  await expect(page).toHaveURL(/\/version.json$/);
});

test('sibling expansion preserves focus on the originating row',async({page})=>{
  await openFixture(page);
  await page.getByRole('treeitem',{name:'Beta',exact:true}).evaluate(row=>{
    row.setAttribute('aria-expanded','false');
    const button=document.createElement('button');
    button.dataset.treeExpand=''; button.tabIndex=-1; button.textContent='Expand';
    row.prepend(button);
  });
  const alpha=page.getByRole('treeitem',{name:'Alpha',exact:true});
  await alpha.focus();
  await page.keyboard.press('*');
  await expect(alpha).toHaveAttribute('aria-expanded','true');
  await expect(page.getByRole('treeitem',{name:'Beta',exact:true})).toHaveAttribute('aria-expanded','true');
  await expect(alpha).toBeFocused();
});


for(const selection of ['Beta','none']) test(`returning to the tree uses ${selection==='none'?'the first node':'the selected node'}`,async({page})=>{
  await openFixture(page);
  if(selection!=='none') await page.getByRole('treeitem',{name:selection,exact:true})
    .evaluate(row=>row.setAttribute('aria-selected','true'));
  await page.getByRole('treeitem',{name:'Alpha',exact:true}).focus();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button',{name:'Show more services'})).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(page.getByRole('treeitem',{name:selection==='none'?'Services':selection,exact:true})).toBeFocused();
});

test('keyboard scrolling stays inside the sidebar',async({page})=>{
  await openFixture(page);
  const last=await addLongTree(page);
  await page.getByRole('treeitem',{name:'Services',exact:true}).focus();
  await page.keyboard.press('End');
  await expect(page.getByRole('treeitem',{name:last,exact:true})).toBeFocused();
  expect(await page.evaluate(()=>scrollY)).toBe(0);
  expect(await page.locator('.console-publication-sidebar').evaluate(sidebar=>sidebar.scrollTop)).toBeGreaterThan(0);
});

test('a newly selected node scrolls into view without changing page focus or scroll',async({page})=>{
  await openFixture(page);
  const last=await addLongTree(page);
  await page.getByRole('heading',{name:'Controller fixture'}).click();
  await page.getByRole('treeitem',{name:last,exact:true}).evaluate(row=>{
    row.setAttribute('aria-selected','true');
    row.replaceWith(row.cloneNode(true));
  });
  const selected=page.getByRole('treeitem',{name:last,exact:true});
  await expect(selected).toHaveAttribute('tabindex','0');
  expect(await page.evaluate(()=>scrollY)).toBe(0);
  await expect(page.locator('body')).toBeFocused();
  const bounds=await selected.boundingBox();
  expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(page.viewportSize()!.height);
});


test('leaving a loading branch cancels deferred focus entry',async({page})=>{
  await openFixture(page);
  const alpha=page.getByRole('treeitem',{name:'Alpha',exact:true});
  await alpha.evaluate(row=>{(row as HTMLElement).dataset.fixtureDelay='300';});
  await alpha.focus();
  await page.keyboard.press('ArrowRight');
  await expect(alpha).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('Tab');
  const more=page.getByRole('button',{name:'Show more services'});
  await expect(more).toBeFocused();
  await expect(page.getByRole('treeitem',{name:'Bridges',exact:true})).toBeVisible();
  await expect(more).toBeFocused();
});

test('row-sized focus targets retain accessible group ownership',async({page})=>{
  await openFixture(page);
  const alpha=page.getByRole('treeitem',{name:'Alpha',exact:true});
  await alpha.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('treeitem',{name:'Bridges',exact:true})).toBeVisible();
  const root=page.getByRole('treeitem',{name:'Services',exact:true});
  await expect(root).toHaveAttribute('aria-owns','fixture-service-group');
  expect((await root.boundingBox())!.height).toBeLessThan(60);
  const results=await new AxeBuilder({page}).include('[data-publication-tree]').analyze();
  expect(results.violations.filter(item=>['serious','critical'].includes(item.impact || ''))).toEqual([]);
});
