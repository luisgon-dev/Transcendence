"use client";

import { useReportWebVitals } from "next/web-vitals";

import { webVitalsRouteTemplate } from "@transcendence/web-routes";

import { isWebVitalName } from "@/lib/webVitalsMetrics";

/**
 * useReportWebVitals reports more than the collector accepts: FID -- retired in favour of INP, but
 * still emitted after the first click, which is what the live 400s were -- and in some modes
 * Next.js's own timings ("Next.js-hydration", ...). Anything but a current Core Web Vital is not sent.
 */
export function shouldReportMetric(name: string) {
  return isWebVitalName(name);
}

function reportMetric(metric: Parameters<typeof useReportWebVitals>[0] extends (
  metric: infer T
) => unknown
  ? T
  : never) {
  if (!shouldReportMetric(metric.name)) return;
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
