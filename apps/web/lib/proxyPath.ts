export function normalizeProxyPath(path: string[]): string[] | null {
  if (!Array.isArray(path) || path.length === 0) return null;

  const normalized: string[] = [];
  for (const raw of path) {
    const segment = raw.trim();
    if (!segment) return null;
    if (segment === "." || segment === "..") return null;
    if (segment.includes("/") || segment.includes("\\")) return null;
    normalized.push(segment);
  }

  return normalized.length > 0 ? normalized : null;
}
