export const PRO_BUILD_ROLES = [
  "ALL",
  "TOP",
  "JUNGLE",
  "MIDDLE",
  "BOTTOM",
  "UTILITY"
] as const;

export type ProBuildRole = (typeof PRO_BUILD_ROLES)[number];

export const PRO_BUILD_SCOPES = ["all", "pro", "highelo"] as const;

export type ProBuildScope = (typeof PRO_BUILD_SCOPES)[number];

export function normalizeProBuildRole(role: string | undefined | null): ProBuildRole {
  if (!role) return "ALL";
  const upper = role.trim().toUpperCase();
  return PRO_BUILD_ROLES.includes(upper as ProBuildRole) ? (upper as ProBuildRole) : "ALL";
}

export function normalizeProBuildScope(scope: string | undefined | null): ProBuildScope {
  if (!scope) return "pro";
  const normalized = scope.trim().toLowerCase();
  return PRO_BUILD_SCOPES.includes(normalized as ProBuildScope) ? (normalized as ProBuildScope) : "pro";
}

export function normalizeProBuildPatch(patch: string | undefined | null): string | null {
  if (!patch) return null;
  const trimmed = patch.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function buildProBuildFilterParams({
  role,
  region,
  scope,
  patch
}: {
  role: ProBuildRole;
  region: string;
  scope?: ProBuildScope;
  patch: string | null;
}): URLSearchParams {
  const params = new URLSearchParams();
  if (role !== "ALL") params.set("role", role);
  if (region !== "ALL") params.set("region", region);
  if (scope && scope !== "pro") params.set("scope", scope);
  if (patch) params.set("patch", patch);
  return params;
}

export function buildProBuildPageHref(
  championId: number,
  filters: {
    role: ProBuildRole;
    region: string;
    scope?: ProBuildScope;
    patch: string | null;
  }
): string {
  const params = buildProBuildFilterParams(filters);
  const qs = params.toString();
  return qs ? `/lol/pro-builds/${championId}?${qs}` : `/lol/pro-builds/${championId}`;
}

/**
 * The entry whose key occurs most often, and how many entries share that key. Entries with a null
 * key are skipped. Ties keep the first one seen; recent pro matches arrive newest first, so a tie
 * goes to the more recent choice.
 */
export function mostCommon<T>(
  entries: readonly T[],
  keyOf: (entry: T) => string | null
): { entry: T; count: number } | null {
  const counts = new Map<string, { entry: T; count: number }>();
  for (const entry of entries) {
    const key = keyOf(entry);
    if (key == null) continue;
    const seen = counts.get(key);
    if (seen) seen.count += 1;
    else counts.set(key, { entry, count: 1 });
  }
  let best: { entry: T; count: number } | null = null;
  for (const candidate of counts.values()) {
    if (!best || candidate.count > best.count) best = candidate;
  }
  return best;
}
