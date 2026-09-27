import { describe, expect, it } from "vitest";

import { shouldReportMetric } from "./WebVitalsReporter";

describe("WebVitalsReporter", () => {
  it("sends only the metrics the collector accepts", () => {
    for (const name of ["CLS", "FCP", "INP", "LCP", "TTFB"]) expect(shouldReportMetric(name)).toBe(true);
  });

  it("drops FID and Next.js's own timings, which the collector rejects", () => {
    for (const name of ["Next.js-hydration", "Next.js-route-change-to-render", "Next.js-render", "FID"]) {
      expect(shouldReportMetric(name)).toBe(false);
    }
  });
});
