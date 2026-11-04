import { test, expect, type Page } from '@playwright/test';

const MINIMUM_TEXT_CONTRAST = 4.5;
const MINIMUM_CONTROL_CONTRAST = 3;

async function openInteractiveRoute(page: Page, route: string): Promise<void> {
  await page.goto(route);
  await expect(page.locator('h1')).toBeFocused();
}

function luminance(color: string): number {
  const channels = color.match(/[\d.]+/g)!.slice(0, 3).map(Number).map(channel => {
    const srgb = channel / 255;
    return srgb <= 0.04045 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

function contrast(foreground: string, background: string): number {
  const values = [luminance(foreground), luminance(background)].sort((first, second) => second - first);
  return (values[0] + 0.05) / (values[1] + 0.05);
}

for (const colorScheme of ['light', 'dark'] as const) {
  test(`legacy routes retain the light palette in a system-${colorScheme} browser`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    await openInteractiveRoute(page, '/operate/data');
    await expect(page.locator('h1')).toHaveCount(1);
    const palette = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      return {
        scheme: root.colorScheme,
        background: getComputedStyle(document.body).backgroundColor,
        surface: root.getPropertyValue('--c-surface').trim(),
        text: getComputedStyle(document.body).color,
      };
    });
    expect(palette.scheme).toBe('light');
    expect(palette.background).toBe('rgb(247, 249, 248)');
    expect(palette.surface).toBe('#ffffff');
    expect(contrast(palette.text, palette.background)).toBeGreaterThanOrEqual(MINIMUM_TEXT_CONTRAST);
  });
}

test('rendered Add data links retain readable text inside action rows', async ({ page }, info) => {
  await openInteractiveRoute(page, '/operate/data');
  const action = page.locator('.console-action-row a[href$="new"]').first();
  await expect(action).toBeVisible();
  const observations = [];
  for (const state of ['normal', 'hover'] as const) {
    if (state === 'hover') await action.hover();
    const colors = await action.evaluate(element => {
      const style = getComputedStyle(element);
      return { foreground: style.color, background: style.backgroundColor };
    });
    const ratio = contrast(colors.foreground, colors.background);
    observations.push({ state, ...colors, ratio });
    expect(ratio).toBeGreaterThanOrEqual(MINIMUM_TEXT_CONTRAST);
  }
  await page.keyboard.press('Tab');
  await action.focus();
  await expect(action).toBeFocused();
  const focus = await action.evaluate(element => {
    const style = getComputedStyle(element);
    return { shadow: style.boxShadow, outline: style.outlineStyle };
  });
  expect(focus.shadow !== 'none' || focus.outline !== 'none').toBe(true);
  await info.attach('computed-action-contrast', { body: JSON.stringify(observations), contentType: 'application/json' });
});

test('semantic text, status and control pairs meet computed contrast', async ({ page }, info) => {
  await openInteractiveRoute(page, '/operate/data');
  const pairs = await page.evaluate(() => {
    const style = getComputedStyle(document.documentElement);
    const token = (name: string) => style.getPropertyValue(`--c-${name}`).trim();
    return [
      ['text', 'surface'], ['text', 'bg'], ['text-2', 'surface'], ['text-3', 'surface-2'],
      ['link', 'surface'], ['on-accent', 'accent'], ['on-accent', 'accent-hover'],
      ['accent-ink', 'accent-soft'], ['on-danger', 'danger'], ['ok-ink', 'ok-soft'],
      ['warn-ink', 'warn-soft'], ['danger-ink', 'danger-soft'], ['info-ink', 'info-soft'],
      ['neutral-ink', 'neutral-soft'], ['text', 'selected'], ['text-2', 'selected'],
      ['focus', 'surface'], ['focus', 'bg'], ['border-strong', 'surface'],
    ].map(([foreground, background]) => ({ foreground, background, colors: [token(foreground), token(background)] }));
  });
  const rgb = (hex: string) => `rgb(${hex.slice(1).match(/../g)!.map(channel => parseInt(channel, 16)).join(', ')})`;
  const observations = pairs.map(pair => ({ ...pair, ratio: contrast(rgb(pair.colors[0]), rgb(pair.colors[1])) }));
  for (const pair of observations) {
    const minimum = ['focus', 'border-strong'].includes(pair.foreground) ? MINIMUM_CONTROL_CONTRAST : MINIMUM_TEXT_CONTRAST;
    expect(pair.ratio, `${pair.foreground} on ${pair.background}`).toBeGreaterThanOrEqual(minimum);
  }
  await info.attach('computed-semantic-contrast', { body: JSON.stringify(observations), contentType: 'application/json' });
});

test('forced colors retain an outline and reduced motion stops shell animation', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active', reducedMotion: 'reduce' });
  await openInteractiveRoute(page, '/operate/data');
  const action = page.locator('.console-action-row a[href$="new"]').first();
  await page.keyboard.press('Tab');
  await action.focus();
  await expect(action).toBeFocused();
  const style = await action.evaluate(element => {
    const computed = getComputedStyle(element);
    return { outline: computed.outlineStyle, outlineWidth: computed.outlineWidth };
  });
  expect(style.outline).toBe('solid');
  expect(parseFloat(style.outlineWidth)).toBeGreaterThanOrEqual(2);
  for (const selector of ['.console-shell', '.console-collapse-button', '.console-collapse-button svg']) {
    const duration = await page.locator(selector).first().evaluate(element => getComputedStyle(element).transitionDuration);
    expect(duration, selector).toBe('0s');
  }
});

test('secondary button and tab style boundaries meet non-text contrast', async ({ page }, info) => {
  await openInteractiveRoute(page, '/operate/data/new');
  const observations = await page.evaluate(() => {
    const tabs = document.createElement('nav');
    tabs.className = 'console-tab-row';
    tabs.innerHTML = '<a href="#details">Details</a><button type="button">Settings</button>';
    document.querySelector('.console-page')!.append(tabs);
    const secondary = document.querySelector('.console-button-secondary')!;
    return [secondary, ...tabs.children].map(element => {
      const style = getComputedStyle(element);
      return { control: element.textContent!.trim(), border: style.borderTopColor, background: style.backgroundColor };
    });
  });
  for (const observation of observations) {
    expect(contrast(observation.border, observation.background), observation.control).toBeGreaterThanOrEqual(MINIMUM_CONTROL_CONTRAST);
  }
  await info.attach('computed-control-boundaries', { body: JSON.stringify(observations), contentType: 'application/json' });
});
