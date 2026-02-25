export type ChampionMap = {
  version: string;
  champions: Record<string, { id: string; name: string; title?: string }>;
};

type DDragonVersions = string[];

type DDragonChampion = {
  id: string;
  name: string;
  title?: string;
  key: string;
};

type DDragonChampionList = {
  data: Record<string, DDragonChampion>;
};

type DDragonItem = {
  name: string;
  plaintext?: string;
};

type DDragonItemList = {
  data: Record<string, DDragonItem>;
};

type DDragonSpell = {
  id: string;
  name: string;
  key: string; // numeric id as string
};

type DDragonSpellList = {
  data: Record<string, DDragonSpell>;
};

type DDragonRune = { id: number; name: string; icon: string };
type DDragonRuneSlot = { runes: DDragonRune[] };
type DDragonRuneStyle = {
  id: number;
  key: string;
  name: string;
  icon: string;
  slots: DDragonRuneSlot[];
};
type DDragonRunesReforged = DDragonRuneStyle[];

export type ItemMap = {
  version: string;
  items: Record<string, { name: string; plaintext?: string }>;
};

export type SummonerSpellMap = {
  version: string;
  spells: Record<string, { id: string; name: string }>;
};

export type RuneStaticData = {
  version: string;
  runeById: Record<string, { name: string; icon: string }>;
  styleById: Record<string, { name: string; icon: string }>;
};

async function fetchLatestVersion() {
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
  return version;
}

export async function fetchChampionMap(): Promise<ChampionMap> {
  const version = await fetchLatestVersion();

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
    champions[champ.key] = { id: champ.id, name: champ.name, title: champ.title };
  }

  return { version, champions };
}

export function championIconUrl(version: string, champId: string) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/champion/${champId}.png`;
}

export function profileIconUrl(version: string, profileIconId: number) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/profileicon/${profileIconId}.png`;
}

export async function fetchItemMap(): Promise<ItemMap> {
  const version = await fetchLatestVersion();

  const res = await fetch(
    `https://ddragon.leagueoflegends.com/cdn/${version}/data/en_US/item.json`,
    { next: { revalidate: 60 * 60 * 24 } }
  );
  if (!res.ok) throw new Error("Failed to fetch item list.");

  const list = (await res.json()) as DDragonItemList;
  const items: ItemMap["items"] = {};
  for (const [id, item] of Object.entries(list.data)) {
    items[id] = { name: item.name, plaintext: item.plaintext };
  }

  return { version, items };
}

export function itemIconUrl(version: string, itemId: number) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/item/${itemId}.png`;
}

export async function fetchSummonerSpellMap(): Promise<SummonerSpellMap> {
  const version = await fetchLatestVersion();

  const res = await fetch(
    `https://ddragon.leagueoflegends.com/cdn/${version}/data/en_US/summoner.json`,
    { next: { revalidate: 60 * 60 * 24 } }
  );
  if (!res.ok) throw new Error("Failed to fetch summoner spell list.");

  const list = (await res.json()) as DDragonSpellList;
  const spells: SummonerSpellMap["spells"] = {};
  for (const spell of Object.values(list.data)) {
    spells[spell.key] = { id: spell.id, name: spell.name };
  }

  return { version, spells };
}

export function summonerSpellIconUrl(version: string, spellCdnId: string) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/spell/${spellCdnId}.png`;
}

export async function fetchRunesReforged(): Promise<RuneStaticData> {
  const version = await fetchLatestVersion();

  const res = await fetch(
    `https://ddragon.leagueoflegends.com/cdn/${version}/data/en_US/runesReforged.json`,
    { next: { revalidate: 60 * 60 * 24 } }
  );
  if (!res.ok) throw new Error("Failed to fetch runes reforged list.");

  const styles = (await res.json()) as DDragonRunesReforged;
  const runeById: RuneStaticData["runeById"] = {};
  const styleById: RuneStaticData["styleById"] = {};

  for (const style of styles) {
    styleById[String(style.id)] = { name: style.name, icon: style.icon };
    for (const slot of style.slots ?? []) {
      for (const rune of slot.runes ?? []) {
        runeById[String(rune.id)] = { name: rune.name, icon: rune.icon };
      }
    }
  }

  return { version, runeById, styleById };
}

export function runeIconUrl(iconPath: string) {
  return `https://ddragon.leagueoflegends.com/cdn/img/${iconPath}`;
}

