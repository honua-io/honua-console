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
