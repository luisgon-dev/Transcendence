export function isAllowedAppProxyPath(method: string, path: string[]): boolean {
  if (
    method === "GET" &&
    path.length === 3 &&
    path[0] === "lol" &&
    path[1] === "operations" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(path[2])
  ) {
    return true;
  }

  if (
    method === "POST" &&
    path.length === 3 &&
    path[0] === "lol" &&
    path[1] === "summoners" &&
    path[2] === "multi-search"
  ) {
    return true;
  }

  if (
    method === "POST" &&
    path.length === 7 &&
    path[0] === "lol" &&
    path[1] === "summoners" &&
    path[5] === "live-game" &&
    path[6] === "probe"
  ) {
    return true;
  }

  return (
    method === "GET" &&
    path.length === 6 &&
    path[0] === "lol" &&
    path[1] === "summoners" &&
    path[5] === "live-game"
  );
}
