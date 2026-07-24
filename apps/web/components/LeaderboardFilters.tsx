"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState, useTransition } from "react";

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
  const [localFilters, setLocalFilters] = useState(filters);

  useEffect(() => {
    setLocalFilters(filters);
  }, [filters]);

  const championOptions = [
    { value: "ALL", label: "All champions" },
    ...Object.entries(champions)
      .sort(([, a], [, b]) => a.name.localeCompare(b.name))
      .map(([id, champion]) => ({ value: id, label: champion.name }))
  ];

  function update(next: Filters) {
    setLocalFilters(next);
    startTransition(() => {
      router.replace(`${pathname}?${leaderboardSearchParams(next).toString()}`, { scroll: false });
    });
  }

  return (
    <div
      aria-busy={pending}
      className="flex w-full flex-wrap items-center gap-2 opacity-100 transition-opacity aria-busy:opacity-65"
    >
      <Select
        value={localFilters.region}
        onValueChange={(region) => update({ ...localFilters, region })}
        options={[...LOL_REGION_OPTIONS]}
        ariaLabel="Leaderboard region"
        className="min-w-24"
      />
      <Select
        value={localFilters.queue}
        onValueChange={(queue) => update({ ...localFilters, queue: queue as Filters["queue"] })}
        options={QUEUE_OPTIONS}
        ariaLabel="Ranked queue"
        className="min-w-40"
      />
      <Select
        value={localFilters.championId ? String(localFilters.championId) : "ALL"}
        onValueChange={(championId) =>
          update({
            ...localFilters,
            championId: championId === "ALL" ? null : Number(championId),
            role: championId === "ALL" ? null : localFilters.role
          })
        }
        options={championOptions}
        ariaLabel="Champion leaderboard filter"
        className="min-w-44"
      />
      {localFilters.championId ? (
        <Select
          value={localFilters.role ?? "ALL"}
          onValueChange={(role) => update({ ...localFilters, role: role === "ALL" ? null : role })}
          options={ROLE_OPTIONS}
          ariaLabel="Champion role"
          className="min-w-32"
        />
      ) : null}
      <span className="sr-only" aria-live="polite">
        {pending ? "Updating leaderboard" : ""}
      </span>
    </div>
  );
}
