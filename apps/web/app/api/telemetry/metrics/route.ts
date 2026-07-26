import { headers } from "next/headers";

import { getWebVitalsMetricsStore } from "@/lib/webVitalsMetrics";

export async function GET() {
  // A runtime API opts this GET handler out of Cache Components prerendering. The metrics are mutable
  // process state and must be rendered at scrape time, not frozen as an empty build artifact.
  await headers();
  return new Response(getWebVitalsMetricsStore().renderPrometheus(), {
    status: 200,
    headers: {
      "cache-control": "no-store",
      "content-type": "text/plain; version=0.0.4; charset=utf-8"
    }
  });
}
