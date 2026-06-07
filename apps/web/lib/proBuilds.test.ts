import { describe, expect, it } from "vitest";

import {
  buildProBuildFilterParams,
  buildProBuildPageHref,
  normalizeProBuildPatch,
  normalizeProBuildRole,
  normalizeProBuildScope
} from "@/lib/proBuilds";

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

  it("falls back to all for invalid or missing values", () => {
    expect(normalizeProBuildScope(undefined)).toBe("all");
    expect(normalizeProBuildScope("pros")).toBe("all");
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
      scope: "pro",
      patch: "14.5"
    });

    expect(params.toString()).toBe("role=MIDDLE&region=KR&scope=pro&patch=14.5");
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
