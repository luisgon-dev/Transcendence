import { beforeEach, describe, expect, it } from "vitest";

import {
  getWebVitalsMetricsStore,
  normalizeWebVitalNavigationType,
  resetWebVitalsMetricsStoreForTests
} from "@/lib/webVitalsMetrics";

describe("WebVitalsMetricsStore", () => {
  beforeEach(() => {
    resetWebVitalsMetricsStoreForTests();
  });

  it("exports cumulative timing buckets with bounded route and rating labels", () => {
    const store = getWebVitalsMetricsStore();
    store.record("LCP", 2400, "good", "/lol/champions/[championId]");
    store.record("LCP", 3200, "needs-improvement", "/lol/champions/[championId]");

    const metrics = store.renderPrometheus();

    expect(metrics).toContain(
      'transcendence_web_vital_duration_milliseconds_bucket{metric="LCP",route="/lol/champions/[championId]",le="2500"} 1'
    );
    expect(metrics).toContain(
      'transcendence_web_vital_duration_milliseconds_bucket{metric="LCP",route="/lol/champions/[championId]",le="+Inf"} 2'
    );
    expect(metrics).toContain(
      'transcendence_web_vital_reports_total{metric="LCP",route="/lol/champions/[championId]",rating="needs-improvement"} 1'
    );
  });

  it("keeps CLS in its dimensionless histogram", () => {
    getWebVitalsMetricsStore().record("CLS", 0.08, "good", "/");

    const metrics = getWebVitalsMetricsStore().renderPrometheus();

    expect(metrics).toContain(
      'transcendence_web_vital_cls_bucket{metric="CLS",route="/",le="0.1"} 1'
    );
    expect(metrics).not.toContain(
      'transcendence_web_vital_duration_milliseconds_bucket{metric="CLS"'
    );
  });

  it("normalizes unknown navigation types instead of creating cardinality", () => {
    expect(normalizeWebVitalNavigationType("navigate")).toBe("navigate");
    expect(normalizeWebVitalNavigationType("user-controlled-value")).toBe("unknown");
  });
});
