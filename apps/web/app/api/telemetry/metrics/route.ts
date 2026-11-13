import { getJourneyMetrics } from "@/lib/journeyMetrics";
import { headers } from "next/headers";

import { getWebVitalsMetricsStore } from "@/lib/webVitalsMetrics";
import { getAssetReadinessProbe } from "@/lib/assetReadiness";

export async function GET() {
  // A runtime API opts this GET handler out of Cache Components prerendering. The metrics are mutable
  // process state and must be rendered at scrape time, not frozen as an empty build artifact.
  await headers();
  const assets = getAssetReadinessProbe();
  // Scrapes remain fast; a single bounded background probe refreshes shared process state.
  void assets.get();
  return new Response(getWebVitalsMetricsStore().renderPrometheus() + assets.renderPrometheus() + getJourneyMetrics().renderPrometheus(), {
    status: 200,
    headers: {
      "cache-control": "no-store",
      "content-type": "text/plain; version=0.0.4; charset=utf-8"
    }
  });
}
