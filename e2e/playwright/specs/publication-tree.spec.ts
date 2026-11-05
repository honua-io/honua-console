import {test,expect,type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';

async function openFixture(page: Page) {
  await page.route('**/publication-tree-fixture',route=>route.fulfill({contentType:'text/html',
    body:readFileSync(new URL('../fixtures/publication-tree.html',import.meta.url),'utf8')}));
  await page.goto('/publication-tree-fixture');
}

test('tree arrows move focus, lazy right enters children, and Enter alone opens links',async({page})=>{
  await openFixture(page);
  const root=page.getByRole('treeitem',{name:'Services',exact:true});
  const alpha=page.getByRole('treeitem',{name:'Alpha',exact:true});
  await root.focus();
  await page.keyboard.press('ArrowDown');
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

test('Alt+Left retains browser history behavior while the tree has focus',async({page})=>{
  await page.goto('/version.json');
  await openFixture(page);
  await page.getByRole('treeitem',{name:'Alpha',exact:true}).focus();
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).toHaveURL(/\/version.json$/);
});

test('sibling expansion preserves focus on the originating row',async({page})=>{
  await openFixture(page);
  await page.getByRole('treeitem',{name:'Beta',exact:true}).evaluate(row=>{
    row.setAttribute('aria-expanded','false');
    const button=document.createElement('button');
    button.dataset.treeExpand=''; button.tabIndex=-1; button.textContent='Expand';
    row.querySelector('.console-tree-row')!.prepend(button);
  });
  const alpha=page.getByRole('treeitem',{name:'Alpha',exact:true});
  await alpha.focus();
  await page.keyboard.press('*');
  await expect(alpha).toHaveAttribute('aria-expanded','true');
  await expect(page.getByRole('treeitem',{name:'Beta',exact:true})).toHaveAttribute('aria-expanded','true');
  await expect(alpha).toBeFocused();
});
