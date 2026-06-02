"use client";

import { cn } from "@/lib/cn";
import { type AnalyticsRegionOption } from "@/lib/analyticsRegionShared";
import { type LolAnalyticsPatchOption } from "@/lib/lolPatchFilters";
import { RANK_TIER_FILTERS } from "@/lib/ranks";

import { LaneTabs } from "./LaneTabs";
import { AnalyticsPatchFilter } from "./AnalyticsPatchFilter";
import { AnalyticsRegionFilter } from "./AnalyticsRegionFilter";
import { RankFilterDropdown } from "./RankFilterDropdown";

const DEFAULT_ROLES = ["ALL", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;
const DEFAULT_RANKS = RANK_TIER_FILTERS;

/**
 * Tier-list-specific filter layout. Rank / Region / Patch sit in a quiet
 * controls row; the Lane selector is a prominent icon-tab bar pinned to the
 * bottom so it reads directly onto the champion table beneath it (u.gg-style).
 * Reuses the shared analytics filter children + FilterBar's query-param
 * cross-wiring; the shared FilterBar (champion/matchup detail pages) is left
 * untouched.
 */
export function TierListFilterBar({
  roles = DEFAULT_ROLES,
  activeRole = "ALL",
  ranks = DEFAULT_RANKS,
  activeRank = "all",
  regionOptions,
  activeRegion,
  patchOptions,
  activePatch,
  extraParams,
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
  if (activeRank && activeRank.toLowerCase() !== "all") {
    roleExtraParams.rankTier = activeRank;
  }

  const rankExtraParams: Record<string, string> = { ...sharedExtraParams };
  if (activeRole && activeRole.toUpperCase() !== "ALL") {
    rankExtraParams.role = activeRole;
  }

  return (
    <div className={cn("grid gap-4", className)}>
      {/* Rank · Region · Patch — quiet refinement controls. */}
      <div className="flex flex-wrap items-end gap-x-7 gap-y-4">
        <label className="grid gap-1.5">
          <span className="type-kicker text-fg/55">Rank</span>
          <RankFilterDropdown
            ranks={ranks}
            activeRank={activeRank}
            baseHref={baseHref}
            extraParams={rankExtraParams}
            className="w-full sm:w-44"
          />
        </label>

        {regionOptions && activeRegion ? (
          <div className="grid gap-1.5" role="group" aria-label="Region">
            <span className="type-kicker text-fg/55">Region</span>
            <AnalyticsRegionFilter
              options={regionOptions}
              activeRegion={activeRegion}
              className="-ml-1 flex flex-wrap gap-x-2 gap-y-1"
            />
          </div>
        ) : null}

        {patchOptions ? (
          <label className="grid gap-1.5">
            <span className="type-kicker text-fg/55">Patch</span>
            <AnalyticsPatchFilter
              patches={patchOptions}
              activePatch={activePatch}
              className="control-select min-w-[148px]"
            />
          </label>
        ) : null}
      </div>

      {/* Lane — the prominent primary choice, pinned to the table edge. */}
      <div className="-mx-1 flex items-end gap-x-3 border-b border-border/40 px-1">
        <span className="type-kicker hidden shrink-0 pb-3 text-fg/45 sm:block">Lane</span>
        <LaneTabs
          roles={roles}
          activeRole={activeRole}
          baseHref={baseHref}
          extraParams={roleExtraParams}
          className="-mb-px"
        />
      </div>
    </div>
  );
}
