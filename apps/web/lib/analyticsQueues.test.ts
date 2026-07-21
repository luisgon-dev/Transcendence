import { describe, expect, it } from "vitest";

import { analyticsQueueOption, normalizeAnalyticsQueue } from "./analyticsQueues";

describe("analytics queues", () => {
  it.each(["solo", "aram", "arena", "flex"])("accepts %s", (queue) => {
    expect(normalizeAnalyticsQueue(queue)).toBe(queue);
  });

  it("falls back safely to solo", () => {
    expect(normalizeAnalyticsQueue("normal")).toBe("solo");
  });

  it("marks modes without lane roles", () => {
    expect(analyticsQueueOption("aram").hasRoles).toBe(false);
    expect(analyticsQueueOption("flex").hasRoles).toBe(true);
  });
});
