import { describe, expect, it } from "vitest";

import { normalizeSearchText, searchMatchScore } from "@/lib/searchNormalization";

describe("normalizeSearchText", () => {
  it.each([
    ["Kai'Sa", "kaisa"],
    ["Dr. Mundo", "drmundo"],
    ["Kha'Zix", "khazix"],
    ["Kog'Maw", "kogmaw"],
    ["Rek'Sai", "reksai"],
    ["Bel'Veth", "belveth"]
  ])("normalizes %s to %s", (input, expected) => {
    expect(normalizeSearchText(input)).toBe(expected);
  });
});

describe("searchMatchScore", () => {
  it("ranks exact normalized matches ahead of prefixes and substrings", () => {
    expect(searchMatchScore("Kai'Sa", "kaisa")).toBe(0);
    expect(searchMatchScore("Kassadin", "kas")).toBe(1);
    expect(searchMatchScore("Dr. Mundo", "mundo")).toBe(2);
    expect(searchMatchScore("Miss Fortune", "fortune")).toBe(2);
    expect(searchMatchScore("Twisted Fate", "sted")).toBe(3);
    expect(searchMatchScore("Ahri", "jinx")).toBeNull();
  });
});
