import { test, expect } from '@playwright/test';

// Spec 2: the four product-area routes all resolve from the same Blazor Web host
// and render their area heading. These are the IA anchors from docs/console-route-map.md.
// Catalog and Share stay hidden until the server advertises a persistent catalog.
const areas = [
  { path: '/studio', heading: 'Studio is unavailable on this deployment' },
  { path: '/catalog', heading: 'Catalog and sharing are not available yet' },
  { path: '/operate', heading: 'Operate' },
  { path: '/share', heading: 'Catalog and sharing are not available yet' },
] as const;

for (const area of areas) {
  test(`${area.path} renders the ${area.heading} area`, async ({ page }) => {
    const response = await page.goto(area.path, { waitUntil: 'domcontentloaded' });
    expect(response?.status(), `${area.path} should return 200`).toBe(200);

    // The area heading renders (not a 404 / crash page).
    await expect(
      page.getByRole('heading', { name: area.heading, exact: true }).first(),
    ).toBeVisible();

    // Not the status-code re-execution / not-found surface.
    await expect(page).not.toHaveURL(/\/not-found/);
  });
}

const menus = [
  {name:'Data', paths:['/operate/data','/operate/services','/operate/layers','/operate/connections','/operate/data/new']},
  {name:'Protocols', paths:['/operate/catalogs']},
  {name:'Operate', paths:['/operate','/inbox','/operate/health','/operate/copilot','/operate/geoprocessing','/operate/observability','/operate/metrics','/operate/deploy','/operate/releases']},
  {name:'Access', paths:['/operate/access/members','/operate/access']},
  {name:'Settings', paths:['/operate/settings']},
];

for (const name of ['Services', 'Layers']) test(`Data > ${name} opens its list`, async ({page}) => {
  await page.goto('/operate');
  const menu = page.locator('[data-navigation-menu="Data"]');
  await menu.locator(':scope > summary').press('Enter');
  await menu.getByRole('link', {name, exact:true}).click();
  await expect(page).toHaveURL(new RegExp(`/operate/${name.toLowerCase()}$`));
  await expect(page.getByRole('heading', {level:1, name, exact:true})).toBeVisible();
  await expect(page.locator('[data-resource-tree]')).toHaveCount(0);
  await expect(page.getByText('Connect environment', {exact:true}).or(
    page.getByText('missing binding', {exact:false}).first())).toBeVisible();
});

for (const menu of menus) test(`${menu.name} advertises working current destinations`, async ({page}) => {
  await page.goto('/operate');
  const disclosure=page.locator(`[data-navigation-menu="${menu.name}"]`);
  await disclosure.locator(':scope > summary').press('Enter');
  const nav=disclosure.getByRole('group',{name:menu.name,exact:true});
  await expect(nav).toBeVisible();
  await expect(nav.locator('a[href]')).toHaveCount(menu.paths.length);
  for(const path of menu.paths) {
    await expect(nav.locator(`a[href="${path}"]`)).toBeVisible();
  }
  await expect(nav.locator('a').first()).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(nav).toBeHidden();
  await expect(disclosure.locator(':scope > summary')).toBeFocused();
  for(const path of menu.paths) {
    const response=await page.goto(path);
    expect(response?.status()).toBe(200);
    await expect(page.locator('main h1').first()).toBeVisible();
    await expect(page).not.toHaveURL(/\/not-found/);
    await expect(page.getByRole('heading',{name:'Page Not Found',exact:true})).toHaveCount(0);
  }
});

for (const width of [390, 1440]) test(`navigation preserves keyboard input during startup at ${width}px`, async ({page}) => {
  let startBlazor!: () => void;
  const bootstrap = new Promise<void>(resolve => { startBlazor = resolve; });
  await page.route(/\/_framework\/blazor\.web(?:\.[^/]+)?\.js$/, async route => {
    await bootstrap;
    await route.continue();
  });
  try {
    await page.setViewportSize({width, height:844});
    await page.goto('/operate', {waitUntil:'commit'});
    const toggle=page.getByRole('button',{name:'Toggle navigation'});
    if(width < 1100) {
      await toggle.press('Enter');
      await expect(toggle).toHaveAttribute('aria-expanded','true');
    }
    const summary=page.locator('[data-navigation-menu="Operate"] > summary');
    await summary.press('Enter');
    const nav=page.getByRole('group',{name:'Operate',exact:true});
    await expect(nav).toBeVisible();
    await nav.locator('a').first().focus();
    await page.locator('[data-navigation-menu="Operate"]').evaluate(element => {
      (window as unknown as {prerenderedNavigation: Element}).prerenderedNavigation=element;
    });
    startBlazor();
    await page.waitForFunction(() => document.querySelector('[data-navigation-menu="Operate"]') !==
      (window as unknown as {prerenderedNavigation: Element}).prerenderedNavigation);
    await expect(nav).toBeVisible();
    await expect(nav.locator('a').first()).toBeFocused();
    if(width < 1100) await expect(toggle).toHaveAttribute('aria-expanded','true');
    await page.keyboard.press('Escape');
    await expect(nav).toBeHidden();
    await expect(summary).toBeFocused();
    await summary.press('Enter');
    await nav.locator('a[href="/operate/health"]').press('Enter');
    await expect(page).toHaveURL(/\/operate\/health$/);
    await expect(page.getByRole('heading', {level:1})).toBeFocused();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  } finally {
    startBlazor();
  }
});
