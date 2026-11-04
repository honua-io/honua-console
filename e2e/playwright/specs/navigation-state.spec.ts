import {test,expect,type Page} from '@playwright/test';

async function openNavigationFixture(page: Page) {
  await page.route('**/navigation-fixture', route => route.fulfill({contentType:'text/html',body:`
    <!doctype html><html lang="en"><head><title>Navigation controller fixture</title></head><body>
    <header class="console-topbar"><button data-navigation-toggle aria-expanded="false">Navigation</button>
    <details data-navigation-menu="Test"><summary>Test</summary><div class="console-navigation-items" role="group">
    <a href="#first">First</a><a href="#second">Second</a></div></details></header>
    <h1 tabindex="-1">Fixture page</h1><dialog><button>Dialog action</button></dialog>
    <script src="/js/navigation-state.js"></script></body></html>`}));
  await page.goto('/navigation-fixture');
  await page.locator('summary').press('Enter');
  await expect(page.getByRole('link',{name:'First'})).toBeFocused();
}

async function replaceNavigation(page: Page) {
  await page.locator('details').evaluate(previous => previous.replaceWith(previous.cloneNode(true)));
  await expect(page.getByRole('link',{name:'First'})).toBeFocused();
}

for(const input of ['pointer','focus']) test(`later ${input} input cancels pending startup focus restoration`,async({page})=>{
  await openNavigationFixture(page);
  await replaceNavigation(page);
  const second=page.getByRole('link',{name:'Second'});
  if(input==='pointer') await second.dispatchEvent('pointerdown');
  await second.focus();
  await page.locator('h1').focus();
  await expect(page.locator('h1')).toBeFocused();
});

test('replacement without the previously focused item keeps current menu controls usable',async({page})=>{
  const errors:string[]=[];
  page.on('pageerror',error=>errors.push(error.message));
  await openNavigationFixture(page);
  await page.getByRole('link',{name:'Second'}).focus();
  await page.locator('details').evaluate(previous=>{
    const replacement=previous.cloneNode(true) as HTMLElement;
    replacement.querySelectorAll('a')[1].remove();
    previous.replaceWith(replacement);
  });
  await page.locator('summary').focus();
  await page.keyboard.press('Escape');
  await expect(page.locator('details')).not.toHaveAttribute('open','');
  await page.locator('summary').press('Enter');
  await expect(page.getByRole('link',{name:'First'})).toBeFocused();
  expect(errors).toEqual([]);
});

test('Escape in a dialog preserves its focus and leaves navigation handling to the dialog',async({page})=>{
  await openNavigationFixture(page);
  await page.locator('[data-navigation-toggle]').click();
  await page.locator('dialog').evaluate((dialog:HTMLDialogElement)=>dialog.showModal());
  await expect(page.getByRole('button',{name:'Dialog action'})).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.locator('dialog')).not.toHaveAttribute('open','');
  await expect(page.locator('[data-navigation-toggle]')).toHaveAttribute('aria-expanded','true');
});
