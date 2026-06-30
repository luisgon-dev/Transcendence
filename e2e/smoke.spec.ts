import { test, expect } from "@playwright/test";

test.describe("Smoke tests", () => {
  test("landing page loads with LoL card", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("link", { name: /lol|league/i })).toBeVisible();
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
});
