export type LolAnalyticsPatchOption = {
  patch: string;
  releasedAtUtc?: string | null;
  detectedAtUtc?: string | null;
  isActive: boolean;
  rankedSoloDuoMatchCount: number;
  matchCount?: number;
  queueFamily?: string;
};

const DEFAULT_VISIBLE_HISTORICAL_PATCH_COUNT = 3;

export function normalizeAnalyticsPatch(patch: string | undefined | null): string | null {
  if (!patch) return null;
  const trimmed = patch.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function getVisibleAnalyticsPatches(
  patches: readonly LolAnalyticsPatchOption[],
  historicalPatchCount = DEFAULT_VISIBLE_HISTORICAL_PATCH_COUNT
): LolAnalyticsPatchOption[] {
  const visibleHistoricalPatchCount = Math.max(0, historicalPatchCount);
  let historicalPatchesShown = 0;

  return patches.filter((patch) => {
    if (patch.isActive) return true;
    if (historicalPatchesShown >= visibleHistoricalPatchCount) return false;

    historicalPatchesShown += 1;
    return true;
  });
}

export function buildPatchPreservingParams(params: Record<string, string | null | undefined>): URLSearchParams {
  const next = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (!value) continue;
    if (value.toLowerCase() === "all") continue;
    next.set(key, value);
  }
  return next;
}
