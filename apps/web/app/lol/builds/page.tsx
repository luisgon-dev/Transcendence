import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { BuildLabChampionPicker } from "@/components/BuildLabChampionPicker";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { fetchChampionMap } from "@/lib/staticData";

export const metadata: Metadata = {
  title: "Build Lab",
  description: "Explore context-adjusted item paths, runes, and spells by champion and role."
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
        <p className="type-kicker text-primary">Decision analytics</p>
        <h1 className="type-page-title mt-2">Build Lab</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-fg/72">
          Choose a champion to compare realistic item, rune, and spell alternatives in comparable
          Emerald+ Ranked Solo/Duo games. Every published estimate must clear the evidence gates.
        </p>
      </header>
      <BuildLabChampionPicker champions={list} version={version} />
    </div>
  );
}
