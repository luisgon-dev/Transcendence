import { test, expect } from "@playwright/test";

test.describe("Tierlist page", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/lol/tierlist");
  });

  test("table contains champion rows with valid win rates", async ({ page }) => {
    const rows = page.locator("table tbody tr");
    await expect(rows.first()).toBeVisible();

    // Check at least one win rate is between 0-100%
    const winRateCell = rows.first().locator("td").nth(4);
    const text = await winRateCell.textContent();
    const match = text?.match(/([\d.]+)%/);
    expect(match).toBeTruthy();
    const rate = parseFloat(match![1]);
    expect(rate).toBeGreaterThanOrEqual(0);
    expect(rate).toBeLessThanOrEqual(100);
  });

  test("role filter tabs change URL params and update content", async ({ page }) => {
    // Click a role filter (e.g., "Top")
    const topTab = page.getByRole("link", { name: /^top$/i });
    if (await topTab.isVisible()) {
      await topTab.click();
      await expect(page).toHaveURL(/role=TOP/i);
      await expect(page.locator("table tbody tr").first()).toBeVisible();
    }
  });

  test("tier sections display in correct order", async ({ page }) => {
    const tierHeaders = page.locator("[class*='bg-tier']");
    const count = await tierHeaders.count();
    if (count >= 2) {
      const tiers: string[] = [];
      for (let i = 0; i < count; i++) {
        const text = await tierHeaders.nth(i).textContent();
        const tierMatch = text?.match(/Tier ([SABCD])/);
        if (tierMatch) tiers.push(tierMatch[1]);
      }
      const order = ["S", "A", "B", "C", "D"];
      const filtered = order.filter((t) => tiers.includes(t));
      expect(tiers).toEqual(filtered);
    }
  });

  test("champion links navigate to champion detail pages", async ({ page }) => {
    const championLink = page.locator("table tbody tr a[href*='/lol/champions/']").first();
    await expect(championLink).toBeVisible();
    const href = await championLink.getAttribute("href");
    expect(href).toMatch(/\/lol\/champions\/\d+/);
  });

  test("Analyze links navigate to matchups page", async ({ page }) => {
    const analyzeLink = page.locator("table tbody tr a:has-text('Analyze')").first();
    await expect(analyzeLink).toBeVisible();
    const href = await analyzeLink.getAttribute("href");
    expect(href).toMatch(/\/lol\/matchups\/\d+/);
  });

  test("sortable columns respond to click", async ({ page }) => {
    // Click "Win Rate" header to sort
    const winRateHeader = page.locator("th").filter({ hasText: /win rate/i });
    await expect(winRateHeader).toBeVisible();
    await winRateHeader.click();

    // Should show sort indicator
    await expect(winRateHeader.locator("[data-sort-indicator]")).toBeVisible();
  });
});
