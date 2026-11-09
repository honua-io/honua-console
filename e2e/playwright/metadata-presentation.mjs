import assert from "node:assert/strict";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { checkKeyboardControls } from "./console-presentation.mjs";

export const metadataPreviews = ["metadata-dataset", "metadata-dirty", "metadata-empty", "metadata-history", "metadata-fields", "metadata-fields-dirty", "metadata-import", "metadata-import-dirty"];

export async function inspectMetadataPresentation(page, sample, previews) {
  const root = page.locator(".metadata-preview");
  assert.equal(await root.locator("script, iframe, img, [onerror], [style]").count(), 0);
  await checkKeyboardControls(page, {
    controls: ".metadata-preview :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
    sizedControls: "input, select, button, textarea, summary, .metadata-preview-nav a",
    minimumHeight: 24
  });
  if (sample.name.startsWith("metadata-fields")) {
    assert.ok(await page.getByRole("heading", { name: "Try a local field draft" }).isVisible());
    assert.equal(await page.getByLabel("Name", { exact: true }).count(), 1);
  } else if (sample.name.startsWith("metadata-import")) {
    await inspectImportSummary(root);
  } else {
    await inspectDatasetPreview(page, sample);
  }
  await inspectTableWords(root);
  const accessibility = await new AxeBuilder({ page }).include(".metadata-preview").analyze();
  assert.deepEqual(accessibility.violations, []);
  await root.locator(".console-table-wrap").evaluateAll(regions => regions.forEach(region => { region.scrollLeft = 0; }));
  await page.screenshot({ path: path.join(previews, sample.name + "-" + sample.width + "-light.png"), fullPage: true });
  console.log(sample.name + ": " + sample.width + "px published CSS, CSP, keyboard, control sizes and axe pass");
}

async function inspectImportSummary(root) {
  const lead = root.locator('.metadata-import-counts > [data-metadata-count="mapped"] dd');
  const rest = await root.locator('.metadata-import-counts > div:not([data-metadata-count="mapped"]) dd').evaluateAll(elements =>
    elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
  assert.ok(await lead.evaluate(element => parseFloat(getComputedStyle(element).fontSize)) > Math.max(...rest));
  assert.ok(await root.getByText("No file has been uploaded.", { exact: false }).isVisible());
}

async function inspectDatasetPreview(page, sample) {
  const history = await page.getByRole("heading", { name: "Example history", exact: true }).boundingBox();
  const identification = await page.getByRole("heading", { name: "Identification", exact: true }).boundingBox();
  if (sample.width > 1000) assert.ok(history.x > identification.x + identification.width, "Desktop history must sit beside the fields");
  else assert.ok(history.y > identification.y, "Narrow history must stack below the fields");
  assert.ok(await page.getByRole("heading", { name: "Identification", exact: true }).isVisible());
  assert.ok(await page.getByRole("heading", { name: "Example history", exact: true }).isVisible());
  assert.equal(await page.getByLabel("Local Title override", { exact: true }).count(), 1);
  if (sample.name === "metadata-history") {
    const titleHistory = page.locator(".metadata-field details").first();
    await titleHistory.locator("summary").press("Enter");
    assert.ok(await titleHistory.locator(".metadata-history").isVisible(), "Per-field history must be open");
    assert.ok((await titleHistory.textContent()).includes("Example user"));
  }
  if (sample.name === "metadata-empty") {
    assert.ok(await page.getByText("Explicitly empty override", { exact: true }).isVisible());
    assert.equal(await page.getByLabel("Local Lineage override", { exact: true }).getAttribute("placeholder"), null);
  }
}

async function inspectTableWords(root) {
  const splitHeaders = await root.locator("table thead th").evaluateAll(headers =>
    headers.filter(header => /^[A-Za-z]+$/.test(header.textContent.trim())).filter(header => {
      const range = document.createRange();
      range.selectNodeContents(header);
      return range.getClientRects().length !== 1;
    }).map(header => header.textContent.trim()));
  assert.deepEqual(splitHeaders, [], "Single-word table headers must stay whole inside the scroll region");
}
