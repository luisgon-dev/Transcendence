"use client";

import { usePathname, useRouter } from "next/navigation";
import { useTransition } from "react";

import { Select } from "@/components/ui/Select";
import type { ChampionMap } from "@/lib/staticData";
import { leaderboardSearchParams, type LeaderboardFilters as Filters } from "@/lib/leaderboards";
import { LOL_REGION_OPTIONS } from "@/lib/lolRegions";

const QUEUE_OPTIONS = [
  { value: "solo", label: "Ranked Solo/Duo" },
  { value: "flex", label: "Ranked Flex" }
];

const ROLE_OPTIONS = [
  { value: "ALL", label: "All roles" },
  { value: "TOP", label: "Top" },
  { value: "JUNGLE", label: "Jungle" },
  { value: "MIDDLE", label: "Middle" },
  { value: "BOTTOM", label: "Bottom" },
  { value: "UTILITY", label: "Support" }
];

export function LeaderboardFilters({
  filters,
  champions
}: {
  filters: Filters;
  champions: ChampionMap["champions"];
}) {
  const router = useRouter();
  const pathname = usePathname();
  const [pending, startTransition] = useTransition();
  const championOptions = [
    { value: "ALL", label: "All champions" },
    ...Object.entries(champions)
      .sort(([, a], [, b]) => a.name.localeCompare(b.name))
      .map(([id, champion]) => ({ value: id, label: champion.name }))
  ];

  function update(next: Filters) {
    startTransition(() => {
      router.push(`${pathname}?${leaderboardSearchParams(next).toString()}`, { scroll: false });
    });
  }

  return (
    <div aria-busy={pending} className="flex w-full flex-wrap gap-2 opacity-100 transition-opacity aria-busy:opacity-65">
      <Select
        value={filters.region}
        onValueChange={(region) => update({ ...filters, region })}
        options={[...LOL_REGION_OPTIONS]}
        ariaLabel="Leaderboard region"
        className="min-w-24"
      />
      <Select
        value={filters.queue}
        onValueChange={(queue) => update({ ...filters, queue: queue as Filters["queue"] })}
        options={QUEUE_OPTIONS}
        ariaLabel="Ranked queue"
        className="min-w-40"
      />
      <Select
        value={filters.championId ? String(filters.championId) : "ALL"}
        onValueChange={(championId) =>
          update({
            ...filters,
            championId: championId === "ALL" ? null : Number(championId),
            role: championId === "ALL" ? null : filters.role
          })
        }
        options={championOptions}
        ariaLabel="Champion leaderboard filter"
        className="min-w-44"
      />
      {filters.championId ? (
        <Select
          value={filters.role ?? "ALL"}
          onValueChange={(role) => update({ ...filters, role: role === "ALL" ? null : role })}
          options={ROLE_OPTIONS}
          ariaLabel="Champion role"
          className="min-w-32"
        />
      ) : null}
    </div>
  );
}
