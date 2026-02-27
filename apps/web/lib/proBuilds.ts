export const PRO_BUILD_ROLES = [
  "ALL",
  "TOP",
  "JUNGLE",
  "MIDDLE",
  "BOTTOM",
  "UTILITY"
] as const;

export type ProBuildRole = (typeof PRO_BUILD_ROLES)[number];

export const PRO_BUILD_REGIONS = ["ALL", "KR", "EUW", "NA", "CN"] as const;

export type ProBuildRegion = (typeof PRO_BUILD_REGIONS)[number];

export function normalizeProBuildRole(role: string | undefined | null): ProBuildRole {
  if (!role) return "ALL";
  const upper = role.trim().toUpperCase();
  return PRO_BUILD_ROLES.includes(upper as ProBuildRole) ? (upper as ProBuildRole) : "ALL";
}

export function normalizeProBuildRegion(
  region: string | undefined | null
): ProBuildRegion {
  if (!region) return "ALL";
  const upper = region.trim().toUpperCase();
  return PRO_BUILD_REGIONS.includes(upper as ProBuildRegion)
    ? (upper as ProBuildRegion)
    : "ALL";
}

export function normalizeProBuildPatch(patch: string | undefined | null): string | null {
  if (!patch) return null;
  const trimmed = patch.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function buildProBuildFilterParams({
  role,
  region,
  patch
}: {
  role: ProBuildRole;
  region: ProBuildRegion;
  patch: string | null;
}): URLSearchParams {
  const params = new URLSearchParams();
  if (role !== "ALL") params.set("role", role);
  if (region !== "ALL") params.set("region", region);
  if (patch) params.set("patch", patch);
  return params;
}

export function buildProBuildPageHref(
  championId: number,
  filters: {
    role: ProBuildRole;
    region: ProBuildRegion;
    patch: string | null;
  }
): string {
  const params = buildProBuildFilterParams(filters);
  const qs = params.toString();
  return qs ? `/pro-builds/${championId}?${qs}` : `/pro-builds/${championId}`;
}
