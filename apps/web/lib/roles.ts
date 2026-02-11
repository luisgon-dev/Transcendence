export function roleDisplayLabel(role: string | null | undefined): string {
  if (!role) return "Unknown";

  const normalized = role.trim().toUpperCase();
  if (normalized.length === 0) return "Unknown";

  if (normalized === "UTILITY") return "Support";
  if (normalized === "UNKNOWN") return "Unknown";

  return normalized;
}
