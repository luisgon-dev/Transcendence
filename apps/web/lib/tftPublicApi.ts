const TFT_PUBLIC_SUMMONERS_PREFIX = "/api/trn/public/tft/summoners";

export function buildTftPublicSummonerByRiotIdPath(
  region: string,
  gameName: string,
  tagLine: string
) {
  return `${TFT_PUBLIC_SUMMONERS_PREFIX}/${encodeURIComponent(region)}/${encodeURIComponent(
    gameName
  )}/${encodeURIComponent(tagLine)}`;
}

export function buildTftPublicSummonerByIdPath(summonerId: string) {
  return `${TFT_PUBLIC_SUMMONERS_PREFIX}/${encodeURIComponent(summonerId)}`;
}

export function buildTftPublicSummonerSearchPath(
  region: string,
  query: string,
  limit: number
) {
  const search = new URLSearchParams({
    region,
    q: query,
    limit: String(limit)
  });

  return `${TFT_PUBLIC_SUMMONERS_PREFIX}/search?${search.toString()}`;
}
