import { NextResponse } from "next/server";

import { logEvent } from "@/lib/serverLog";
import { isWebVitalsRouteTemplate } from "@transcendence/web-routes";
import {
  getWebVitalsMetricsStore,
  isWebVitalName,
  isWebVitalRating,
  normalizeWebVitalNavigationType
} from "@/lib/webVitalsMetrics";

export async function POST(request: Request) {
  // Synthetic browsers execute the same reporter. Keep lab runs out of visitor percentiles,
  // including older clients that have not yet loaded the webdriver check.
  if (/HeadlessChrome|Lighthouse/i.test(request.headers.get("user-agent") ?? "")) {
    return new NextResponse(null, { status: 204 });
  }
  if (Number(request.headers.get("content-length") ?? 0) > 4096) {
    return new NextResponse(null, { status: 413 });
  }

  const payload = (await request.json().catch(() => null)) as Record<string, unknown> | null;
  const name = typeof payload?.name === "string" ? payload.name : "";
  const value = typeof payload?.value === "number" ? payload.value : Number.NaN;
  const rating = typeof payload?.rating === "string" ? payload.rating : "";
  const route = typeof payload?.route === "string" ? payload.route : "";
  const navigationType = normalizeWebVitalNavigationType(
    typeof payload?.navigationType === "string" ? payload.navigationType : "unknown"
  );

  if (
    !isWebVitalName(name) ||
    !Number.isFinite(value) ||
    value < 0 ||
    value > 10_000_000 ||
    !isWebVitalRating(rating) ||
    !isWebVitalsRouteTemplate(route)
  ) {
    return NextResponse.json({ message: "Invalid metric." }, { status: 400 });
  }

  logEvent("info", "web vital", {
    metric: name,
    value,
    rating,
    route,
    navigationType
  });
  getWebVitalsMetricsStore().record(name, value, rating, route);

  return new NextResponse(null, { status: 204 });
}
