import { test, expect } from "@playwright/test";

test.describe("Smoke tests", () => {
  test("landing page loads with LoL and TFT cards", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("link", { name: /lol|league/i })).toBeVisible();
    await expect(page.getByRole("link", { name: /tft|teamfight/i })).toBeVisible();
  });

  test("/lol/tierlist loads with champion data", async ({ page }) => {
    await page.goto("/lol/tierlist");
    await expect(page.locator("table tbody tr")).not.toHaveCount(0);
  });

  test("/lol/champions loads with champion grid", async ({ page }) => {
    await page.goto("/lol/champions");
    await expect(page.locator("main")).toBeVisible();
    // Champion grid should have items
    await expect(page.locator("[href*='/lol/champions/']").first()).toBeVisible();
  });

  test("/tft/comps loads with composition data", async ({ page }) => {
    await page.goto("/tft/comps");
    await expect(page.locator("main")).toBeVisible();
  });

  test("/tft/champions loads with champion cards", async ({ page }) => {
    await page.goto("/tft/champions");
    await expect(page.locator("main")).toBeVisible();
  });
});
