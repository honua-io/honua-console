import { test, expect } from '@playwright/test';

// Live spec: every operate section in the sidebar nav is reachable and functional WITHOUT
// depending on an AI provider. This enforces the product intent — forms/APIs are the
// FOUNDATION, AI is an ACCELERATOR — and catches any regression where an operate surface
// fails to render its missing-binding or capability-gate state and falls back to an AI
// prompt or a blank page instead.
//
// Each test navigates directly to an operate section route and asserts:
//   1. The page returns 200 (not 404/not-found).
//   2. The operate sections sidebar nav is present.
//   3. The page renders a primary heading — proving it is a real, navigable surface.
//   4. No AI-provider error surface is shown as the PRIMARY content. An AI entry in the
//      sidebar is expected; an AI-only wall that blocks the rest of the page is not.
//
// Capability-gated sections (sync, alerts/rules, releases, temporal) render their
// ConsoleCapabilityGate "unsupported" surface rather than live data — that is correct
// first-class behaviour, not a failure. These tests confirm the gate surface itself is
// reachable without an AI provider.
//
// This spec uses @playwright/test directly (no admin fixture) because it asserts nav
// structure and page rendering, not live server state mutations. It runs under the live
// config (playwright.live.config.ts) which boots the Console bound to a live honua-server,
// so server-owned surfaces show real missing-binding states rather than demo data.

// Non-capability-gated operate sections — always visible in the sidebar, always reachable.
const ungatedSections = [
  { path: '/operate/data', heading: 'Data & Layers' },
  { path: '/operate/connections', heading: 'Connections' },
  { path: '/operate/import/esri', heading: 'Import from Esri' },
  { path: '/operate/publishing', heading: 'Publishing Workspace' },
  { path: '/operate/versions', heading: /Versions|Branch.version|version manager/i },
  { path: '/operate/sensors', heading: /SensorThings|Sensor/i },
  { path: '/operate/scenes', heading: /3D Scenes|Scenes/i },
  { path: '/operate/geoprocessing', heading: /Geoprocessing/i },
  { path: '/operate/observability', heading: /Observability/i },
  { path: '/operate/metrics', heading: /Metrics|Performance/i },
  { path: '/operate/access', heading: /Access|Roles/i },
  { path: '/operate/catalogs', heading: /Catalogs/i },
  { path: '/operate/settings', heading: /Settings/i },
  { path: '/operate/deploy', heading: /Deploy/i },
] as const;

// Capability-gated sections — visible when the deployment advertises the capability.
// Their pages still resolve (not 404) even without the capability; they render a
// ConsoleCapabilityGate surface with a heading that describes what is unavailable.
const gatedSections = [
  { path: '/operate/sync', heading: /sync/i },
  { path: '/operate/alerts/rules', heading: /alert/i },
  { path: '/operate/temporal', heading: /temporal|history/i },
  { path: '/operate/releases', heading: /release/i },
] as const;

test.describe('Operate nav · no AI provider required (live)', () => {
  for (const section of ungatedSections) {
    test(`${section.path} is reachable and renders without AI`, async ({ page }) => {
      const response = await page.goto(section.path, { waitUntil: 'domcontentloaded' });
      expect(response?.status(), `${section.path} should return 200`).toBe(200);
      await expect(page).not.toHaveURL(/\/not-found/);

      // The operate sections sidebar appears on every /operate/* route.
      await expect(page.locator('nav[aria-label="Operate sections"]')).toBeVisible();

      // A primary heading is visible — the page is a navigable surface, not a blank stub.
      await expect(
        page.getByRole('heading', { name: section.heading }).first(),
      ).toBeVisible();

      // The page must NOT show an AI-only wall as its primary content. An AI entry in the
      // SIDEBAR is expected and correct; this guard catches any route that mistakenly
      // renders nothing but a "connect an AI provider" prompt as its page body.
      const main = page.locator('main.console-main');
      await expect(main).toBeVisible();
      // The main content area should contain either real data, a missing-binding state, or
      // a loading indicator — not ONLY an AI provider prompt. Check that the page body
      // does not consist solely of an omni-prompt / AI intake.
      const hasNonAiContent = await main.evaluate((el) => {
        // The omni-prompt console (Ask Honua AI) uses [data-omni-prompt]. If that is the
        // ONLY content element, the surface is AI-gated. Real operate pages never use it.
        const hasOmniOnly = el.querySelectorAll('[data-omni-prompt]').length > 0 &&
          el.querySelectorAll('.console-page, .console-panel, .console-state').length === 0;
        return !hasOmniOnly;
      });
      expect(
        hasNonAiContent,
        `${section.path} must render a manual operate surface, not an AI-only wall`,
      ).toBeTruthy();
    });
  }

  for (const section of gatedSections) {
    test(`${section.path} resolves to its capability-gate surface (not 404)`, async ({ page }) => {
      const response = await page.goto(section.path, { waitUntil: 'domcontentloaded' });
      expect(response?.status(), `${section.path} should return 200`).toBe(200);
      await expect(page).not.toHaveURL(/\/not-found/);

      // Capability-gated pages render either the gate surface (no capability) or the live
      // surface (capability advertised). Either way the page must show a heading.
      await expect(
        page.getByRole('heading', { name: section.heading }).first(),
      ).toBeVisible();
    });
  }

  test('AI entry is last in the operate sections sidebar and clearly labeled', async ({ page }) => {
    await page.goto('/operate', { waitUntil: 'domcontentloaded' });

    const nav = page.locator('nav[aria-label="Operate sections"]');
    await expect(nav).toBeVisible();

    // The AI entry must exist and include the word "AI" in its label so the user knows
    // it is an AI accelerator, not a required entry point.
    const aiLink = nav.getByText('Ask Honua (AI)');
    await expect(aiLink).toBeVisible();

    // The AI link must point to /operate/ai.
    const aiAnchor = nav.locator('a[href="/operate/ai"]');
    await expect(aiAnchor).toBeVisible();

    // The AI entry must be the LAST link in the operate sections sidebar, not the first.
    const allLinks = nav.locator('a[href]');
    const count = await allLinks.count();
    expect(count, 'operate sections nav must have entries').toBeGreaterThan(0);
    const lastHref = await allLinks.nth(count - 1).getAttribute('href');
    expect(lastHref, 'Ask Honua (AI) must be the last operate section').toBe('/operate/ai');

    // Manual operate surfaces must appear BEFORE the AI entry. Spot-check that the first
    // link is a data/connections/publishing entry, not the AI prompt.
    const firstHref = await allLinks.first().getAttribute('href');
    expect(firstHref, 'first operate section must not be AI').not.toBe('/operate/ai');
  });

  test('operate sidebar shows non-AI sections when AI provider is absent', async ({ page }) => {
    // Navigate to a core manual surface — Data & Layers.
    await page.goto('/operate/data', { waitUntil: 'domcontentloaded' });

    const nav = page.locator('nav[aria-label="Operate sections"]');
    await expect(nav).toBeVisible();

    // The core manual surfaces must be present in the sidebar.
    const manualSurfaces = ['Data & Layers', 'Connections', 'Publishing', 'Access', 'Settings', 'Deploy'];
    for (const label of manualSurfaces) {
      await expect(nav.getByText(label), `sidebar must show "${label}"`).toBeVisible();
    }

    // The page body renders the Data & Layers surface — no AI provider required.
    await expect(page.getByRole('heading', { name: 'Data & Layers' }).first()).toBeVisible();
  });
});
