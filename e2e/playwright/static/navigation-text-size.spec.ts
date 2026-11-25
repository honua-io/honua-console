import { expect, test, type Page } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5189';

async function startSignedInConsole(page: Page) {
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 5189, {
    configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
    handleApi: createContractApi({ allowedOrigin: origin }),
  });
  const close = () => new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  try {
    await signInWithFixturePassword(page, origin, '/inbox');
    await page.waitForURL(url => url.pathname === '/console/inbox');
  } catch (error) {
    await close();
    throw error;
  }
  return close;
}

async function openActivity(page: Page) {
  await page.goto(`${origin}/console/operate/observability`);
  await expect(page.getByRole('form', { name: 'Operate log filters' })).toBeVisible();
  await expect(page.locator('.operate-summary-tile').first()).toBeVisible();
}

// Elements of the top bar that are drawn, with the boxes that must stay inside the window and apart.
async function topBarBoxes(page: Page) {
  return page.evaluate(() => {
    const drawn = [...document.querySelectorAll<HTMLElement>('.console-topbar > *, .console-topnav > *')]
      .filter(element => !element.matches('.console-topnav') && element.getClientRects().length > 0);
    const boxes = drawn.map(element => {
      const box = (element.querySelector(':scope > summary') ?? element).getBoundingClientRect();
      return { name: element.textContent!.trim().slice(0, 30) || element.className, left: box.left, right: box.right, top: box.top, bottom: box.bottom };
    });
    const touches = (first: typeof boxes[number], second: typeof boxes[number]) =>
      Math.min(first.right, second.right) - Math.max(first.left, second.left) > 1
      && Math.min(first.bottom, second.bottom) - Math.max(first.top, second.top) > 1;
    const overlaps = boxes.flatMap((first, index) => boxes.slice(index + 1)
      .filter(second => touches(first, second)).map(second => `${first.name} / ${second.name}`));
    return {
      overlaps,
      outside: boxes.filter(box => box.left < -1 || box.right > innerWidth + 1).map(box => box.name),
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth,
    };
  });
}

test('top navigation and Activity reflow without sideways scroll at 200% text size', async ({ page }, testInfo) => {
  const close = await startSignedInConsole(page);
  try {
    await openActivity(page);
    await page.evaluate(() => { document.documentElement.style.fontSize = '32px'; });
    const toggle = page.getByRole('button', { name: 'Toggle navigation' });
    await expect(toggle).toBeVisible();
    await expect(page.locator('#console-primary')).toBeHidden();
    const closed = await topBarBoxes(page);
    expect(closed, 'menu closed').toMatchObject({ overlaps: [], outside: [] });
    expect(closed.scrollWidth, 'page width').toBeLessThanOrEqual(closed.innerWidth + 1);
    const contentEdges = await page.locator('.operate-summary-tile, .operate-log-filter-bar')
      .evaluateAll(elements => elements.map(element => element.getBoundingClientRect().right));
    expect(Math.max(...contentEdges), 'summary tiles and log filter').toBeLessThanOrEqual(closed.innerWidth + 1);

    await toggle.click();
    await page.locator('[data-navigation-menu="Settings"] > summary').click();
    const items = page.locator('[data-navigation-menu="Settings"] .console-navigation-items');
    await expect(items).toBeVisible();
    const open = await topBarBoxes(page);
    expect(open, 'menu open').toMatchObject({ overlaps: [], outside: [] });
    expect(open.scrollWidth, 'page width with the menu open').toBeLessThanOrEqual(open.innerWidth + 1);
    const itemsBox = (await items.boundingBox())!;
    expect(itemsBox.x + itemsBox.width, 'Settings items').toBeLessThanOrEqual(open.innerWidth + 1);
    await testInfo.attach('navigation-200-percent', { body: await page.screenshot(), contentType: 'image/png' });
  } finally {
    await close();
  }
});

test('top navigation keeps its default layout at normal text size', async ({ page }, testInfo) => {
  const close = await startSignedInConsole(page);
  try {
    await openActivity(page);
    if (testInfo.project.name === 'desktop') await page.setViewportSize({ width: 1440, height: 900 });
    const toggle = page.getByRole('button', { name: 'Toggle navigation' });
    const wide = testInfo.project.name === 'desktop';
    await expect(toggle).toBeVisible({ visible: !wide });
    await expect(page.locator('#console-primary')).toBeVisible({ visible: wide });
    const closed = await topBarBoxes(page);
    expect(closed).toMatchObject({ overlaps: [], outside: [] });
    expect(closed.scrollWidth).toBeLessThanOrEqual(closed.innerWidth + 1);
  } finally {
    await close();
  }
});

test('expanded navigation keeps keyboard focus onscreen at 200% text size', async ({ page }) => {
  const close = await startSignedInConsole(page);
  try {
    await page.evaluate(() => { document.documentElement.style.fontSize = '32px'; });
    await page.getByRole('button', { name: 'Toggle navigation' }).press('Enter');
    for (const group of ['Operate', 'Access', 'Settings']) {
      const menu = page.locator(`[data-navigation-menu="${group}"]`);
      const summary = menu.locator(':scope > summary');
      await summary.press('Enter');
      const links = await menu.locator('.console-navigation-items a').all();
      const expectVisibleFocus = async (link: typeof links[number]) => {
        await expect(link).toBeFocused();
        await expect.poll(() => link.evaluate(element => {
          const box = element.getBoundingClientRect();
          const header = element.closest('header')!.getBoundingClientRect();
          return box.top >= Math.max(0, header.top) - 1 && box.bottom <= Math.min(innerHeight, header.bottom) + 1;
        }), { message: `${group} link remains fully visible` }).toBe(true);
      };
      for (const [index, link] of links.entries()) {
        if (index > 0) await page.keyboard.press('Tab');
        await expectVisibleFocus(link);
      }
      for (const link of links.slice(0, -1).reverse()) {
        await page.keyboard.press('Shift+Tab');
        await expectVisibleFocus(link);
      }
      await summary.press('Enter');
    }
  } finally {
    await close();
  }
});

test('desktop dropdowns remain visible after expanding compact navigation', async ({ page }) => {
  const close = await startSignedInConsole(page);
  try {
    await page.setViewportSize({ width: 390, height: 900 });
    await page.getByRole('button', { name: 'Toggle navigation' }).press('Enter');
    await page.setViewportSize({ width: 1600, height: 900 });
    await page.locator('[data-navigation-menu="Operate"] > summary').press('Enter');
    const link = page.locator('[data-navigation-menu="Operate"] .console-navigation-items a').last();
    await expect.poll(() => link.evaluate(element => {
      const box = element.getBoundingClientRect();
      return document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2)?.closest('a') === element;
    })).toBe(true);
  } finally {
    await close();
  }
});

for (const rootSize of [16, 32]) {
  for (const group of ['Operate', 'Access', 'Settings']) {
    test(`focused ${group} link stays onscreen after reflow at ${rootSize}px`, async ({ page }) => {
      const close = await startSignedInConsole(page);
      try {
        await page.setViewportSize({ width: 390, height: 568 });
        await page.evaluate(size => { document.documentElement.style.fontSize = `${size}px`; }, rootSize);
        await page.getByRole('button', { name: 'Toggle navigation' }).press('Enter');
        const menu = page.locator(`[data-navigation-menu="${group}"]`);
        await menu.locator(':scope > summary').press('Enter');
        const links = await menu.locator('.console-navigation-items a').all();
        await expect(links[0]).toBeFocused();
        for (let index = 1; index < links.length; index++) await page.keyboard.press('Tab');
        const last = links.at(-1)!;
        await expect(last).toBeFocused();
        for (const width of [1600, 320, 1280, 390]) {
          await page.setViewportSize({ width, height: 568 });
          await expect(last).toBeFocused();
          await expect.poll(() => last.evaluate(element => {
            const box = element.getBoundingClientRect();
            return box.top >= -1 && box.bottom <= innerHeight + 1
              && box.left >= -1 && box.right <= innerWidth + 1;
          }), { message: `${group} focus stays visible at ${width}px` }).toBe(true);
        }
      } finally {
        await close();
      }
    });
  }
}

for (const width of [390, 1024, 1101, 1120, 1121, 1280, 1440]) {
  for (const increasedSpacing of [false, true]) {
    test(`header controls stay apart at ${width}px with ${increasedSpacing ? 'increased' : 'default'} spacing`, async ({ page }) => {
      const close = await startSignedInConsole(page);
      try {
        await page.setViewportSize({ width, height: 900 });
        if (increasedSpacing) {
          await page.addStyleTag({ content: '* { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } p { margin-bottom: 2em !important; }' });
        }
        const toggle = page.getByRole('button', { name: 'Toggle navigation' });
        const closed = await topBarBoxes(page);
        expect(closed, 'header controls').toMatchObject({ overlaps: [], outside: [] });
        expect(closed.scrollWidth).toBeLessThanOrEqual(closed.innerWidth + 1);
        await expect(toggle).toBeVisible({ visible: width <= 1120 });
        if (await toggle.isVisible()) await toggle.press('Enter');
        const settings = page.locator('[data-navigation-menu="Settings"] > summary');
        await settings.press('Enter');
        await expect(page.locator('[data-navigation-menu="Settings"] .console-navigation-items')).toBeVisible();
        expect(await topBarBoxes(page), 'open navigation').toMatchObject({ overlaps: [], outside: [] });
      } finally {
        await close();
      }
    });
  }
}

test('selected job detail and unavailable workflow evidence fit the viewport', async ({ page }, testInfo) => {
  const jobId = '79ecd964b5094e3ca13d7f61529f0c18';
  const close = await startSignedInConsole(page);
  try {
    await page.route('**/api/v1/admin/jobs**', async route => {
      const path = new URL(route.request().url()).pathname;
      const body = path.endsWith(`/${jobId}`) ? {
        jobId, kind: 'export', backend: 'csv', status: 'Running',
        workloadName: 'Export in progress', requestedBy: 'apikey:api-key:00000000-0000-0000-0000-000000000001',
        createdAt: '2026-11-16T02:00:00Z', percentComplete: 50,
      } : { items: [] };
      await route.fulfill({ json: body });
    });
    await page.goto(`${origin}/console/operate/jobs/${jobId}`);
    await expect(page.locator('#job-detail-heading')).toHaveText(jobId);
    await expect(page.getByRole('heading', { name: 'Workflow evidence unavailable' })).toBeVisible();
    for (const grid of await page.locator('.operate-detail-grid').all()) {
      await expect(grid).not.toContainText('honua-server#');
    }
    const dimensions = await page.locator('.operate-detail-grid > *').evaluateAll(elements => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth,
      rightEdges: elements.map(element => element.getBoundingClientRect().right),
    }));
    expect(dimensions.scrollWidth, 'selected-job page width').toBeLessThanOrEqual(dimensions.innerWidth + 1);
    expect(Math.max(...dimensions.rightEdges), 'job grid panels').toBeLessThanOrEqual(dimensions.innerWidth + 1);
    await testInfo.attach('selected-job-reflow', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  } finally {
    await close();
  }
});
