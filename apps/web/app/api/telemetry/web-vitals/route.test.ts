import { beforeEach, describe, expect, it } from "vitest";

import { POST } from "@/app/api/telemetry/web-vitals/route";
import {
  getWebVitalsMetricsStore,
  resetWebVitalsMetricsStoreForTests
} from "@/lib/webVitalsMetrics";

function request(body: Record<string, unknown>, headers: HeadersInit = {}) {
  return new Request("http://localhost/api/telemetry/web-vitals", {
    method: "POST",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify(body)
  });
}

describe("POST /api/telemetry/web-vitals", () => {
  beforeEach(() => {
    resetWebVitalsMetricsStoreForTests();
  });

  it("records a valid bounded metric", async () => {
    const response = await POST(
      request({
        name: "INP",
        value: 145,
        rating: "good",
        route: "/lol/summoners/[region]/[riotId]",
        navigationType: "navigate"
      })
    );

    expect(response.status).toBe(204);
    expect(getWebVitalsMetricsStore().renderPrometheus()).toContain(
      'transcendence_web_vital_reports_total{metric="INP",route="/lol/summoners/[region]/[riotId]",rating="good"} 1'
    );
  });

  it("rejects unbounded metrics and routes", async () => {
    const response = await POST(
      request({
        name: "custom-metric",
        value: 1,
        rating: "good",
        route: "/lol/summoners/Kronic-NA1",
        navigationType: "custom"
      })
    );

    expect(response.status).toBe(400);
    expect(getWebVitalsMetricsStore().renderPrometheus()).not.toContain(
      "transcendence_web_vital_reports_total{"
    );
  });

  it("rejects oversized payloads before parsing", async () => {
    const response = await POST(request({}, { "content-length": "4097" }));
    expect(response.status).toBe(413);
  });
});
