const LOL_PUBLIC_SUMMONERS_PREFIX = "/api/trn/public/lol/summoners";

export function buildLolPublicSummonerByRiotIdPath(
  region: string,
  gameName: string,
  tagLine: string
) {
  return `${LOL_PUBLIC_SUMMONERS_PREFIX}/${encodeURIComponent(region)}/${encodeURIComponent(
    gameName
  )}/${encodeURIComponent(tagLine)}`;
}

export function buildLolPublicSummonerByIdPath(summonerId: string) {
  return `${LOL_PUBLIC_SUMMONERS_PREFIX}/${encodeURIComponent(summonerId)}`;
}

export function buildLolPublicSummonerSearchPath(
  region: string,
  query: string,
  limit: number
) {
  const search = new URLSearchParams({
    region,
    q: query,
    limit: String(limit)
  });

  return `${LOL_PUBLIC_SUMMONERS_PREFIX}/search?${search.toString()}`;
}
