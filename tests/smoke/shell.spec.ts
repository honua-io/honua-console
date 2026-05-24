import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";

const STYLE_EDITOR_DEMO_CONTENT_ITEM_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAR";

const NAV_ROUTES = [
  { id: "catalog", path: "/catalog", heading: "Catalog" },
  { id: "maps", path: "/catalog/maps", heading: "Maps" },
  { id: "data", path: "/data", heading: "Data" },
  { id: "groups", path: "/groups", heading: "Groups" },
  { id: "public", path: "/share/public", heading: "Public" },
] as const;

test.describe("Console shell smoke (honua-console#4)", () => {
  test("unauthenticated visit to a protected route lands at sign-in", async ({ page }) => {
    await page.goto("/maps");
    await expect(page.getByRole("heading", { name: /Sign in to Honua Console/i })).toBeVisible();
  });

  test("member sign-in then full nav round-trip", async ({ page }) => {
    await page.goto("/auth/signin?as=member");
    await expect(page.getByRole("heading", { name: /Welcome back/i })).toBeVisible();

    for (const route of NAV_ROUTES) {
      await page.getByTestId(`nav-${route.id}`).click();
      await expect(page).toHaveURL(new RegExp(`${route.path}$`));
      await expect(page.getByRole("heading", { level: 1, name: route.heading })).toBeVisible();
    }

    // Member should not see the operator link-back even with the menu open.
    await page.getByTestId("usermenu-trigger").click();
    await expect(page.getByTestId("usermenu-admin-link")).toHaveCount(0);

    // Sign-out returns to the signed-out screen.
    await page.getByTestId("usermenu-signout").click();
    await expect(page).toHaveURL(/\/auth\/signed-out$/);
  });

  test("operator session exposes the admin link-back", async ({ page }) => {
    await page.goto("/auth/signin?as=operator");
    await expect(page.getByRole("heading", { name: /Welcome back/i })).toBeVisible();
    await page.getByTestId("usermenu-trigger").click();
    const adminLink = page.getByTestId("usermenu-admin-link");
    await expect(adminLink).toBeVisible();
    const href = await adminLink.getAttribute("href");
    expect(href).toBeTruthy();
  });

  test("intent URL is preserved across sign-in", async ({ page }) => {
    await page.goto("/maps");
    await expect(page.getByRole("heading", { name: /Sign in to Honua Console/i })).toBeVisible();
    await page.getByTestId("signin-as-member").click();
    await expect(page).toHaveURL(/\/maps$/);
    await expect(page.getByRole("heading", { level: 1, name: "Maps" })).toBeVisible();
  });

  test("maps workspace opens a saved map", async ({ page }) => {
    await page.goto("/auth/signin?as=member");
    await page.goto("/maps");

    await expect(page.getByTestId("maps-workspace")).toBeVisible();
    await expect(page.getByTestId(`saved-map-card-${STYLE_EDITOR_DEMO_CONTENT_ITEM_ID}`)).toContainText(
      "Honolulu style demo",
    );

    await page.getByRole("link", { name: "Open map" }).click();
    await expect(page).toHaveURL(new RegExp(`/maps/${STYLE_EDITOR_DEMO_CONTENT_ITEM_ID}(?:#.*)?$`));
    await expect(page.getByTestId("viewer-header").getByText("Honolulu style demo")).toBeVisible();
  });

  test("saved-map collaboration shares presence, cursors, and feature edit cues", async ({ context, page }) => {
    await page.goto("/auth/signin?as=member");
    await page.goto("/maps/map-style-demo");
    await expect(page.locator("[data-map-status]")).toContainText("Loaded 2 layer");

    const ownerPage = await context.newPage();
    await ownerPage.goto("/auth/signin?as=owner");
    await ownerPage.goto("/maps/map-style-demo");
    await expect(ownerPage.locator("[data-map-status]")).toContainText("Loaded 2 layer");

    const ownerPanel = ownerPage.getByTestId("collaboration-panel");
    await expect(ownerPanel.locator("[data-collab-user-id='u-member']")).toContainText("Mira Chen");

    await page.getByTestId("map-container").hover({ position: { x: 360, y: 220 } });
    await expect(ownerPanel.locator("[data-collab-user-id='u-member']")).toContainText("Cursor on map");

    await page.getByTestId("feature-table").locator("tbody tr").first().click();
    await expect(ownerPanel.locator("[data-collab-feature-list]")).toContainText("Mira Chen editing");
    await expect(ownerPage.getByTestId("feature-table").locator("tbody tr[data-collaboration='editing']")).toHaveCount(
      1,
    );

    await ownerPage.close();
  });

  test("member can add saved-map annotations and embeds render them read-only", async ({ page }) => {
    await page.goto("/auth/signin?as=member");
    await page.goto("/maps/map-style-demo");

    await expect(page.getByTestId("viewer-header").getByText("Honolulu style demo")).toBeVisible();
    await expect(page.locator("[data-map-status]")).toContainText("Loaded 2 layer");

    const panel = page.getByTestId("annotation-panel");
    await panel.locator("[data-annotation-body]").fill("Smoke-test map review note.");
    await panel.getByRole("button", { name: "Place pin" }).click();
    await expect(panel).toContainText("Click the map to place the pin");
    await page.getByTestId("map-container").click({ position: { x: 360, y: 220 } });
    await expect(panel).toContainText("Smoke-test map review note.");
    await expect(page.locator("[data-map-status]")).toContainText("Annotation saved.");

    await panel.locator("[data-annotation-body]").fill("Smoke-test rectangle review area");
    await panel.getByRole("button", { name: "Place rectangle" }).click();
    await expect(panel).toContainText("Click the map to place the rectangle");
    await page.getByTestId("map-container").click({ position: { x: 420, y: 260 } });
    await expect(panel).toContainText("Smoke-test rectangle review area");
    await expect(page.locator("[data-map-status]")).toContainText("Rectangle annotation saved.");

    await panel.locator("[data-annotation-body]").fill("Smoke-test polygon review area");
    await panel.getByRole("button", { name: "Start polygon" }).click();
    await expect(panel.getByRole("button", { name: "Cancel polygon" })).toBeVisible();
    await page.getByTestId("map-container").click({ position: { x: 480, y: 220 } });
    await page.getByTestId("map-container").click({ position: { x: 560, y: 260 } });
    await page.getByTestId("map-container").click({ position: { x: 500, y: 320 } });
    await panel.getByRole("button", { name: "Finish polygon" }).click();
    await expect(panel).toContainText("Smoke-test polygon review area");
    await expect(page.locator("[data-map-status]")).toContainText("Polygon annotation saved.");

    await panel.locator("[data-annotation-body]").fill("Smoke-test freehand sketch");
    await panel.getByRole("button", { name: "Start freehand" }).click();
    await expect(panel.getByRole("button", { name: "Cancel freehand" })).toBeVisible();
    await page.getByTestId("map-container").click({ position: { x: 460, y: 180 } });
    await page.getByTestId("map-container").click({ position: { x: 500, y: 220 } });
    await page.getByTestId("map-container").click({ position: { x: 540, y: 240 } });
    await expect(panel.getByRole("button", { name: "Finish freehand" })).toBeEnabled();
    await panel.getByRole("button", { name: "Finish freehand" }).click();
    await expect(panel).toContainText("Smoke-test freehand sketch");
    await expect(page.locator("[data-map-status]")).toContainText("Freehand annotation saved.");

    await panel.getByLabel("Allow public comments").check();
    await expect(page.locator("[data-map-status]")).toContainText("Public comments enabled.");

    await page.reload();
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test map review note.");
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test rectangle review area");
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test polygon review area");
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test freehand sketch");

    const downloadPromise = page.waitForEvent("download");
    await page.getByTestId("annotation-panel").getByRole("button", { name: "Export JSON" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe("map-style-demo-annotations.json");
    const downloadPath = await download.path();
    if (!downloadPath) throw new Error("Expected Playwright to persist the annotation export download.");
    const exportText = await readFile(downloadPath, "utf8");
    expect(exportText).toContain("honua-annotation-export/v1");
    expect(exportText).toContain("Smoke-test map review note.");
    expect(exportText).toContain("Smoke-test rectangle review area");
    expect(exportText).toContain("Smoke-test polygon review area");
    expect(exportText).toContain("Smoke-test freehand sketch");
    expect(exportText).toContain('"publicComments": true');
    expect(exportText).toContain("Mira Chen");

    await page.evaluate(() => window.sessionStorage.clear());
    await page.goto("/embed/maps/map-style-demo");
    await expect(page.getByTestId("embed-map-viewer-root")).toBeVisible();
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test map review note.");
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test rectangle review area");
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test polygon review area");
    await expect(page.getByTestId("annotation-panel")).toContainText("Smoke-test freehand sketch");
    await expect(page.locator("[data-annotation-body]")).toHaveCount(1);
    await page.locator("[data-annotation-body]").fill("Guest public crosswalk note.");
    await page.getByRole("button", { name: "Place public comment" }).click();
    await page.getByTestId("map-container").click({ position: { x: 380, y: 210 } });
    await expect(page.getByTestId("annotation-panel")).toContainText("Public comment submitted for approval.");
    await expect(page.getByTestId("annotation-panel")).not.toContainText("Guest public crosswalk note.");
    await expect(page.getByRole("button", { name: "Place pin" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Place rectangle" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Start polygon" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Finish polygon" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Cancel polygon" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Start freehand" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Finish freehand" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Cancel freehand" })).toHaveCount(0);

    await page.goto("/auth/signin?as=member");
    await page.goto("/maps/map-style-demo");
    await expect(page.getByTestId("annotation-panel")).toContainText("Guest public crosswalk note.");
    await expect(page.getByTestId("annotation-panel")).toContainText("Pending approval");
    await page.getByTestId("annotation-panel").getByRole("button", { name: "Approve" }).click();
    await expect(page.locator("[data-map-status]")).toContainText("Comment approved.");

    await page.evaluate(() => window.sessionStorage.clear());
    await page.goto("/embed/maps/map-style-demo");
    await expect(page.getByTestId("annotation-panel")).toContainText("Guest public crosswalk note.");
    await expect(page.getByRole("button", { name: "Approve" })).toHaveCount(0);
  });

  test("map viewer hydrates a catalog service through the SDK client path", async ({ page }) => {
    await page.goto("/auth/signin?as=member");
    await page.goto("/maps/new?from=01HXY3ZK7N1J2Q9V8M0FQ2PWAB");

    await expect(page.getByTestId("viewer-header").getByText("City Parcels 2026")).toBeVisible();
    await expect(page.getByTestId("feature-table")).toContainText("HON-001");
    await expect(page.getByTestId("feature-table")).toContainText("Residential");
    await expect(page.locator("[data-map-status]")).toContainText("Loaded 1 layer");
  });

  test("owner can apply a catalog item sharing change", async ({ page }) => {
    await page.goto("/auth/signin?as=owner");
    await expect(page.getByRole("heading", { name: /Welcome back/i })).toBeVisible();

    await page.goto("/catalog/city-parcels-overview-map");
    await expect(page.getByRole("heading", { name: "City Parcels Overview Map" })).toBeVisible();

    const panel = page.getByTestId("share-panel");
    await expect(panel).toBeVisible();
    await panel.getByLabel("Visibility").selectOption("org");
    await panel.getByRole("button", { name: /Apply sharing/i }).click();
    await expect(panel.getByTestId("share-status")).toContainText("Sharing updated to Organization");
  });

  test("public open-data item is reachable without an account", async ({ page }) => {
    await page.goto("/public");
    await expect(page.getByRole("heading", { level: 1, name: "Public" })).toBeVisible();
    const publicItemLink = page.getByRole("link", { name: "City Parcels 2026", exact: true });
    await expect(publicItemLink).toBeVisible();
    await expect(page.getByText("City Parcels Overview Map")).toHaveCount(0);

    await publicItemLink.click();
    await expect(page).toHaveURL(/\/public\/items\/city-parcels-2026$/);
    await expect(page.getByRole("heading", { level: 1, name: "City Parcels 2026" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Downloads and APIs" })).toBeVisible();
    await expect(page.getByText("ArcGIS REST endpoint")).toBeVisible();
    await expect(page.getByText("OGC API Features endpoint")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Related items" })).toBeVisible();
    await expect(page.getByRole("link", { name: /City Parcels.*Active/ })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Revision history" })).toBeVisible();
    await expect(page.getByText("city-parcels-2026.2026-05-06")).toBeVisible();
    await expect(page.getByText(/metadata-level context only/i)).toBeVisible();
    await expect(page.getByRole("heading", { name: "ArcGIS REST GeoJSON" })).toBeVisible();
    await expect(page.getByTestId("signin-trigger")).toBeVisible();

    await page.goto("/public/items/internal-staff-roster-layer");
    await expect(page.getByRole("heading", { name: "Public item not found" })).toBeVisible();
    await expect(page.getByText("Internal Staff Roster Layer")).toHaveCount(0);
  });

  // The app-builder proof flow (catalog item → generated-app preview) is the
  // Studio surface and moves with honua-console#5. The Console shell smoke
  // owns Catalog → viewer → saved-map → share/embed → public open-data; the
  // cross-surface "publish → catalog → Studio → share/embed" smoke lives in
  // honua-console#9.
});
