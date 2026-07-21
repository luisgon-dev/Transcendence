import { describe, expect, it } from "vitest";

import {
  buildResourceHref,
  filterAndSortBuildResources,
  normalizeBuildResourceSort,
  type BuildResourceEntry
} from "@/lib/buildResources";

const entries: BuildResourceEntry[] = [
  { resourceId: 2, name: "B Item", games: 200, wins: 100, winRate: 0.5, pickRate: 0.2, topChampions: [] },
  { resourceId: 1, name: "A Item", games: 100, wins: 60, winRate: 0.6, pickRate: 0.1, topChampions: [] }
];

describe("build resource helpers", () => {
  it("filters by name or numeric id and applies the requested sort", () => {
    expect(filterAndSortBuildResources(entries, "1", "popular").map((entry) => entry.resourceId)).toEqual([1]);
    expect(filterAndSortBuildResources(entries, undefined, "winrate").map((entry) => entry.resourceId)).toEqual([1, 2]);
    expect(filterAndSortBuildResources(entries, undefined, "name").map((entry) => entry.resourceId)).toEqual([1, 2]);
  });

  it("normalizes unsupported sorts and preserves regional detail links", () => {
    expect(normalizeBuildResourceSort("unexpected")).toBe("popular");
    expect(buildResourceHref("runes", 8005, "KR")).toBe("/lol/runes/8005?region=KR");
    expect(buildResourceHref("items", null, "ALL")).toBe("/lol/items");
  });
});
