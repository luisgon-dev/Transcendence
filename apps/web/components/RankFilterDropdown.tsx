"use client";

import { useRouter } from "next/navigation";

import { cn } from "@/lib/cn";
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

  function handleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const rank = e.target.value;
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
    <select
      value={activeRank || "all"}
      onChange={handleChange}
      className={cn(
        "control-select min-w-[156px]",
        className
      )}
    >
      {ranks.map((rank) => (
        <option key={rank} value={rank}>
          {rankTierDisplayLabel(rank)}
        </option>
      ))}
    </select>
  );
}
