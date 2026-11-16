import { expect, type Page } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';

export const textModes = [
  { name: 'baseline', enlarged: false, spacing: false },
  { name: 'spacing', enlarged: false, spacing: true },
  { name: 'enlarged', enlarged: true, spacing: false },
  { name: 'enlarged-spacing', enlarged: true, spacing: true },
];

export async function startReflowFixture(page: Page, configuration: { v4Enabled: boolean }) {
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0,
    { configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, ...configuration },
      handleApi: (...args) => api(...args) },
  );
  const origin = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
  const api = createContractApi({ allowedOrigin: origin });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  return { origin, errors, close: () => new Promise<void>((resolve, reject) =>
    server.close(error => error ? reject(error) : resolve())) };
}

export async function installTextSpacing(page: Page) {
  await page.addStyleTag({ content: `html[data-test-spacing] * {
    line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important;
  } html[data-test-spacing] p { margin-bottom: 2em !important; }` });
}

export async function applyTextMode(page: Page, mode: typeof textModes[number]) {
  await page.evaluate(({ enlarged, spacing }) => {
    document.documentElement.style.fontSize = enlarged ? '200%' : '';
    document.documentElement.toggleAttribute('data-test-spacing', spacing);
  }, mode);
}

export function expectDocumentFits(bounds: { rootWidth: number; viewport: number }, context: string) {
  expect.soft(bounds.rootWidth, context).toBeLessThanOrEqual(bounds.viewport);
}
