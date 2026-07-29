import Image from "next/image";
import Link from "next/link";
import type { ReactNode } from "react";

import {
  buildLabRegionLabel,
  formatCompactCount,
  formatWpa,
  humanizeToken,
  wpaToneClass,
  type AdjustedActionEstimate,
  type BuildLabContext,
  type BuildLabProvenance
} from "@/lib/buildLab";
import { rankTierDisplayLabel } from "@/lib/ranks";
import { itemIconUrl, runeIconUrl, summonerSpellIconUrl } from "@/lib/staticData";

export type ChampionRecommendationSummary = {
  available: boolean;
  provenance: BuildLabProvenance;
  /**
   * The champion-profile summary carries provenance only — `ChampionRecommendationSummary` in
   * BuildLabDtos.cs has no context member — so nothing may be read from here without a fallback
   * that works on the real payload. Kept optional so a backend that starts threading the resolved
   * context is honored without another pass here.
   */
  context?: BuildLabContext | null;
  firstItem?: AdjustedActionEstimate | null;
  rune?: AdjustedActionEstimate | null;
  spellPair?: AdjustedActionEstimate | null;
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
  estimate,
  names,
  icons
}: {
  label: string;
  estimate?: AdjustedActionEstimate | null;
  names: string[];
  icons: string[];
}) {
  return (
    <div className="min-w-0 border-t border-border/45 py-3 first:border-t-0 sm:border-l sm:border-t-0 sm:px-4 sm:first:border-l-0 sm:first:pl-0">
      <p className="type-kicker text-muted">{label}</p>
      {estimate ? (
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
            <span className="truncate text-sm font-semibold text-fg">{names.join(" + ")}</span>
          </div>
          <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
            <span className={`font-semibold tabular-nums ${wpaToneClass(estimate.adjustedWpa)}`}>
              {formatWpa(estimate.adjustedWpa)}
            </span>
            <span className="tabular-nums">
              95% {formatWpa(estimate.confidenceLow)} to {formatWpa(estimate.confidenceHigh)}
            </span>
            <span className="tabular-nums">
              {formatCompactCount(estimate.observedCount)} observed
            </span>
            {estimate.fallbackScope === "GLOBAL_FALLBACK" ? (
              <span>{buildLabRegionLabel(estimate.regionScope)} cell</span>
            ) : null}
          </div>
          <p className="mt-1.5 text-xs text-muted">
            Estimated against {estimate.baselineDefinition.replace(/\.$/, "").toLowerCase()} ·{" "}
            {humanizeToken(estimate.evidenceQuality)} evidence
          </p>
        </>
      ) : (
        <p className="mt-2 text-sm text-muted">Insufficient evidence</p>
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
  opponentName,
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
  opponentName?: string | null;
  itemVersion: string;
  items: Record<string, { name: string }>;
  runeById: Record<string, { name: string; icon: string }>;
  spellVersion: string;
  spells: Record<string, { id: string; name: string }>;
}) {
  const context = recommendation.context ?? null;
  // The champion profile never scopes the summary to a lane opponent (the controller passes none),
  // so this stays null until the endpoint gains one — the chip below states that rather than imply it.
  const opponentChampionId = context?.opponentChampionId ?? null;
  // The effective patch has to be disclosed, and provenance is the only place the summary carries
  // it: the promoted generation always lists its own patch first in includedPatches (see
  // BuildLabGenerationCoordinator), and BuildLabService resolves every estimate against exactly
  // that patch — a borrowed prior patch never becomes the effective one.
  const effectivePatch = recommendation.provenance.includedPatches[0] ?? null;
  const query = new URLSearchParams({ role });
  if (patch) query.set("patch", patch);
  if (region && region !== "ALL") query.set("region", region);
  if (opponentChampionId) query.set("opponentChampionId", String(opponentChampionId));
  const item = recommendation.firstItem;
  const rune = recommendation.rune;
  const spell = recommendation.spellPair;
  const itemNames = item?.actionIds.map((id) => items[String(id)]?.name ?? `Item ${id}`) ?? [];
  const runeNames = rune?.actionIds.map((id) => runeById[String(id)]?.name ?? `Rune ${id}`) ?? [];
  const spellNames = spell?.actionIds.map((id) => spells[String(id)]?.name ?? `Spell ${id}`) ?? [];
  const rankScope = recommendation.provenance.rankScope || "EMERALD_PLUS";
  const rankScopeLabel = rankTierDisplayLabel(rankScope);
  // The estimates are modeled at one fixed rank scope; the page's rank filter does not move them,
  // so the difference is stated instead of left to be misread.
  const rankMismatch =
    Boolean(pageRankTier) && (pageRankTier ?? "").toUpperCase() !== rankScope.toUpperCase();

  return (
    <section className="rounded-card border border-border/65 bg-surface">
      <div className="flex flex-col gap-3 border-b border-border/45 px-4 py-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <p className="type-kicker text-primary">Recommended setup</p>
          <h2 className="type-section mt-1">Best supported decisions</h2>
          <div className="mt-2 flex flex-wrap items-center gap-1.5">
            <Chip>{rankScopeLabel} scope</Chip>
            {effectivePatch ? <Chip>Patch {effectivePatch}</Chip> : null}
            {opponentChampionId ? (
              <Chip>vs {opponentName ?? `champion ${opponentChampionId}`}</Chip>
            ) : (
              <Chip>Any lane opponent</Chip>
            )}
          </div>
          <p className="mt-2 text-xs text-muted">
            Context-adjusted estimates comparing realistic alternatives, not raw win rates.
            {rankMismatch
              ? ` Always modeled at ${rankScopeLabel} — the ${rankTierDisplayLabel(pageRankTier)} filter above does not change it.`
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
            estimate={item}
            names={itemNames}
            icons={item?.actionIds.map((id) => itemIconUrl(itemVersion, id)) ?? []}
          />
          <RecommendationChoice
            label="Rune page"
            estimate={rune}
            names={runeNames}
            icons={
              rune?.actionIds.map((id) => runeIconUrl(runeById[String(id)]?.icon ?? "")) ?? []
            }
          />
          <RecommendationChoice
            label="Spell pair"
            estimate={spell}
            names={spellNames}
            icons={
              spell?.actionIds.map((id) =>
                summonerSpellIconUrl(spellVersion, spells[String(id)]?.id ?? "")
              ) ?? []
            }
          />
        </div>
      ) : (
        <p className="px-4 py-4 text-sm text-muted">
          {recommendation.unavailableReason ?? "This champion-role is still in shadow validation."}
        </p>
      )}
    </section>
  );
}
