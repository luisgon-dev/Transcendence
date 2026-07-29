import { afterEach, describe, expect, it, vi } from "vitest";

import { analyticsFeatureFlags } from "./analyticsFeatureFlags";

const connection = vi.hoisted(() => vi.fn(async () => undefined));

vi.mock("next/server", () => ({ connection }));

afterEach(() => {
  connection.mockClear();
  vi.unstubAllEnvs();
});

describe("analyticsFeatureFlags", () => {
  it("reads the flags inside the request so a restart can flip them", async () => {
    vi.stubEnv("TRN_FEATURE_BUILD_LAB", "true");

    await analyticsFeatureFlags();

    // Without connection() the read would be prerendered into the static shell at image-build
    // time, and the container env could never turn the flag on.
    expect(connection).toHaveBeenCalledTimes(1);
  });

  it("treats only an explicit true as enabled", async () => {
    vi.stubEnv("TRN_FEATURE_BUILD_LAB", " TRUE ");
    vi.stubEnv("TRN_FEATURE_CHAMPION_RECOMMENDATIONS", "1");
    vi.stubEnv("TRN_FEATURE_BUILD_REFERENCE_LINKS", "");

    expect(await analyticsFeatureFlags()).toEqual({
      buildLab: true,
      championRecommendations: false,
      buildReferenceLinks: false
    });
  });

  it("keeps every surface off when nothing is configured", async () => {
    vi.stubEnv("TRN_FEATURE_BUILD_LAB", undefined);
    vi.stubEnv("TRN_FEATURE_CHAMPION_RECOMMENDATIONS", undefined);
    vi.stubEnv("TRN_FEATURE_BUILD_REFERENCE_LINKS", undefined);

    expect(await analyticsFeatureFlags()).toEqual({
      buildLab: false,
      championRecommendations: false,
      buildReferenceLinks: false
    });
  });
});
