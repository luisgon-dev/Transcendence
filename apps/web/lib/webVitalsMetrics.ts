export const WEB_VITAL_NAMES = ["CLS", "FCP", "INP", "LCP", "TTFB"] as const;
export const WEB_VITAL_RATINGS = ["good", "needs-improvement", "poor"] as const;
export const WEB_VITAL_NAVIGATION_TYPES = [
  "navigate",
  "reload",
  "back-forward",
  "back-forward-cache",
  "prerender",
  "restore",
  "unknown"
] as const;

export type WebVitalName = (typeof WEB_VITAL_NAMES)[number];
export type WebVitalRating = (typeof WEB_VITAL_RATINGS)[number];
export type WebVitalNavigationType = (typeof WEB_VITAL_NAVIGATION_TYPES)[number];

type HistogramSeries = {
  count: number;
  sum: number;
  buckets: number[];
};

const DURATION_BUCKETS_MS = [
  100, 200, 300, 500, 800, 1000, 1500, 2000, 2500, 3000, 4000, 5000, 8000, 10_000
] as const;
const CLS_BUCKETS = [0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2, 5] as const;

const nameSet = new Set<string>(WEB_VITAL_NAMES);
const ratingSet = new Set<string>(WEB_VITAL_RATINGS);
const navigationTypeSet = new Set<string>(WEB_VITAL_NAVIGATION_TYPES);

function seriesKey(metric: WebVitalName, route: string) {
  return `${metric}\u0000${route}`;
}

function ratingKey(metric: WebVitalName, route: string, rating: WebVitalRating) {
  return `${metric}\u0000${route}\u0000${rating}`;
}

function escapeLabel(value: string) {
  return value.replaceAll("\\", "\\\\").replaceAll("\n", "\\n").replaceAll('"', '\\"');
}

function labels(values: Record<string, string>) {
  return `{${Object.entries(values)
    .map(([key, value]) => `${key}="${escapeLabel(value)}"`)
    .join(",")}}`;
}

function getOrCreateSeries(
  collection: Map<string, HistogramSeries>,
  key: string,
  bucketCount: number
) {
  const existing = collection.get(key);
  if (existing) return existing;
  const created = { count: 0, sum: 0, buckets: Array.from({ length: bucketCount }, () => 0) };
  collection.set(key, created);
  return created;
}

function renderHistogram(
  lines: string[],
  metricName: string,
  metric: WebVitalName,
  route: string,
  series: HistogramSeries,
  bucketBounds: readonly number[]
) {
  bucketBounds.forEach((upperBound, index) => {
    lines.push(
      `${metricName}_bucket${labels({ metric, route, le: String(upperBound) })} ${series.buckets[index]}`
    );
  });
  lines.push(
    `${metricName}_bucket${labels({ metric, route, le: "+Inf" })} ${series.count}`,
    `${metricName}_sum${labels({ metric, route })} ${series.sum}`,
    `${metricName}_count${labels({ metric, route })} ${series.count}`
  );
}

export function isWebVitalName(value: string): value is WebVitalName {
  return nameSet.has(value);
}

export function isWebVitalRating(value: string): value is WebVitalRating {
  return ratingSet.has(value);
}

export function normalizeWebVitalNavigationType(value: string): WebVitalNavigationType {
  return navigationTypeSet.has(value) ? (value as WebVitalNavigationType) : "unknown";
}

export class WebVitalsMetricsStore {
  private readonly durationSeries = new Map<string, HistogramSeries>();
  private readonly clsSeries = new Map<string, HistogramSeries>();
  private readonly reports = new Map<string, number>();

  record(metric: WebVitalName, value: number, rating: WebVitalRating, route: string) {
    const bucketBounds = metric === "CLS" ? CLS_BUCKETS : DURATION_BUCKETS_MS;
    const collection = metric === "CLS" ? this.clsSeries : this.durationSeries;
    const series = getOrCreateSeries(collection, seriesKey(metric, route), bucketBounds.length);

    series.count += 1;
    series.sum += value;
    bucketBounds.forEach((upperBound, index) => {
      if (value <= upperBound) series.buckets[index] += 1;
    });

    const reportKey = ratingKey(metric, route, rating);
    this.reports.set(reportKey, (this.reports.get(reportKey) ?? 0) + 1);
  }

  renderPrometheus() {
    const lines = [
      "# HELP transcendence_web_vital_reports_total Browser Web Vital reports by bounded metric, route template, and rating.",
      "# TYPE transcendence_web_vital_reports_total counter"
    ];

    for (const [key, count] of [...this.reports.entries()].sort(([a], [b]) =>
      a.localeCompare(b)
    )) {
      const [metric, route, rating] = key.split("\u0000");
      lines.push(
        `transcendence_web_vital_reports_total${labels({ metric, route, rating })} ${count}`
      );
    }

    lines.push(
      "# HELP transcendence_web_vital_duration_milliseconds Browser timing Web Vitals in milliseconds.",
      "# TYPE transcendence_web_vital_duration_milliseconds histogram"
    );
    for (const [key, series] of [...this.durationSeries.entries()].sort(([a], [b]) =>
      a.localeCompare(b)
    )) {
      const [metric, route] = key.split("\u0000") as [WebVitalName, string];
      renderHistogram(
        lines,
        "transcendence_web_vital_duration_milliseconds",
        metric,
        route,
        series,
        DURATION_BUCKETS_MS
      );
    }

    lines.push(
      "# HELP transcendence_web_vital_cls Browser cumulative layout shift values.",
      "# TYPE transcendence_web_vital_cls histogram"
    );
    for (const [key, series] of [...this.clsSeries.entries()].sort(([a], [b]) =>
      a.localeCompare(b)
    )) {
      const [metric, route] = key.split("\u0000") as [WebVitalName, string];
      renderHistogram(lines, "transcendence_web_vital_cls", metric, route, series, CLS_BUCKETS);
    }

    return `${lines.join("\n")}\n`;
  }
}

declare global {
  var __transcendenceWebVitalsMetrics: WebVitalsMetricsStore | undefined;
}

export function getWebVitalsMetricsStore() {
  globalThis.__transcendenceWebVitalsMetrics ??= new WebVitalsMetricsStore();
  return globalThis.__transcendenceWebVitalsMetrics;
}

export function resetWebVitalsMetricsStoreForTests() {
  globalThis.__transcendenceWebVitalsMetrics = new WebVitalsMetricsStore();
}
