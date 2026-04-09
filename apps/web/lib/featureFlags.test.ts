import { describe, expect, it } from "vitest";

import { parseTftReleaseStage } from "@/lib/featureFlags";

describe("parseTftReleaseStage", () => {
  it("defaults to off for empty or unsupported values", () => {
    expect(parseTftReleaseStage(undefined)).toBe("off");
    expect(parseTftReleaseStage("")).toBe("off");
    expect(parseTftReleaseStage("preview")).toBe("off");
  });

  it("accepts supported release stages", () => {
    expect(parseTftReleaseStage("internal")).toBe("internal");
    expect(parseTftReleaseStage("beta")).toBe("beta");
    expect(parseTftReleaseStage("public")).toBe("public");
  });

  it("normalizes casing and whitespace", () => {
    expect(parseTftReleaseStage("  PUBLIC  ")).toBe("public");
    expect(parseTftReleaseStage(" Beta ")).toBe("beta");
  });
});
