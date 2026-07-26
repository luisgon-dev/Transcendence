import { describe, expect, it } from "vitest";

import { formatQueueLabel } from "@/lib/queues";

describe("formatQueueLabel", () => {
  it("normalizes known queue ids", () => {
    expect(formatQueueLabel("420")).toBe("Ranked Solo/Duo");
    expect(formatQueueLabel("450")).toBe("ARAM");
    expect(formatQueueLabel("480")).toBe("Swiftplay");
    expect(formatQueueLabel("QUEUE_480")).toBe("Swiftplay");
    expect(formatQueueLabel(undefined, 490)).toBe("Quickplay");
    expect(formatQueueLabel("900")).toBe("ARURF");
  });

  it("normalizes known queue tokens", () => {
    expect(formatQueueLabel("RANKED_SOLO_DUO")).toBe("Ranked Solo/Duo");
    expect(formatQueueLabel("normal_sr")).toBe("Normal (SR)");
    expect(formatQueueLabel("ALL")).toBe("All Queues");
  });

  it("handles unknown values safely", () => {
    expect(formatQueueLabel("12345")).toBe("Other mode");
    expect(formatQueueLabel("QUEUE_12345")).toBe("Other mode");
    expect(formatQueueLabel("ultra_brawl_mode")).toBe("Ultra Brawl Mode");
    expect(formatQueueLabel("")).toBe("Other mode");
  });
});
