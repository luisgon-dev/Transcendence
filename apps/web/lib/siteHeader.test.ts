import { describe, expect, it } from "vitest";

import { getGameSwitcherItems } from "@/lib/siteHeader";

describe("getGameSwitcherItems", () => {
  it("keeps both game pills navigable when TFT is enabled", () => {
    const [lol, tft] = getGameSwitcherItems("/tft/comps", true);

    expect(lol.href).toBe("/lol");
    expect(lol.isDisabled).toBe(false);
    expect(lol.comingSoon).toBe(false);
    expect(lol.isActive).toBe(false);

    expect(tft.href).toBe("/tft");
    expect(tft.isDisabled).toBe(false);
    expect(tft.comingSoon).toBe(false);
    expect(tft.isActive).toBe(true);
  });

  it("disables only the TFT pill when the frontend flag is off", () => {
    const [lol, tft] = getGameSwitcherItems("/lol/tierlist", false);

    expect(lol.href).toBe("/lol");
    expect(lol.isDisabled).toBe(false);
    expect(lol.comingSoon).toBe(false);
    expect(lol.isActive).toBe(true);

    expect(tft.href).toBeUndefined();
    expect(tft.isDisabled).toBe(true);
    expect(tft.comingSoon).toBe(true);
    expect(tft.isActive).toBe(false);
  });

  it("does not mark disabled TFT as active on a tft pathname", () => {
    const [, tft] = getGameSwitcherItems("/tft/comps", false);

    expect(tft.isDisabled).toBe(true);
    expect(tft.isActive).toBe(false);
    expect(tft.comingSoon).toBe(true);
  });
});
