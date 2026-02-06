import { describe, expect, it } from "vitest";

import { computeNextPollDelayMs } from "@/lib/polling";

describe("computeNextPollDelayMs", () => {
  it("uses retry-after when provided", () => {
    expect(computeNextPollDelayMs(2000, 5)).toBe(5000);
  });

  it("backs off and clamps to max", () => {
    expect(computeNextPollDelayMs(9000)).toBe(10000);
    expect(computeNextPollDelayMs(10000)).toBe(10000);
  });
});

