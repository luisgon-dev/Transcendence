"use client";

import { cn } from "@/lib/cn";
import { RANK_TIER_FILTERS } from "@/lib/ranks";

import { RankFilterDropdown } from "./RankFilterDropdown";
import { RoleFilterTabs } from "./RoleFilterTabs";

const DEFAULT_ROLES = ["ALL", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;
const DEFAULT_RANKS = RANK_TIER_FILTERS;

export function FilterBar({
  roles = DEFAULT_ROLES,
  activeRole = "ALL",
  ranks = DEFAULT_RANKS,
  activeRank = "all",
  baseHref,
  patch,
  className
}: {
  roles?: readonly string[];
  activeRole?: string;
  ranks?: readonly string[];
  activeRank?: string;
  baseHref: string;
  patch?: string | null;
  className?: string;
}) {
  const roleExtraParams: Record<string, string> = {};
  if (activeRank && activeRank.toLowerCase() !== "all") {
    roleExtraParams.rankTier = activeRank;
  }

  const rankExtraParams: Record<string, string> = {};
  if (activeRole && activeRole.toUpperCase() !== "ALL") {
    rankExtraParams.role = activeRole;
  }

  return (
    <div className={cn("flex flex-wrap items-center gap-3", className)}>
      <RoleFilterTabs
        roles={roles}
        activeRole={activeRole}
        baseHref={baseHref}
        extraParams={roleExtraParams}
      />
      <RankFilterDropdown
        ranks={ranks}
        activeRank={activeRank}
        baseHref={baseHref}
        extraParams={rankExtraParams}
      />
      {patch ? (
        <span className="rounded-full border border-primary/40 bg-primary/10 px-2.5 py-1 text-xs font-medium text-primary">
          Patch {patch}
        </span>
      ) : null}
    </div>
  );
}
