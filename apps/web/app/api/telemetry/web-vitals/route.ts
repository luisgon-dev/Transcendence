import { NextResponse } from "next/server";

import { logEvent } from "@/lib/serverLog";

const METRICS = new Set(["CLS", "FCP", "INP", "LCP", "TTFB"]);
const RATINGS = new Set(["good", "needs-improvement", "poor"]);
const ROUTE_PATTERN = /^\/[a-zA-Z0-9/[\]_-]{0,160}$/;

export async function POST(request: Request) {
  if (Number(request.headers.get("content-length") ?? 0) > 4096) {
    return new NextResponse(null, { status: 413 });
  }

  const payload = (await request.json().catch(() => null)) as Record<string, unknown> | null;
  const name = typeof payload?.name === "string" ? payload.name : "";
  const value = typeof payload?.value === "number" ? payload.value : Number.NaN;
  const rating = typeof payload?.rating === "string" ? payload.rating : "";
  const route = typeof payload?.route === "string" ? payload.route : "";
  const navigationType =
    typeof payload?.navigationType === "string" ? payload.navigationType.slice(0, 32) : "unknown";

  if (
    !METRICS.has(name) ||
    !Number.isFinite(value) ||
    value < 0 ||
    value > 10_000_000 ||
    !RATINGS.has(rating) ||
    !ROUTE_PATTERN.test(route)
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

  return new NextResponse(null, { status: 204 });
}
