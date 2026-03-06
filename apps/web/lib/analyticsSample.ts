export type AnalyticsSampleLike =
  | {
      sampleStatus?: unknown;
      sampleSize?: unknown;
      minimumRecommendedSampleSize?: unknown;
      patchAgeHours?: unknown;
      isEarlyPatchWindow?: unknown;
    }
  | null
  | undefined;

export type AnalyticsSampleStatus = "sufficient" | "low_sample" | "no_data";

export type NormalizedAnalyticsSample = {
  status: AnalyticsSampleStatus;
  sampleSize: number;
  minimumRecommendedSampleSize: number;
  patchAgeHours: number;
  isEarlyPatchWindow: boolean;
};

export function analyticsSampleCoveragePercent(
  sample: Pick<NormalizedAnalyticsSample, "sampleSize" | "minimumRecommendedSampleSize">
): number {
  const minimum = Math.max(1, sample.minimumRecommendedSampleSize);
  const ratio = sample.sampleSize / minimum;
  return Math.max(0, Math.min(100, Math.round(ratio * 100)));
}

export function analyticsSampleShortfall(
  sample: Pick<NormalizedAnalyticsSample, "sampleSize" | "minimumRecommendedSampleSize">
): number {
  return Math.max(0, sample.minimumRecommendedSampleSize - sample.sampleSize);
}

export function normalizeAnalyticsSampleStatus(value: unknown): AnalyticsSampleStatus {
  if (typeof value === "number") {
    if (value === 0) return "sufficient";
    if (value === 1) return "low_sample";
    if (value === 2) return "no_data";
  }

  if (typeof value !== "string") return "sufficient";
  const normalized = value.trim().toLowerCase();

  if (normalized === "low_sample" || normalized === "lowsample") return "low_sample";
  if (normalized === "no_data" || normalized === "nodata") return "no_data";
  return "sufficient";
}

function asNumber(value: unknown, fallback = 0): number {
  if (typeof value !== "number" || !Number.isFinite(value)) return fallback;
  return value;
}

export function normalizeAnalyticsSample(
  sample: AnalyticsSampleLike
): NormalizedAnalyticsSample | null {
  if (!sample) return null;

  return {
    status: normalizeAnalyticsSampleStatus(sample.sampleStatus),
    sampleSize: Math.max(0, Math.floor(asNumber(sample.sampleSize, 0))),
    minimumRecommendedSampleSize: Math.max(
      1,
      Math.floor(asNumber(sample.minimumRecommendedSampleSize, 1))
    ),
    patchAgeHours: Math.max(0, asNumber(sample.patchAgeHours, 0)),
    isEarlyPatchWindow: Boolean(sample.isEarlyPatchWindow)
  };
}

function severity(status: AnalyticsSampleStatus): number {
  if (status === "no_data") return 2;
  if (status === "low_sample") return 1;
  return 0;
}

export function pickMostSevereAnalyticsSample(
  ...samples: AnalyticsSampleLike[]
): NormalizedAnalyticsSample | null {
  let best: NormalizedAnalyticsSample | null = null;

  for (const candidateRaw of samples) {
    const candidate = normalizeAnalyticsSample(candidateRaw);
    if (!candidate) continue;
    if (!best || severity(candidate.status) > severity(best.status)) best = candidate;
  }

  return best;
}
