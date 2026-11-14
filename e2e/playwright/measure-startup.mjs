import { readdir, stat, writeFile } from "node:fs/promises";
import { resolve, relative } from "node:path";
import { chromium } from "@playwright/test";
import { startStaticBundleServer } from "./static-bundle-server.mjs";
import { createContractApi } from "./static-contract-server.mjs";

// Compare published runtimes on the same machine. This intentionally measures a
// local, uncompressed, cold-context sign-in, not production network latency.
const [directory, output, label = "runtime"] = process.argv.slice(2);
if (!directory || !output) throw new Error("Usage: node measure-startup.mjs PUBLISHED_CONSOLE OUTPUT_JSON LABEL");
const root = resolve(directory);
const sampleCount = 7;
const sizes = { frameworkRawBytes: 0, frameworkBrotliBytes: 0, publishedRawBytes: 0 };
async function inventory(path) {
  for (const entry of await readdir(path, { withFileTypes: true })) {
    const child = resolve(path, entry.name);
    if (entry.isDirectory()) {
      await inventory(child);
      continue;
    }
    const name = relative(root, child).replaceAll("\\", "/");
    const { size } = await stat(child);
    accountFile(name, size);
  }
}

function accountFile(name, size) {
  if (!/\.(br|gz)$/.test(name)) sizes.publishedRawBytes += size;
  if (!name.startsWith("_framework/")) return;
  if (name.endsWith(".br")) {
    sizes.frameworkBrotliBytes += size;
    return;
  }
  if (!name.endsWith(".gz")) sizes.frameworkRawBytes += size;
}

await inventory(root);
let origin;
const server = await startStaticBundleServer(root, 0, {
  configuration: { apiBaseUrl: "/", auth: { mode: "builtin" }, v4Enabled: true },
  handleApi: (request, response) => createContractApi({ allowedOrigin: origin })(request, response),
});
origin = `http://127.0.0.1:${server.address().port}`;
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const samplesMs = [];
  for (let index = 0; index < sampleCount; index++) {
    samplesMs.push(await measureSample(browser, origin));
  }
  const sorted = [...samplesMs].sort((a, b) => a - b);
  const result = { label, browser: browser.version(), viewport: "1280x900", samples: sampleCount,
    method: "Fresh Chromium context per sample; no-store localhost HTTP without compression; navigation start to visible password input; fixture API; seven runs including first run; median.",
    ...sizes, samplesMs, medianStartupMs: sorted[Math.floor(sampleCount / 2)] };
  await writeFile(output, `${JSON.stringify(result, null, 2)}\n`);
  console.log(JSON.stringify(result));
} finally {
  await browser?.close();
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
}

async function measureSample(browser, origin) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, serviceWorkers: "block" });
  try {
    const page = await context.newPage();
    await page.addInitScript(() => {
      const observer = new MutationObserver(() => {
        const password = document.querySelector('input[type="password"]');
        if (password && password.getBoundingClientRect().height > 0) {
          window.consoleSigninReadyMs = performance.now();
          observer.disconnect();
        }
      });
      observer.observe(document, { childList: true, subtree: true, attributes: true });
    });
    await page.goto(`${origin}/console/auth/signin`, { waitUntil: "domcontentloaded" });
    await page.waitForFunction(() => Number.isFinite(window.consoleSigninReadyMs), undefined, { timeout: 60_000 });
    return await page.evaluate(() => window.consoleSigninReadyMs);
  } finally {
    await context.close();
  }
}
