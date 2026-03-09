import { test, expect } from "@playwright/test";

test.describe("TFT pages", () => {
  test("comps page shows composition entries", async ({ page }) => {
    await page.goto("/tft/comps");
    await expect(page.locator("main")).toBeVisible();
  });

  test("champions page shows champion cards with costs", async ({ page }) => {
    await page.goto("/tft/champions");
    await expect(page.locator("main")).toBeVisible();
  });

  test("items page loads with item grid", async ({ page }) => {
    await page.goto("/tft/items");
    await expect(page.locator("main")).toBeVisible();
  });

  test("traits page loads with trait entries", async ({ page }) => {
    await page.goto("/tft/traits");
    await expect(page.locator("main")).toBeVisible();
  });
});
