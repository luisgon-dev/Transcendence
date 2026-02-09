import { describe, expect, it } from "vitest";

import { formatDurationSeconds, formatPercent } from "@/lib/format";

describe("formatPercent", () => {
  it("formats ratio inputs as percent", () => {
    expect(formatPercent(0.5234)).toBe("52.3%");
    expect(formatPercent(1)).toBe("100.0%");
  });

  it("formats percent inputs as percent (auto)", () => {
    expect(formatPercent(52.34)).toBe("52.3%");
    expect(formatPercent(0)).toBe("0.0%");
  });

  it("handles invalid inputs", () => {
    expect(formatPercent(undefined)).toBe("-");
    expect(formatPercent(Number.NaN)).toBe("-");
    expect(formatPercent(Number.POSITIVE_INFINITY)).toBe("-");
  });
});

describe("formatDurationSeconds", () => {
  it("formats mm:ss", () => {
    expect(formatDurationSeconds(0)).toBe("0:00");
    expect(formatDurationSeconds(59)).toBe("0:59");
    expect(formatDurationSeconds(60)).toBe("1:00");
    expect(formatDurationSeconds(61)).toBe("1:01");
  });

  it("formats hh:mm:ss", () => {
    expect(formatDurationSeconds(3600)).toBe("1:00:00");
    expect(formatDurationSeconds(3661)).toBe("1:01:01");
  });
});

