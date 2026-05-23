import { expect, test } from "@playwright/test";

test.describe("Console shell", () => {
  test("signs in with the fixture driver and keeps builder sessions out of Operate", async ({ page }) => {
    await page.goto("/studio");

    await expect(page.getByRole("heading", { name: "Sign in to Honua Console" })).toBeVisible();
    await page.getByRole("button", { name: "Continue as builder" }).click();

    await expect(page).toHaveURL(/\/studio$/);
    await expect(page.getByRole("heading", { name: "Studio" })).toBeVisible();
    await expect(page.getByTestId("usermenu-trigger")).toContainText("Mira Chen");
    await expect(page.getByTestId("nav-operate")).toHaveCount(0);

    await page.goto("/catalog");
    await expect(page.getByRole("heading", { name: "Catalog" })).toBeVisible();

    await page.goto("/share");
    await expect(page.getByRole("heading", { name: "Share" })).toBeVisible();

    await page.goto("/operate");
    await expect(page.getByTestId("brand-home")).toBeVisible();
    await expect(page.getByText(/reserved for operator and admin scopes/)).toBeVisible();
  });

  test("lets operator fixtures reach the Operate placeholder", async ({ page }) => {
    await page.goto("/operate");

    await expect(page.getByRole("heading", { name: "Sign in to Honua Console" })).toBeVisible();
    await page.getByRole("button", { name: "Continue as operator" }).click();

    await expect(page).toHaveURL(/\/operate$/);
    await expect(page.getByTestId("nav-operate")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Operate" })).toBeVisible();
  });
});
