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
