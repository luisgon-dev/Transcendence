import Image from "next/image";
import Link from "next/link";
import type { ReactNode } from "react";

import {
  formatCompactCount,
  formatLift,
  formatPercent,
  liftToneClass,
  type BuildLabCoverage,
  type BuildLabOption
} from "@/lib/buildLab";
import { rankTierDisplayLabel } from "@/lib/ranks";
import { itemIconUrl, runeIconUrl, summonerSpellIconUrl } from "@/lib/staticData";

export type ChampionRecommendationSummary = {
  available: boolean;
  coverage: BuildLabCoverage;
  firstItem?: BuildLabOption | null;
  runePage?: BuildLabOption | null;
  spellPair?: BuildLabOption | null;
  unavailableReason?: string | null;
};

function Chip({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-control border border-border/60 bg-surface-2 px-2 py-0.5 text-xs text-fg/75">
      {children}
    </span>
  );
}

function RecommendationChoice({
  label,
  option,
  name,
  icons
}: {
  label: string;
  option?: BuildLabOption | null;
  name: string;
  icons: string[];
}) {
  return (
    <div className="min-w-0 border-t border-border/45 py-3 first:border-t-0 sm:border-l sm:border-t-0 sm:px-4 sm:first:border-l-0 sm:first:pl-0">
      <p className="type-kicker text-muted">{label}</p>
      {option ? (
        <>
          <div className="mt-2 flex min-w-0 items-center gap-2">
            <span className="flex shrink-0 -space-x-1">
              {icons.map((icon, index) => (
                <Image
                  key={`${icon}-${index}`}
                  src={icon}
                  alt=""
                  width={32}
                  height={32}
                  className="size-8 rounded-control border-2 border-surface bg-surface-2 object-cover"
                />
              ))}
            </span>
            <span className="truncate text-sm font-semibold text-fg">{name}</span>
          </div>
          <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
            <span className="font-semibold tabular-nums text-fg">
              {formatPercent(option.adjustedWinRate)}
            </span>
            <span className={`font-semibold tabular-nums ${liftToneClass(option.lift)}`}>
              {formatLift(option.lift)}
            </span>
            <span className="tabular-nums">{formatPercent(option.pickRate)} pick</span>
            <span className="tabular-nums">{formatCompactCount(option.games)} games</span>
          </div>
        </>
      ) : (
        <p className="mt-2 text-sm text-muted">Not enough games yet</p>
      )}
    </div>
  );
}

export function ChampionRecommendation({
  recommendation,
  championId,
  role,
  patch,
  region,
  pageRankTier,
  itemVersion,
  items,
  runeById,
  spellVersion,
  spells
}: {
  recommendation: ChampionRecommendationSummary;
  championId: number;
  role: string;
  patch?: string | null;
  region?: string | null;
  /** The rank filter the surrounding page is showing, so the two scopes cannot be conflated. */
  pageRankTier?: string | null;
  itemVersion: string;
  items: Record<string, { name: string }>;
  runeById: Record<string, { name: string; icon: string }>;
  spellVersion: string;
  spells: Record<string, { id: string; name: string }>;
}) {
  const query = new URLSearchParams({ role });
  if (patch) query.set("patch", patch);
  if (region && region !== "ALL") query.set("region", region);
  const { firstItem: item, runePage, spellPair: spell } = recommendation;
  const patches = recommendation.coverage.includedPatches;
  // Build Lab counts every tracked ranked game; a rank filter on the page above does not narrow it,
  // so the difference is stated instead of left to be misread.
  const rankFiltered = Boolean(pageRankTier) && (pageRankTier ?? "").toUpperCase() !== "ALL";

  return (
    <section className="rounded-card border border-border/65 bg-surface">
      <div className="flex flex-col gap-3 border-b border-border/45 px-4 py-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <p className="type-kicker text-primary">Recommended setup</p>
          <h2 className="type-section mt-1">Best supported choices</h2>
          <div className="mt-2 flex flex-wrap items-center gap-1.5">
            <Chip>All tracked ranks</Chip>
            {patches.length > 0 ? (
              <Chip>{patches.length > 1 ? `Patches ${patches.join(", ")}` : `Patch ${patches[0]}`}</Chip>
            ) : null}
            <Chip>Any lane opponent</Chip>
          </div>
          <p className="mt-2 text-xs text-muted">
            Win rates adjusted for the gold lead each choice was made with.
            {rankFiltered
              ? ` Counted across all tracked ranks — the ${rankTierDisplayLabel(pageRankTier)} filter above does not change it.`
              : ""}
          </p>
        </div>
        <Link
          href={`/lol/builds/${championId}?${query.toString()}`}
          className="shrink-0 text-sm font-semibold text-primary hover:underline"
        >
          Explore in Build Lab
        </Link>
      </div>
      {recommendation.available ? (
        <div className="grid px-4 sm:grid-cols-3">
          <RecommendationChoice
            label="First item"
            option={item}
            name={item ? items[String(item.actionIds[0])]?.name ?? `Item ${item.actionIds[0]}` : ""}
            icons={item?.actionIds.map((id) => itemIconUrl(itemVersion, id)) ?? []}
          />
          <RecommendationChoice
            label="Rune page"
            option={runePage}
            name={
              runePage
                ? runeById[String(runePage.actionIds[0])]?.name ?? `Rune ${runePage.actionIds[0]}`
                : ""
            }
            icons={
              runePage?.actionIds
                .slice(0, 1)
                .map((id) => runeIconUrl(runeById[String(id)]?.icon ?? "")) ?? []
            }
          />
          <RecommendationChoice
            label="Spell pair"
            option={spell}
            name={spell?.actionIds.map((id) => spells[String(id)]?.name ?? `Spell ${id}`).join(" + ") ?? ""}
            icons={
              spell?.actionIds.map((id) =>
                summonerSpellIconUrl(spellVersion, spells[String(id)]?.id ?? "")
              ) ?? []
            }
          />
        </div>
      ) : (
        <p className="px-4 py-4 text-sm text-muted">
          {recommendation.unavailableReason ?? "Not enough games have been counted yet."}
        </p>
      )}
    </section>
  );
}
