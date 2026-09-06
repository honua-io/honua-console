import { test, expect } from '@playwright/test';

const modulePath = '/_content/Honua.Console.Shell/chart-preview.js';
const rows = [
  { id: 1, category: 'east', amount: 12 },
  { id: 2, category: 'west', amount: 30 },
  { id: 3, category: 'east', amount: 18 },
];

// Representative inputs for the shapes emitted by Studio*ChartSpec, StudioQueryResultChart,
// StudioAnalysisInputChart and OpsHealthTrendCharts. Expected values are fixture arithmetic,
// never a snapshot of the old or new renderer. C# tests separately cover the spec builders.
for (const schema of [5, 6]) {
  for (const surface of ['dashboard', 'report', 'query', 'analysis']) {
    test(`${surface} v${schema} spec renders the fixture values with Vega 6`, async ({ page }) => {
      const counts = surface === 'query' || surface === 'analysis';
      const field = surface === 'analysis' ? '__auto__' : 'category';
      const spec = {
        $schema: `https://vega.github.io/schema/vega-lite/v${schema}.json`,
        width: 300, height: 200,
        mark: { type: 'bar', tooltip: true },
        ...(counts ? {} : { data: { values: rows.slice(0, 2) } }),
        encoding: {
          x: { field, type: 'nominal', title: field },
          y: counts
            ? { aggregate: 'count', type: 'quantitative', title: 'features' }
            : { field: 'amount', type: 'quantitative' },
        },
      };
      let featureRequests = 0;
      await page.route('**/map-proxy/features/chart-fixture/0', async route => {
        featureRequests++;
        await route.fulfill({ json: { features: rows.map(attributes => ({ attributes })) } });
      });
      await page.goto('/studio', { waitUntil: 'domcontentloaded' });
      const rendered = await page.evaluate(async ({ modulePath, spec, counts }) => {
        const mod = await import(modulePath);
        const container = document.createElement('div');
        document.body.appendChild(container);
        try {
          const mounted = await mod.init(container, {
            spec: JSON.stringify(spec),
            featuresUrl: counts ? '/map-proxy/features/chart-fixture/0' : undefined,
          });
          const bars = [...container.querySelectorAll<SVGGraphicsElement>('.mark-rect.role-mark path')]
            .map(bar => ({ label: bar.getAttribute('aria-label'), height: bar.getBBox().height }));
          return { mounted, bars };
        } finally {
          mod.dispose(container);
          container.remove();
        }
      }, { modulePath, spec, counts });

      expect(rendered.mounted).toBe(true);
      expect(featureRequests).toBe(counts ? 1 : 0);
      expect(rendered.bars.map(bar => bar.label)).toEqual(counts
        ? ['category: east; features: 2', 'category: west; features: 1']
        : ['category: east; amount: 12', 'category: west; amount: 30']);
      expect(rendered.bars[0].height).toBeGreaterThan(0);
      expect(rendered.bars[1].height).toBeGreaterThan(0);
      expect(rendered.bars[0].height / rendered.bars[1].height).toBeCloseTo(counts ? 2 : 12 / 30, 5);
    });
  }

  for (const [surface, title, series] of [
    ['latency', 'Latency (ms)', [['p50', 10, 20], ['p95', 40, 60], ['p99', 80, 100]]],
    ['error rate', 'Error rate (%)', [['error rate', 2, 5]]],
    ['GP queue', 'Active jobs', [['cluster', 3, 7]]],
    ['alert backlog', 'Count', [['pending', 4, 8], ['dead-lettered', 1, 2]]],
  ] as const) {
    test(`Operate ${surface} v${schema} spec preserves points and reconnect seam`, async ({ page }) => {
      const times = ['2026-09-05T00:00:00Z', '2026-09-05T00:02:00Z'];
      const spec = {
        $schema: `https://vega.github.io/schema/vega-lite/v${schema}.json`,
        width: 300, height: 200,
        data: { values: series.flatMap(([name, first, second]) => [
          { bucketStart: times[0], series: name, value: first },
          { bucketStart: times[1], series: name, value: second },
        ]) },
        layer: [
          {
            mark: { type: 'line', point: true, tooltip: true },
            encoding: {
              x: { field: 'bucketStart', type: 'temporal', title: 'Time' },
              y: { field: 'value', type: 'quantitative', title },
              color: { field: 'series', type: 'nominal', title: 'Series' },
            },
          },
          {
            data: { values: [{ at: '2026-09-05T00:01:00Z' }] },
            mark: { type: 'rule', strokeDash: [4, 4], color: '#888888' },
            encoding: { x: { field: 'at', type: 'temporal' } },
          },
        ],
      };
      await page.goto('/operate/health', { waitUntil: 'domcontentloaded' });
      const rendered = await page.evaluate(async ({ modulePath, spec }) => {
        const mod = await import(modulePath);
        const container = document.createElement('div');
        document.body.appendChild(container);
        try {
          const mounted = await mod.init(container, { spec: JSON.stringify(spec) });
          const points = [...container.querySelectorAll<SVGGraphicsElement>('.mark-symbol.role-mark path')]
            .map(point => ({ label: point.getAttribute('aria-label'), transform: point.getAttribute('transform') }));
          const seams = [...container.querySelectorAll<SVGGraphicsElement>('.mark-rule.role-mark line')]
            .map(rule => ({ dash: rule.getAttribute('stroke-dasharray'), transform: rule.getAttribute('transform') }));
          return { mounted, points, seams };
        } finally {
          mod.dispose(container);
          container.remove();
        }
      }, { modulePath, spec });

      expect(rendered.mounted).toBe(true);
      expect(rendered.points).toHaveLength(series.length * 2);
      for (const [name, first, second] of series) {
        const ordinates: number[] = [];
        for (const [index, value] of [first, second].entries()) {
          const matches = rendered.points.filter(point =>
            point.label?.includes(`${title}: ${value}; Series: ${name}`));
          expect(matches).toHaveLength(1);
          const coordinates = matches[0].transform?.match(/^translate\(([-\d.]+),([-\d.]+)\)$/);
          expect(coordinates).toBeTruthy();
          expect(Number(coordinates![1])).toBeCloseTo(index * 300, 5);
          ordinates.push(Number(coordinates![2]));
        }
        // Higher values move up the SVG; the two timestamps occupy the domain endpoints.
        expect(ordinates[0]).toBeGreaterThan(ordinates[1]);
      }
      expect(rendered.seams).toEqual([{ dash: '4,4', transform: 'translate(150,0)' }]);
    });
  }
}

test('patched Vega rejects expression object coercion properties', async ({ page }) => {
  await page.goto('/studio', { waitUntil: 'domcontentloaded' });
  await page.addScriptTag({ url: '/_content/Honua.Console.Shell/vendor/vega/vega.min.js' });
  const results = await page.evaluate(() => {
    const vega = (window as unknown as { vega: { parse: (spec: unknown) => unknown } }).vega;
    return ['1 + 1', '({toString: 1}) + 1', '({valueOf: 1}) + 1'].map(update => {
      try {
        vega.parse({ signals: [{ name: 'fixture', update }] });
        return 'accepted';
      } catch (error) {
        return String(error);
      }
    });
  });
  expect(results[0]).toBe('accepted');
  expect(results[1]).toMatch(/Illegal property: toString/);
  expect(results[2]).toMatch(/Illegal property: valueOf/);
});

test('an empty bound feature result does not fabricate chart values', async ({ page }) => {
  await page.route('**/map-proxy/features/chart-fixture/0', route =>
    route.fulfill({ json: { features: [] } }));
  await page.goto('/studio', { waitUntil: 'domcontentloaded' });
  const rendered = await page.evaluate(async modulePath => {
    const mod = await import(modulePath);
    const container = document.createElement('div');
    document.body.appendChild(container);
    try {
      const mounted = await mod.init(container, {
        featuresUrl: '/map-proxy/features/chart-fixture/0',
        spec: JSON.stringify({
          $schema: 'https://vega.github.io/schema/vega-lite/v6.json',
          mark: 'bar',
          encoding: {
            x: { field: '__auto__', type: 'nominal' },
            y: { aggregate: 'count', type: 'quantitative' },
          },
        }),
      });
      return { mounted, svgCount: container.querySelectorAll('svg').length };
    } finally {
      mod.dispose(container);
      container.remove();
    }
  }, modulePath);
  expect(rendered).toEqual({ mounted: false, svgCount: 0 });
});
