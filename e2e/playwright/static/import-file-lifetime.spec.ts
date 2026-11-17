import { expect, test, type Page } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

async function importFixture(v4Enabled: boolean) {
  let previews = 0;
  let uploaded = '';
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
      handleApi: async (request, response) => {
        response.setHeader('Content-Type', 'application/json');
        const path = new URL(request.url!, 'http://localhost').pathname;
        if (path.endsWith('/auth/session')) {
          response.end(JSON.stringify({ isAuthenticated: true, claims: [
            { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
            { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
          ] }));
        } else if (path.endsWith('/auth/csrf')) {
          response.end(JSON.stringify({ requestToken: 'fixture-only' }));
        } else if (path.endsWith('/import/formats')) {
          response.end(JSON.stringify({ supportedExtensions: ['.gpkg'] }));
        } else if (path.endsWith('/import/preview')) {
          previews++;
          const chunks: Buffer[] = [];
          for await (const chunk of request) chunks.push(Buffer.from(chunk));
          uploaded = Buffer.concat(chunks).toString();
          response.end('{}');
        } else {
          response.writeHead(404).end('{}');
        }
      },
    });
  return { server, previewCount: () => previews, uploaded: () => uploaded };
}

async function holdFileRead(page: Page) {
  await page.addInitScript(() => {
    const state = window as unknown as { readStarted: boolean; releaseRead: () => void; readFinished: boolean };
    let release: () => void = () => {};
    const gate = new Promise<void>(resolve => { release = resolve; });
    state.releaseRead = release;
    const slice = File.prototype.slice;
    File.prototype.slice = function (...args) {
      const blob = slice.apply(this, args);
      if (this.name === 'lifetime.gpkg') {
        const read = blob.arrayBuffer.bind(blob);
        blob.arrayBuffer = async () => {
          state.readStarted = true;
          await gate;
          const bytes = await read();
          state.readFinished = true;
          return bytes;
        };
      }
      return blob;
    };
  });
}

async function navigateToSupport(page: Page) {
  await page.evaluate(() => {
    const link = document.createElement('a');
    link.href = '/console/support';
    document.body.append(link);
    link.click();
    link.remove();
  });
  await expect(page.locator('h1')).toHaveText(/^(Contact support|Support)$/);
}

for (const v4Enabled of [false, true]) {
  for (const navigateAway of [false, true]) {
    test(`file read owns its preview (v4=${v4Enabled}, navigate=${navigateAway})`, async ({ page }, testInfo) => {
      const errors: string[] = [];
      page.on('pageerror', error => errors.push(error.message));
      const fixture = await importFixture(v4Enabled);
      const { server } = fixture;
      try {
        await holdFileRead(page);
        const address = server.address();
        if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
        await page.goto(`http://127.0.0.1:${address.port}/console/operate/resources/import`);
        const input = page.locator('input[type=file]');
        await expect(input).toHaveAttribute('accept', '.gpkg');
        await input.setInputFiles({ name: 'lifetime.gpkg', mimeType: 'application/octet-stream', buffer: Buffer.from('held-import-fixture') });
        await expect.poll(() => page.evaluate(() => (window as unknown as { readStarted: boolean }).readStarted)).toBe(true);
        expect(fixture.previewCount()).toBe(0);
        if (navigateAway) await navigateToSupport(page);
        await page.evaluate(() => (window as unknown as { releaseRead: () => void }).releaseRead());
        await expect.poll(() => page.evaluate(() => (window as unknown as { readFinished: boolean }).readFinished)).toBe(true);
        if (navigateAway) {
          await page.waitForTimeout(500);
          expect(fixture.previewCount()).toBe(0);
          await expect(page.locator('h1')).toHaveText(/^(Contact support|Support)$/);
        } else {
          await expect.poll(fixture.previewCount).toBe(1);
          await expect(page.getByRole('note')).toContainText('did not report a coordinate system');
          expect(fixture.uploaded()).toContain('held-import-fixture');
          await expect(input).toBeEnabled();
        }
        expect(errors).toEqual([]);
        await testInfo.attach('import-file-lifetime', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
      } finally {
        await page.evaluate(() => (window as unknown as { releaseRead?: () => void }).releaseRead?.()).catch(() => {});
        await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
      }
    });
  }
}
