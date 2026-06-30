// Allowlist for the anonymous public BFF proxy (`/api/trn/public/[...path]`).
//
// The public proxy forwards to the backend WITHOUT any credentials (no session
// cookie, no API key). Without an allowlist it would relay anonymous traffic to
// *any* `/api/*` backend route. We restrict it to the only surface the public
// frontend actually uses: LoL summoner reads, plus the refresh POST that
// kicks off an on-demand profile refresh.
//
// `normalizeProxyPath` (lib/proxyPath.ts) already rejects path traversal
// (`.`/`..`/embedded separators); this is the orthogonal "which routes" gate.

const PUBLIC_NAMESPACES = new Set(["lol"]);

/**
 * Returns true if the given method + path segments are part of the intended
 * anonymous public surface. Everything else (admin, user, auth, analytics
 * writes, arbitrary paths, PUT/DELETE) is rejected by the route handler.
 */
export function isAllowedPublicProxyPath(method: string, path: string[]): boolean {
  // `lol/summoners/...`
  if (path.length < 2) return false;
  if (!PUBLIC_NAMESPACES.has(path[0])) return false;
  if (path[1] !== "summoners") return false;

  // Summoner reads.
  if (method === "GET") return true;

  // The only mutating public action is the on-demand refresh: `.../refresh`.
  if (method === "POST") return path[path.length - 1] === "refresh";

  return false;
}
