"use client";

import { useReportWebVitals } from "next/web-vitals";

import { webVitalsRouteTemplate } from "@/lib/webVitalsRoute";

function reportMetric(metric: Parameters<typeof useReportWebVitals>[0] extends (
  metric: infer T
) => unknown
  ? T
  : never) {
  const body = JSON.stringify({
    name: metric.name,
    value: metric.value,
    rating: metric.rating,
    navigationType: metric.navigationType,
    route: webVitalsRouteTemplate(window.location.pathname)
  });

  if (navigator.sendBeacon) {
    const accepted = navigator.sendBeacon(
      "/api/telemetry/web-vitals",
      new Blob([body], { type: "application/json" })
    );
    if (accepted) return;
  }

  void fetch("/api/telemetry/web-vitals", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body,
    keepalive: true
  });
}

export function WebVitalsReporter() {
  useReportWebVitals(reportMetric);
  return null;
}
