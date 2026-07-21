export function isAllowedAppProxyPath(method: string, path: string[]): boolean {
  if (
    method === "POST" &&
    path.length === 3 &&
    path[0] === "lol" &&
    path[1] === "summoners" &&
    path[2] === "multi-search"
  ) {
    return true;
  }

  return (
    method === "GET" &&
    path.length === 5 &&
    path[0] === "summoners" &&
    path[4] === "live-game"
  );
}
