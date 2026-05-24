import { expect, test } from "@playwright/test";

const PUBLISH_CASES: readonly {
  readonly draftId: string;
  readonly itemId: string;
  readonly target: string;
  readonly entryRoute: string;
  readonly entryTestId: string;
  readonly publishLinkTestId: string;
  readonly previewRoute: string;
  readonly previewSurfaceTestId: string;
}[] = [
  {
    draftId: "draft-map-operations",
    itemId: "console-map-operations",
    target: "map",
    entryRoute: "/studio/previews/draft-map-operations",
    entryTestId: "studio-preview-page",
    publishLinkTestId: "preview-publish-link",
    previewRoute: "/maps/console-map-operations",
    previewSurfaceTestId: "route-map-preview"
  },
  {
    draftId: "draft-dashboard-operations",
    itemId: "console-dashboard-operations",
    target: "dashboard",
    entryRoute: "/studio/drafts/draft-dashboard-operations",
    entryTestId: "studio-draft-page",
    publishLinkTestId: "draft-publish-link",
    previewRoute: "/dashboards/console-dashboard-operations",
    previewSurfaceTestId: "route-dashboard-preview"
  },
  {
    draftId: "draft-report-operations",
    itemId: "console-report-operations",
    target: "report",
    entryRoute: "/studio/drafts/draft-report-operations",
    entryTestId: "studio-draft-page",
    publishLinkTestId: "draft-publish-link",
    previewRoute: "/reports/console-report-operations",
    previewSurfaceTestId: "route-report-preview"
  },
  {
    draftId: "draft-app-operations",
    itemId: "console-app-operations",
    target: "app",
    entryRoute: "/studio/previews/draft-app-operations",
    entryTestId: "studio-preview-page",
    publishLinkTestId: "preview-publish-link",
    previewRoute: "/apps/console-app-operations/preview",
    previewSurfaceTestId: "route-app-preview"
  }
];

test.describe("Studio publishing smoke", () => {
  for (const publishCase of PUBLISH_CASES) {
    test(`publishes ${publishCase.target} from Studio and opens post-publish Console routes`, async ({ page }) => {
      await page.addInitScript(() => {
        const events: unknown[] = [];
        Object.defineProperty(window, "__honuaStudioPublishEvents", {
          configurable: true,
          value: events
        });
        window.addEventListener("honua:studio-publish", (event) => {
          events.push((event as CustomEvent).detail);
        });
      });

      await page.goto(publishCase.entryRoute);
      await expect(page.getByTestId(publishCase.entryTestId)).toBeVisible();

      await page.getByTestId(publishCase.publishLinkTestId).click();
      await expect(page.getByTestId("publish-review")).toHaveAttribute("data-target", publishCase.target);
      await expect(page).toHaveURL(`/studio/drafts/${publishCase.draftId}/publish`);

      await page.getByLabel("Visibility", { exact: true }).selectOption("workspace");
      await page.getByLabel("Allow embed when the selected visibility supports it").check();
      await page.getByLabel("Version note").fill(`Smoke publish ${publishCase.target}`);
      await page.getByTestId("publish-submit").click();

      await expect(page.getByTestId("publish-result")).toBeVisible();
      await expect(page.getByTestId("published-canonical-route")).toHaveText(`/catalog/${publishCase.itemId}`);
      await expect(page.getByTestId("publish-result")).toContainText(`${publishCase.itemId}-v1`);
      await expect(page.getByTestId("publish-result")).toContainText("workspace, embed same-origin");
      await expect(page.getByTestId("result-catalog-link")).toHaveAttribute("href", `/catalog/${publishCase.itemId}`);
      await expect(page.getByTestId("result-preview-link")).toHaveAttribute("href", publishCase.previewRoute);
      await expect(page.getByTestId("result-share-link")).toHaveAttribute("href", `/share/${publishCase.itemId}`);
      await expect(page.getByTestId("result-embed-link")).toHaveAttribute("href", `/embed/${publishCase.itemId}`);
      await expect(page.getByTestId("result-edit-link")).toHaveAttribute("href", `/studio/items/${publishCase.itemId}/edit`);

      const telemetry = await page.evaluate(() => {
        return (window as Window & { __honuaStudioPublishEvents?: unknown[] }).__honuaStudioPublishEvents ?? [];
      });
      expect(telemetry).toEqual(
        expect.arrayContaining([
          expect.objectContaining({ name: "publish.review.opened", draftId: publishCase.draftId, target: publishCase.target }),
          expect.objectContaining({ name: "publish.submitted", draftId: publishCase.draftId, target: publishCase.target }),
          expect.objectContaining({
            name: "publish.succeeded",
            draftId: publishCase.draftId,
            itemId: publishCase.itemId,
            target: publishCase.target
          })
        ])
      );

      await page.goto(`/catalog/${publishCase.itemId}`);
      const catalogSurface = page.getByTestId("route-catalog");
      await expect(catalogSurface).toBeVisible();
      await expect(catalogSurface.getByRole("link", { name: "Preview" })).toHaveAttribute("href", publishCase.previewRoute);
      await expect(catalogSurface.getByRole("link", { name: "Share" })).toHaveAttribute("href", `/share/${publishCase.itemId}`);
      await expect(catalogSurface.getByRole("link", { name: "Embed" })).toHaveAttribute("href", `/embed/${publishCase.itemId}`);
      await expect(catalogSurface.getByRole("link", { name: "Edit in Studio" })).toHaveAttribute(
        "href",
        `/studio/items/${publishCase.itemId}/edit`
      );

      await catalogSurface.getByRole("link", { name: "Preview" }).click();
      await expect(page.getByTestId(publishCase.previewSurfaceTestId)).toBeVisible();
      await page.goBack();
      await expect(page.getByTestId("route-catalog")).toBeVisible();

      if (publishCase.target === "dashboard") {
        await page.goto(`/maps/${publishCase.itemId}`);
        await expect(page.getByTestId("unsupported-state")).toContainText("Preview route does not match this item");
        await expect(page.getByTestId("unsupported-state")).toContainText("/dashboards/console-dashboard-operations");
        await page.goto(`/catalog/${publishCase.itemId}`);
        await expect(page.getByTestId("route-catalog")).toBeVisible();
      }

      await catalogSurface.getByRole("link", { name: "Share" }).click();
      await expect(page.getByTestId("share-settings")).toContainText("workspace");
      await expect(page.getByTestId("share-settings")).toContainText("Public link: Disabled");
      await page.goBack();
      await expect(page.getByTestId("route-catalog")).toBeVisible();

      await catalogSurface.getByRole("link", { name: "Embed" }).click();
      await expect(page.getByTestId("embed-settings")).toContainText("Embed policy: same-origin");
      await expect(page.getByTestId("embed-settings")).toContainText("Embeddable: Enabled");
      await page.goBack();
      await expect(page.getByTestId("route-catalog")).toBeVisible();

      await catalogSurface.getByRole("link", { name: "Edit in Studio" }).click();
      await expect(page.getByTestId("studio-edit-page")).toBeVisible();
      await expect(page.getByTestId("reopen-generation-state")).toHaveText("Not called");
    });
  }

  test("renders dependency-closure conflicts on the shared Console error surface", async ({ page }) => {
    await page.goto("/studio/drafts/draft-map-conflict/publish");
    await expect(page.getByTestId("publish-review")).toBeVisible();

    await page.getByTestId("publish-submit").click();

    await expect(page.getByTestId("publish-error")).toContainText("Private incident layer cannot be widened");
  });

  test("requires group ids before submitting group visibility", async ({ page }) => {
    await page.goto("/studio/drafts/draft-map-operations/publish");
    await expect(page.getByTestId("publish-review")).toBeVisible();

    await page.getByLabel("Visibility", { exact: true }).selectOption("group");
    await page.getByLabel("Group ids").fill("   ");
    await page.getByTestId("publish-submit").click();

    await expect(page.getByTestId("publish-error")).toContainText("Choose at least one group");
    await expect(page.getByTestId("publish-result")).toHaveCount(0);
  });
});
