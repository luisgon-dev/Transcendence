import { test, expect } from "@playwright/test";

test.describe("Summoner profiles", () => {
  test("LoL profile loads with summoner name visible", async ({ page }) => {
    await page.goto("/lol/summoners/na/Kronic-NA1");
    await expect(page.getByRole("heading", { name: /Kronic/i }).first()).toBeVisible();
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
    // Each match row is a button labelled with its outcome, e.g. "Victory on Ahri. KDA 5/2/9. 31:04."
    await expect(
      page.getByRole("button", { name: /^(Victory|Defeat) on /i }).first()
    ).toBeVisible({ timeout: 15_000 });
  });
});
