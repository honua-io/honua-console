import { expect, test } from "@playwright/test";

const GENERATED_APP_ID = "01J7APPS00000000000000";

test.describe("Generated app lifecycle smoke (honua-console#5)", () => {
  test("member opens authenticated generated app preview and rolls back one revision", async ({ page }) => {
    await page.goto("/auth/signin");

    await page.goto(`/studio/apps/${GENERATED_APP_ID}/preview?revision=rev-002`);
    await expect(page.getByRole("heading", { name: "Operations dashboard proof" })).toBeVisible();
    await expect(page.getByTestId("generated-app-preview-page")).toHaveAttribute("data-active-revision", "rev-002");
    await expect(page.getByText("SDK AppPackage restore point")).toBeVisible();
    await expect(page.getByText("Manifest artifact")).toBeVisible();

    await page.getByRole("button", { name: "Roll back to Revision 1" }).click();
    await expect(page.getByTestId("generated-app-preview-page")).toHaveAttribute("data-active-revision", "rev-001");
    await expect(page.getByText("v2")).toBeVisible();
  });
});
