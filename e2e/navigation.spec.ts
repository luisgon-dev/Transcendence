import { test, expect } from "@playwright/test";

test.describe("Navigation flows", () => {
  test("tierlist -> click champion -> champion detail page", async ({ page }) => {
    await page.goto("/lol/tierlist");
    const championLink = page.locator("table tbody tr a[href*='/lol/champions/']").first();
    await expect(championLink).toBeVisible();
    await championLink.click();
    await expect(page).toHaveURL(/\/lol\/champions\/\d+/);
    await expect(page.locator("main")).toBeVisible();
  });

  test("tierlist -> click Analyze -> matchups page", async ({ page }) => {
    await page.goto("/lol/tierlist");
    const analyzeLink = page.locator("table tbody tr a:has-text('Analyze')").first();
    await expect(analyzeLink).toBeVisible();
    await analyzeLink.click();
    await expect(page).toHaveURL(/\/lol\/matchups\/\d+/);
    await expect(page.locator("main")).toBeVisible();
  });

  test("landing page -> Tier List link -> tierlist loads", async ({ page }) => {
    await page.goto("/");
    const tierListLink = page.getByRole("link", { name: /tier\s*list/i });
    if (await tierListLink.isVisible()) {
      await tierListLink.click();
      await expect(page).toHaveURL(/\/lol\/tierlist/);
      await expect(page.locator("table tbody tr").first()).toBeVisible();
    }
  });
});
