import { test, expect } from '@playwright/test';

const modulePath = '/console/_content/Honua.Console.Shell/chart-preview.js';
const chart = {
  mark: 'bar',
  data: { values: [{ category: 'east', amount: 12 }, { category: 'west', amount: 30 }] },
  encoding: {
    x: { field: 'category', type: 'nominal' },
    y: { field: 'amount', type: 'quantitative' },
  },
};

for (const asset of ['vega', 'vega-lite', 'vega-embed']) {
  test(`chart recovers after a transient ${asset} load failure`, async ({ page }, testInfo) => {
    let requests = 0;
    await page.route(`**/vendor/${asset}/${asset}.min.js`, async route => {
      if (++requests === 1) await route.abort('failed');
      else await route.continue();
    });
    await page.goto('/console/studio', { waitUntil: 'networkidle' });
    const result = await page.evaluate(async ({ modulePath, chart }) => {
      const mod = await import(modulePath);
      const target = document.createElement('div');
      target.id = 'chart-recovery-fixture';
      target.style.cssText = 'width: min(100%, 600px); height: 300px; background: white';
      document.body.appendChild(target);
      const first = await mod.init(target, { spec: JSON.stringify(chart) });
      const second = await mod.init(target, { spec: JSON.stringify(chart) });
      return {
        first, second,
        bars: [...target.querySelectorAll<SVGGraphicsElement>('.mark-rect.role-mark path')]
          .map(bar => ({ label: bar.getAttribute('aria-label'), height: bar.getBBox().height })),
      };
    }, { modulePath, chart });
    expect(result.first).toBe(false);
    expect(result.second).toBe(true);
    expect(requests).toBe(2);
    expect(result.bars.map(bar => bar.label)).toEqual([
      'category: east; amount: 12', 'category: west; amount: 30',
    ]);
    expect(result.bars[0].height / result.bars[1].height).toBeCloseTo(12 / 30, 5);
    await testInfo.attach('recovered-chart', {
      body: await page.locator('#chart-recovery-fixture').screenshot(), contentType: 'image/png',
    });
  });
}

for (const replacement of ['closed', 'replaced']) {
  test(`late Vega embedding preserves the ${replacement} panel`, async ({ page }) => {
    await page.goto('/console/studio', { waitUntil: 'networkidle' });
    const result = await page.evaluate(async ({ modulePath, chart, replacement }) => {
      const mod = await import(modulePath);
      const target = document.createElement('div');
      target.style.cssText = 'width: 320px; height: 240px';
      document.body.appendChild(target);
      await mod.init(target, { spec: JSON.stringify(chart) });
      const runtime = window as unknown as { vegaEmbed: (...args: unknown[]) => Promise<{ finalize: () => void }> };
      const embed = runtime.vegaEmbed;
      let resume!: () => void;
      let started!: () => void;
      const gate = new Promise<void>(resolve => { resume = resolve; });
      const pending = new Promise<void>(resolve => { started = resolve; });
      let finalized = 0;
      runtime.vegaEmbed = async (...args) => {
        started();
        await gate;
        const view = await embed(...args);
        const finalize = view.finalize.bind(view);
        view.finalize = () => { finalized++; finalize(); };
        return view;
      };
      const oldChart = mod.init(target, { spec: JSON.stringify(chart) });
      await pending;
      mod.dispose(target);
      runtime.vegaEmbed = embed;
      if (replacement === 'replaced') {
        await mod.init(target, { spec: JSON.stringify({ ...chart, mark: 'point' }) });
      }
      const current = target.firstElementChild;
      resume();
      const oldMounted = await oldChart;
      const outcome = {
        oldMounted, finalized,
        preserved: target.firstElementChild === current,
        charts: target.querySelectorAll('svg.marks').length,
      };
      mod.dispose(target);
      target.remove();
      return outcome;
    }, { modulePath, chart, replacement });
    expect(result).toEqual({
      oldMounted: false, finalized: 1, preserved: true, charts: replacement === 'closed' ? 0 : 1,
    });
  });
}
