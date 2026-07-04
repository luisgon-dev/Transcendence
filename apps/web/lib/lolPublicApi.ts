const LOL_PUBLIC_SUMMONERS_PREFIX = "/api/trn/public/lol/summoners";
const LOL_USER_SUMMONERS_PREFIX = "/api/trn/user/lol/summoners";

export function buildLolPublicSummonerByRiotIdPath(
  region: string,
  gameName: string,
  tagLine: string
) {
  return `${LOL_PUBLIC_SUMMONERS_PREFIX}/${encodeURIComponent(region)}/${encodeURIComponent(
    gameName
  )}/${encodeURIComponent(tagLine)}`;
}

export function buildLolUserSummonerRefreshPath(
  region: string,
  gameName: string,
  tagLine: string
) {
  return `${LOL_USER_SUMMONERS_PREFIX}/${encodeURIComponent(region)}/${encodeURIComponent(
    gameName
  )}/${encodeURIComponent(tagLine)}/refresh`;
}

export function buildLolPublicSummonerByIdPath(summonerId: string) {
  return `${LOL_PUBLIC_SUMMONERS_PREFIX}/${encodeURIComponent(summonerId)}`;
}

export function buildLolPublicSummonerRankHistoryPath(summonerId: string, queueType?: string) {
  const base = `${buildLolPublicSummonerByIdPath(summonerId)}/stats/rank-history`;
  return queueType ? `${base}?queueType=${encodeURIComponent(queueType)}` : base;
}

export function buildLolPublicSummonerMatchTimelinePath(summonerId: string, matchId: string) {
  return `${buildLolPublicSummonerByIdPath(summonerId)}/matches/${encodeURIComponent(matchId)}/timeline`;
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
