"use client";

import { useRouter } from "next/navigation";

import { Select } from "@/components/ui/Select";
import { rankTierDisplayLabel } from "@/lib/ranks";

export function RankFilterDropdown({
  ranks,
  activeRank,
  baseHref,
  extraParams,
  className
}: {
  ranks: readonly string[];
  activeRank: string;
  baseHref: string;
  extraParams?: Record<string, string>;
  className?: string;
}) {
  const router = useRouter();

  function handleChange(rank: string) {
    const params = new URLSearchParams();
    if (extraParams) {
      for (const [k, v] of Object.entries(extraParams)) {
        if (v && v.toLowerCase() !== "all") params.set(k, v);
      }
    }
    if (rank && rank.toLowerCase() !== "all") params.set("rankTier", rank);
    const qs = params.toString();
    router.push(qs ? `${baseHref}?${qs}` : baseHref);
  }

  return (
    <Select
      value={activeRank || "all"}
      onValueChange={handleChange}
      ariaLabel="Rank"
      options={ranks.map((rank) => ({ value: rank, label: rankTierDisplayLabel(rank) }))}
      className={className}
    />
  );
}
