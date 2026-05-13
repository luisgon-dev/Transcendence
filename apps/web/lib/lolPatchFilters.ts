export type LolAnalyticsPatchOption = {
  patch: string;
  releasedAtUtc?: string | null;
  detectedAtUtc?: string | null;
  isActive: boolean;
  rankedSoloDuoMatchCount: number;
};

export function normalizeAnalyticsPatch(patch: string | undefined | null): string | null {
  if (!patch) return null;
  const trimmed = patch.trim();
  return trimmed.length > 0 ? trimmed : null;
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
