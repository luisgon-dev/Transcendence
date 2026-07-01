import { test, expect } from "@playwright/test";

test.describe("Summoner profiles", () => {
  test("LoL profile loads with summoner name visible", async ({ page }) => {
    await page.goto("/lol/summoners/na/Kronic-NA1");
    await expect(page.getByText(/Kronic/i)).toBeVisible();
  });

  test("LoL profile shows rank information", async ({ page }) => {
    await page.goto("/lol/summoners/na/Kronic-NA1");
    await expect(page.locator("main")).toBeVisible();
    // Rank section should be present (ranked card or text)
    await expect(
      page.locator("text=/Iron|Bronze|Silver|Gold|Platinum|Emerald|Diamond|Master|Grandmaster|Challenger|Unranked/i").first()
    ).toBeVisible();
  });

  test("LoL profile shows match history section", async ({ page }) => {
    await page.goto("/lol/summoners/na/Kronic-NA1");
    // Match history entries should appear
    await expect(
      page.locator("[href*='/lol/matches/'], [class*='match'], text=/victory|defeat/i").first()
    ).toBeVisible({ timeout: 15_000 });
  });
});
