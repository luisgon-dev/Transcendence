import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { BuildLab } from "@/components/BuildLab";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { fetchBackendJson } from "@/lib/backendCall";
import {
  buildLabRequestQuery,
  normalizeBuildLabState,
  type BuildLabResponse
} from "@/lib/buildLab";
import { getBackendBaseUrl } from "@/lib/env";
import {
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap
} from "@/lib/staticData";

type SearchParams = Record<string, string | string[] | undefined>;

export async function generateMetadata({
  params
}: {
  params: Promise<{ championId: string }>;
}): Promise<Metadata> {
  const { championId } = await params;
  const { champions } = await fetchChampionMap();
  const champion = champions[championId];
  return {
    title: `${champion?.name ?? "Champion"} Build Lab`,
    description: `Compare context-adjusted item paths, runes, and spells for ${champion?.name ?? "this champion"}.`
  };
}

export default async function ChampionBuildLabPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<SearchParams>;
}) {
  if (!(await analyticsFeatureFlags()).buildLab) notFound();
  const [{ championId: rawChampionId }, rawSearchParams] = await Promise.all([
    params,
    searchParams ?? Promise.resolve({})
  ]);
  const championId = Number(rawChampionId);
  if (!Number.isInteger(championId) || championId <= 0) {
    return <BackendErrorCard title="Build Lab" message="That champion route is invalid." />;
  }

  const { state, issues } = normalizeBuildLabState(rawSearchParams);
  const [championStatic, itemStatic, runeStatic, spellStatic] = await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchRunesReforged(),
    fetchSummonerSpellMap()
  ]);
  const champion = championStatic.champions[String(championId)];
  if (!champion) {
    return <BackendErrorCard title="Build Lab" message="That champion is not available." />;
  }

  const result = await fetchBackendJson<BuildLabResponse>(
    `${getBackendBaseUrl()}/api/lol/analytics/build-lab/${championId}?${buildLabRequestQuery(state).toString()}`,
    { cache: "no-store" }
  );
  const emptyResponse: BuildLabResponse = {
    available: false,
    context: {
      championId,
      role: state.role,
      opponentChampionId: state.opponentChampionId,
      requestedPatch: state.patch ?? "",
      effectivePatch: "",
      requestedRegion: state.region ?? "GLOBAL",
      effectiveRegion: "GLOBAL",
      section: state.section.toUpperCase(),
      mode: state.mode.toUpperCase()
    },
    provenance: {
      generationId: null,
      datasetVersion: "",
      modelVersion: "",
      staticDataVersion: itemStatic.version,
      sourceCutoffUtc: null,
      generatedAtUtc: null,
      matchCount: 0,
      rankScope: "Emerald+",
      includedPatches: [],
      includedRegions: []
    },
    selectedPath: [],
    pathEstimate: null,
    stages: [],
    unavailableReason: result.ok
      ? "This context has not passed the publication gates."
      : "Build Lab is temporarily unavailable."
  };
  const champions = Object.entries(championStatic.champions)
    .map(([id, entry]) => ({
      championId: Number(id),
      slug: entry.id,
      name: entry.name
    }))
    .sort((a, b) => a.name.localeCompare(b.name));

  return (
    <BuildLab
      championId={championId}
      championSlug={champion.id}
      championName={champion.name}
      champions={champions}
      version={championStatic.version}
      itemVersion={itemStatic.version}
      items={itemStatic.items}
      runes={runeStatic.runeById}
      spellVersion={spellStatic.version}
      spells={spellStatic.spells}
      initialState={state}
      initialResponse={result.body ?? emptyResponse}
      initialIssues={issues}
    />
  );
}
