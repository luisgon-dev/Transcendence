import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { BuildLabChampionPicker } from "@/components/BuildLabChampionPicker";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { fetchChampionMap } from "@/lib/staticData";

export const metadata: Metadata = {
  title: "Build Lab",
  description: "Compare items, runes, and spells by champion and role."
};

export default async function BuildLabIndexPage() {
  if (!(await analyticsFeatureFlags()).buildLab) notFound();
  const { version, champions } = await fetchChampionMap();
  const list = Object.entries(champions)
    .map(([championId, champion]) => ({
      championId: Number(championId),
      slug: champion.id,
      name: champion.name,
      title: champion.title
    }))
    .sort((a, b) => a.name.localeCompare(b.name));

  return (
    <div className="grid gap-7">
      <header className="border-b border-border/60 pb-6">
        <h1 className="type-page-title mt-2">Build Lab</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-fg/72">
          Pick a champion and role to compare items, runes, spells, and skill orders in Ranked Solo/Duo.
          Win rates account for the gold lead when each choice was made.
        </p>
      </header>
      <BuildLabChampionPicker champions={list} version={version} />
    </div>
  );
}
