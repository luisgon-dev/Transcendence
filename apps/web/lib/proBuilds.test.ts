import { describe, expect, it } from "vitest";

import {
  buildProBuildFilterParams,
  buildProBuildPageHref,
  normalizeProBuildPatch,
  normalizeProBuildRegion,
  normalizeProBuildRole
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

describe("normalizeProBuildRegion", () => {
  it("normalizes valid region values", () => {
    expect(normalizeProBuildRegion("kr")).toBe("KR");
    expect(normalizeProBuildRegion("EUW")).toBe("EUW");
  });

  it("falls back to ALL for invalid or missing values", () => {
    expect(normalizeProBuildRegion(undefined)).toBe("ALL");
    expect(normalizeProBuildRegion("OCE")).toBe("ALL");
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
      patch: "14.5"
    });

    expect(params.toString()).toBe("role=MIDDLE&region=KR&patch=14.5");
  });
});

describe("buildProBuildPageHref", () => {
  it("builds href with filters", () => {
    expect(
      buildProBuildPageHref(222, { role: "MIDDLE", region: "KR", patch: "14.5" })
    ).toBe("/pro-builds/222?role=MIDDLE&region=KR&patch=14.5");
  });

  it("omits query string when all filters are default", () => {
    expect(buildProBuildPageHref(222, { role: "ALL", region: "ALL", patch: null })).toBe(
      "/pro-builds/222"
    );
  });
});
