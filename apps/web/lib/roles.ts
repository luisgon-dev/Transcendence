/**
 * The five playable lanes, in ladder order. Used for lane/role tab selectors
 * site-wide. There is intentionally no "ALL" entry — an "All lanes" tab does
 * not make sense in most contexts (a champion is played in a specific lane).
 * Pages that aggregate across lanes do so via the absence of a `role` param,
 * not a selectable tab.
 */
export const LANE_ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

export function roleDisplayLabel(role: string | null | undefined): string {
  if (!role) return "Unknown";

  const normalized = role.trim().toUpperCase();
  if (normalized.length === 0) return "Unknown";

  const labels: Record<string, string> = {
    ALL: "All",
    TOP: "Top",
    JUNGLE: "Jungle",
    MIDDLE: "Middle",
    BOTTOM: "Bottom",
    UTILITY: "Support",
    SUPPORT: "Support",
    UNKNOWN: "Unknown"
  };

  return labels[normalized] ?? "Unknown";
}
