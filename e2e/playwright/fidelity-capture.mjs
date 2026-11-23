import assert from "node:assert/strict";
import path from "node:path";

const fidelityPages = new Set([
  "home-v4-true", "navigation-v4-true", "home-inventory-v4", "folder-v4", "data-service-detail", "data-layer-detail",
  "studio-styles-list-v4", "studio-styles-v4", "studio-styles-conflict-v4", "data-layers",
  "scale-v4-initial", "scale-v4-expanded", "scale-v4-search", "scale-v4-empty",
]);

export function presentationWidths(name, widths = [1280, 390]) {
  return fidelityPages.has(name) ? [...new Set([1440, ...widths])] : widths;
}

export async function captureFidelityThemes(page, { previews, name, width }) {
  if (!fidelityPages.has(name) || ![1440, 390].includes(width)) return;
  const attributes = await page.evaluate(() => ({
    enabled: document.documentElement.getAttribute("data-console-theme-enabled"),
    theme: document.documentElement.getAttribute("data-theme"),
  }));
  try {
    for (const theme of ["light", "dark"]) {
      await page.evaluate(theme => {
        document.documentElement.setAttribute("data-console-theme-enabled", "true");
        document.documentElement.setAttribute("data-theme", theme);
      }, theme);
      await page.evaluate(() => document.fonts.ready);
      assert.equal(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme), theme,
        `${name}/${width}: ${theme} theme stylesheet must be active`);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
        `${name}/${width}/${theme}: horizontal page overflow`);
      await page.screenshot({
        path: path.join(previews, `${name}-fidelity-${width}-${theme}.png`),
        fullPage: true, animations: "disabled", caret: "hide",
      });
    }
  } finally {
    await page.evaluate(attributes => {
      for (const [name, value] of [["data-console-theme-enabled", attributes.enabled], ["data-theme", attributes.theme]]) {
        if (value === null) document.documentElement.removeAttribute(name);
        else document.documentElement.setAttribute(name, value);
      }
    }, attributes);
  }
}
