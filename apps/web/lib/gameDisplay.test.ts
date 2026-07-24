import { describe, expect, it } from "vitest";

import {
  championDisplayName,
  itemDisplayName,
  runeDisplayName,
  spellDisplayName
} from "@/lib/gameDisplay";

describe("game resource display names", () => {
  it("uses the resource name when static data is available", () => {
    expect(championDisplayName({ name: "Ahri" })).toBe("Ahri");
    expect(itemDisplayName({ name: "Trinity Force" })).toBe("Trinity Force");
    expect(runeDisplayName({ name: "Press the Attack" })).toBe("Press the Attack");
    expect(spellDisplayName({ name: "Flash" })).toBe("Flash");
  });

  it("uses human-readable fallbacks without exposing resource identifiers", () => {
    expect(championDisplayName(null)).toBe("Unknown champion");
    expect(itemDisplayName(undefined)).toBe("Unknown item");
    expect(runeDisplayName({ name: " " })).toBe("Unknown rune");
    expect(spellDisplayName(null)).toBe("Unknown summoner spell");
  });
});
