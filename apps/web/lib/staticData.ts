export type ChampionMap = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

type DDragonVersions = string[];

type DDragonChampion = {
  id: string;
  name: string;
  key: string;
};

type DDragonChampionList = {
  data: Record<string, DDragonChampion>;
};

export async function fetchChampionMap(): Promise<ChampionMap> {
  const versionsRes = await fetch(
    "https://ddragon.leagueoflegends.com/api/versions.json",
    { next: { revalidate: 60 * 60 * 24 } }
  );
  if (!versionsRes.ok) {
    throw new Error("Failed to fetch Data Dragon versions.");
  }

  const versions = (await versionsRes.json()) as DDragonVersions;
  const version = versions[0];
  if (!version) throw new Error("No Data Dragon versions available.");

  const champsRes = await fetch(
    `https://ddragon.leagueoflegends.com/cdn/${version}/data/en_US/champion.json`,
    { next: { revalidate: 60 * 60 * 24 } }
  );
  if (!champsRes.ok) {
    throw new Error("Failed to fetch champion list.");
  }

  const champList = (await champsRes.json()) as DDragonChampionList;
  const champions: ChampionMap["champions"] = {};

  for (const champ of Object.values(champList.data)) {
    champions[champ.key] = { id: champ.id, name: champ.name };
  }

  return { version, champions };
}

export function championIconUrl(version: string, champId: string) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/champion/${champId}.png`;
}

