import { expect, test } from "@playwright/test";
import { createRequire } from "node:module";
const resolveDependency = createRequire(`${process.cwd()}/package.json`);
const axePath = resolveDependency.resolve("axe-core/axe.min.js");

for (const theme of ["light", "dark"]) {
  for (const [label, path] of [["home", "/"], ["tierlist", "/lol/tierlist"], ["champion", "/lol/champions/103"], ["profile", "/lol/summoners/na/faker-NA1"], ["ranked profile", "/lol/summoners/na/Kronic-NA1"]]) {
    test(`${theme} ${label} passes accessibility and mobile overflow checks`, async ({ page }) => {
      await page.setViewportSize({ width: theme === "dark" ? 390 : 1365, height: 900 });
      await page.addInitScript(value => localStorage.setItem("theme", value), theme);
      await page.goto(path, { waitUntil: "domcontentloaded" });
      await page.evaluate(() => document.fonts.ready);
      if (label === "ranked profile") await expect(page.getByRole("button", { name: /^(Victory|Defeat)\b/i }).first()).toBeVisible();
      if (label === "tierlist" && process.env.LOCAL_OPERATION_BACKEND === "seeded") await expect(page.locator("table tbody tr").first()).toBeVisible();
      await expect(page.locator("h1").first()).toBeVisible();
      await page.addScriptTag({ path: axePath });
      const failures = await page.evaluate(async () => {
        const axe = (window as unknown as { axe: { run: (target: Document, options: unknown) => Promise<{ violations: Array<{ id: string; nodes: Array<{ target: string[]; failureSummary: string }> }> }> } }).axe;
        const result = await axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
        return result.violations.map(({ id, nodes }) => ({ id, nodes: nodes.map(({ target, failureSummary }) => ({ target, failureSummary })) }));
      });
      expect(failures).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1);
    });
  }
}

test("scouting links preserve region, identity and queue for keyboard navigation", async ({ page }) => {
  await page.goto("/lol/live?region=euw&riotId=Test%23EUW&queue=family%3ARANKED_SOLO_DUO");
  const profile = page.getByRole("link", { name: "Player profile", exact: true });
  await expect(profile).toHaveAttribute("href", /\/lol\/summoners\/euw\/Test-EUW\?queue=family%3ARANKED_SOLO_DUO/);
  const lobby = page.getByRole("link", { name: "Scout lobby", exact: true });
  await lobby.focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("textbox", { name: "Lobby Riot IDs" })).toHaveValue("Test#EUW");
  await expect(page.getByRole("combobox", { name: "Lobby region" })).toHaveText("EUW");
});

test("image failures offer a user retry and recover without reloading the page", async ({ page }) => {
  let fail = true;
  await page.route("**/_next/image?*", route => fail ? route.fulfill({ status: 503, body: "temporary outage" }) : route.continue());
  await page.goto("/lol/summoners/na/faker-NA1");
  const retry = page.getByRole("button", { name: "Retry images", exact: true });
  await expect(retry).toBeVisible();
  fail = false;
  await retry.click();
  await expect(retry).toHaveCount(0, { timeout: 20_000 });
});
