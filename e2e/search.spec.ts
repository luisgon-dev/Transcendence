import { expect, test } from "@playwright/test";

test("the first search click loads the dialog, restores focus, and supports reopening", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });
  const trigger = page.getByRole("button", { name: "Open search", exact: true });
  await trigger.click();
  const input = page.getByRole("combobox", { name: "Global search input" });
  await expect(input).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(input).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await page.keyboard.press("Control+k");
  await expect(input).toBeFocused();
  await input.fill("Kronic#NA1");
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/\/lol\/summoners\/na\/Kronic-NA1/);
});

test("the first keyboard shortcut opens search without a prior click", async ({ page }) => {
  await page.goto("/", { waitUntil: "domcontentloaded" });
  // Focusing the hydrated launcher establishes the same interaction boundary as a keyboard user.
  await page.getByRole("button", { name: "Open search", exact: true }).focus();
  await page.keyboard.press("Control+k");
  await expect(page.getByRole("combobox", { name: "Global search input" })).toBeFocused();
});
