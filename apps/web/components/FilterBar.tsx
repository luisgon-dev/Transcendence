"use client";

import { cn } from "@/lib/cn";
import { type AnalyticsRegionOption } from "@/lib/analyticsRegionShared";
import { type LolAnalyticsPatchOption } from "@/lib/lolPatchFilters";
import { RANK_TIER_FILTERS } from "@/lib/ranks";
import { LANE_ROLES } from "@/lib/roles";

import { AnalyticsPatchFilter } from "./AnalyticsPatchFilter";
import { AnalyticsRegionFilter } from "./AnalyticsRegionFilter";
import { RankFilterDropdown } from "./RankFilterDropdown";
import { RoleFilterTabs } from "./RoleFilterTabs";

const DEFAULT_ROLES = LANE_ROLES;
const DEFAULT_RANKS = RANK_TIER_FILTERS;

export function FilterBar({
  roles = DEFAULT_ROLES,
  activeRole = "ALL",
  ranks = DEFAULT_RANKS,
  activeRank = "all",
  regionOptions,
  activeRegion,
  patchOptions,
  activePatch,
  extraParams,
  explicitAllRank = false,
  baseHref,
  className
}: {
  roles?: readonly string[];
  activeRole?: string;
  ranks?: readonly string[];
  activeRank?: string;
  regionOptions?: readonly AnalyticsRegionOption[];
  activeRegion?: string;
  patchOptions?: readonly LolAnalyticsPatchOption[];
  activePatch?: string | null;
  extraParams?: Record<string, string | null | undefined>;
  /** Keep "All Ranks" selectable on pages whose absent-param default is Emerald+. */
  explicitAllRank?: boolean;
  baseHref: string;
  className?: string;
}) {
  const sharedExtraParams: Record<string, string> = {};
  if (extraParams) {
    for (const [key, value] of Object.entries(extraParams)) {
      if (value && value.toLowerCase() !== "all") sharedExtraParams[key] = value;
    }
  }

  const roleExtraParams: Record<string, string> = { ...sharedExtraParams };
  if (activeRank && (activeRank.toLowerCase() !== "all" || explicitAllRank)) {
    roleExtraParams.rankTier = activeRank;
  }

  const rankExtraParams: Record<string, string> = { ...sharedExtraParams };
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
        keepRankAll={explicitAllRank}
      />
      <RankFilterDropdown
        ranks={ranks}
        activeRank={activeRank}
        baseHref={baseHref}
        extraParams={rankExtraParams}
        emitAllRank={explicitAllRank}
      />
      {regionOptions && activeRegion ? (
        <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />
      ) : null}
      {patchOptions ? (
        <AnalyticsPatchFilter patches={patchOptions} activePatch={activePatch} />
      ) : null}
    </div>
  );
}
