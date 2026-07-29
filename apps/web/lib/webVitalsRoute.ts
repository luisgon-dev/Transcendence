const ROUTE_REPLACEMENTS: Array<[RegExp, string]> = [
  [
    /^\/lol\/summoners\/[^/]+\/[^/]+\/matches\/[^/]+/,
    "/lol/summoners/[region]/[riotId]/matches/[matchId]"
  ],
  [
    /^\/lol\/summoners\/[^/]+\/[^/]+\/matches/,
    "/lol/summoners/[region]/[riotId]/matches"
  ],
  [/^\/lol\/summoners\/[^/]+\/[^/]+/, "/lol/summoners/[region]/[riotId]"],
  // Must precede /lol/builds/[championId]: that pattern also matches a share link's first segment.
  [/^\/lol\/builds\/shared\/[^/]+/, "/lol/builds/shared/[shareId]"],
  [/^\/lol\/builds\/[^/]+/, "/lol/builds/[championId]"],
  [/^\/lol\/champions\/[^/]+/, "/lol/champions/[championId]"],
  [/^\/lol\/pro-builds\/[^/]+/, "/lol/pro-builds/[championId]"],
  [/^\/lol\/items\/[^/]+/, "/lol/items/[itemId]"],
  [/^\/lol\/runes\/[^/]+/, "/lol/runes/[runeId]"],
  [/^\/admin\/jobs\/[^/]+/, "/admin/jobs/[jobId]"]
];

const KNOWN_STATIC_ROUTES = new Set([
  "/",
  "/account/favorites",
  "/account/forgot-password",
  "/account/login",
  "/account/register",
  "/account/reset-password",
  "/account/saved-builds",
  "/admin",
  "/admin/analytics/build-lab",
  "/admin/api-keys",
  "/admin/audit",
  "/admin/jobs",
  "/admin/logs",
  "/admin/pro-summoners",
  "/lol",
  "/lol/builds",
  "/lol/champions",
  "/lol/items",
  "/lol/leaderboards",
  "/lol/live",
  "/lol/multi-search",
  "/lol/pro-builds",
  "/lol/runes",
  "/lol/tierlist",
  "/terms"
]);

export function webVitalsRouteTemplate(pathname: string) {
  for (const [pattern, replacement] of ROUTE_REPLACEMENTS) {
    if (pattern.test(pathname)) return replacement;
  }
  return KNOWN_STATIC_ROUTES.has(pathname) ? pathname : "/_other";
}

export function isWebVitalsRouteTemplate(value: string) {
  return webVitalsRouteTemplate(value) === value;
}
