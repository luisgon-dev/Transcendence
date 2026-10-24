import { describe, expect, it } from "vitest";

import {
  buildProBuildFilterParams,
  buildProBuildPageHref,
  mostCommon,
  normalizeProBuildPatch,
  normalizeProBuildRole,
  normalizeProBuildScope
} from "@/lib/proBuilds";

describe("mostCommon", () => {
  const key = (entry: { k: string | null }) => entry.k;

  it("returns the most frequent key with its count", () => {
    const result = mostCommon([{ k: "a" }, { k: "b" }, { k: "b" }, { k: "a" }, { k: "b" }], key);
    expect(result?.entry.k).toBe("b");
    expect(result?.count).toBe(3);
  });

  it("breaks ties toward the first entry seen and skips null keys", () => {
    const result = mostCommon([{ k: null }, { k: "x" }, { k: "y" }, { k: null }], key);
    expect(result?.entry.k).toBe("x");
    expect(result?.count).toBe(1);
  });

  it("returns null when nothing has a key", () => {
    expect(mostCommon([], key)).toBeNull();
    expect(mostCommon([{ k: null }], key)).toBeNull();
  });
});

describe("normalizeProBuildRole", () => {
  it("normalizes valid role values", () => {
    expect(normalizeProBuildRole("top")).toBe("TOP");
    expect(normalizeProBuildRole("ALL")).toBe("ALL");
  });

  it("falls back to ALL for invalid or missing values", () => {
    expect(normalizeProBuildRole(undefined)).toBe("ALL");
    expect(normalizeProBuildRole("adc")).toBe("ALL");
  });
});

describe("normalizeProBuildPatch", () => {
  it("trims patch values", () => {
    expect(normalizeProBuildPatch(" 14.5 ")).toBe("14.5");
  });

  it("returns null when empty", () => {
    expect(normalizeProBuildPatch("")).toBeNull();
    expect(normalizeProBuildPatch("   ")).toBeNull();
  });
});

describe("normalizeProBuildScope", () => {
  it("normalizes valid scope values", () => {
    expect(normalizeProBuildScope("PRO")).toBe("pro");
    expect(normalizeProBuildScope(" highelo ")).toBe("highelo");
  });

  it("falls back to pro for invalid or missing values", () => {
    expect(normalizeProBuildScope(undefined)).toBe("pro");
    expect(normalizeProBuildScope("pros")).toBe("pro");
  });
});

describe("buildProBuildFilterParams", () => {
  it("only includes non-default filters", () => {
    const params = buildProBuildFilterParams({
      role: "ALL",
      region: "ALL",
      patch: null
    });

    expect(params.toString()).toBe("");
  });

  it("serializes selected filters", () => {
    const params = buildProBuildFilterParams({
      role: "MIDDLE",
      region: "KR",
      scope: "all",
      patch: "14.5"
    });

    expect(params.toString()).toBe("role=MIDDLE&region=KR&scope=all&patch=14.5");
  });
});

describe("buildProBuildPageHref", () => {
  it("builds href with filters", () => {
    expect(
      buildProBuildPageHref(222, { role: "MIDDLE", region: "KR", scope: "highelo", patch: "14.5" })
    ).toBe("/lol/pro-builds/222?role=MIDDLE&region=KR&scope=highelo&patch=14.5");
  });

  it("omits query string when all filters are default", () => {
    expect(buildProBuildPageHref(222, { role: "ALL", region: "ALL", patch: null })).toBe(
      "/lol/pro-builds/222"
    );
  });
});
