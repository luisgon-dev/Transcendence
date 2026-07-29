import Image from "next/image";
import Link from "next/link";

import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import type { AnalyticsRegionOption } from "@/lib/analyticsRegionShared";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import {
  buildResourceHref,
  filterAndSortBuildResources,
  type BuildResourceIndexResponse,
  type BuildResourceKind,
  type BuildResourceSort
} from "@/lib/buildResources";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  itemIconUrl,
  runeIconUrl,
  type ChampionMap,
  type ItemMap,
  type RuneStaticData
} from "@/lib/staticData";

export async function BuildResourceIndex({
  kind,
  response,
  champions,
  itemMap,
  runeData,
  regionOptions,
  activeRegion,
  activeRegionLabel,
  query,
  sort
}: {
  kind: BuildResourceKind;
  response: BuildResourceIndexResponse | null;
  champions: ChampionMap | null;
  itemMap: ItemMap | null;
  runeData: RuneStaticData | null;
  regionOptions: AnalyticsRegionOption[];
  activeRegion: string;
  activeRegionLabel: string;
  query?: string;
  sort: BuildResourceSort;
}) {
  const isItems = kind === "items";
  const noun = isItems ? "Item" : "Rune";
  const entries = filterAndSortBuildResources(response?.entries ?? [], query, sort);
  // A reference link is only a link if its destination exists: /lol/builds 404s while buildLab is
  // off, so the reference flag can never render one on its own.
  const flags = await analyticsFeatureFlags();
  const showBuildLabLinks = flags.buildReferenceLinks && flags.buildLab;

  return (
    <div className="grid gap-7">
      <section className="page-hero overflow-hidden p-6 sm:p-8">
        <div className="flex flex-wrap items-center gap-2">
          <Badge>{activeRegionLabel}</Badge>
          {response?.patch ? <Badge className="border-primary/35 bg-primary/10 text-primary">Patch {response.patch}</Badge> : null}
          <Badge>{response ? `${formatGames(response.totalParticipantGames)} player-games` : "Analytics unavailable"}</Badge>
        </div>
        <p className="type-kicker mt-6 text-primary/85">Resource library</p>
        <h1 className="type-display mt-3 max-w-4xl">{noun} mechanics and observed ranked usage.</h1>
        <p className="type-lead mt-4 max-w-3xl">
          See what gets picked, how it performs, and which champion-role pairs use it most. Win rate is observational, so read it beside sample size and pick rate.
        </p>
        <div className="mt-7 flex flex-wrap gap-3">
          {showBuildLabLinks ? (
            <Link
              href="/lol/builds"
              className="type-ui inline-flex min-h-11 items-center rounded-control bg-primary px-4 font-semibold text-primary-fg transition hover:bg-primary/92"
            >
              Compare choices in Build Lab
            </Link>
          ) : null}
          <Link
            href={buildResourceHref(isItems ? "runes" : "items", null, activeRegion)}
            className="type-ui inline-flex min-h-11 items-center rounded-control border border-border/70 bg-surface/75 px-4 font-semibold text-fg transition hover:border-primary/35 hover:text-primary"
          >
            Browse {isItems ? "runes" : "items"}
          </Link>
        </div>
      </section>

      <Card className="p-5 sm:p-6">
        <div className="grid gap-5">
          <div className="grid gap-2 sm:grid-cols-[auto_1fr] sm:items-center sm:gap-5">
            <p className="type-kicker text-fg/60">Region</p>
            <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} className="flex flex-wrap gap-x-4 gap-y-1.5" />
          </div>
          <form action={`/lol/${kind}`} method="get" className="grid gap-3 border-t border-border/30 pt-5 md:grid-cols-[minmax(0,1fr)_11rem_auto]">
            {activeRegion !== "ALL" ? <input type="hidden" name="region" value={activeRegion} /> : null}
            <label className="grid gap-1.5">
              <span className="field-label">Find {isItems ? "an item" : "a rune"}</span>
              <input
                name="q"
                defaultValue={query}
                placeholder={isItems ? "Trinity Force" : "Press the Attack"}
                className="h-11 rounded-control border border-border/70 bg-bg/70 px-3 text-sm text-fg outline-none transition placeholder:text-muted focus:border-primary/45 focus:ring-2 focus:ring-primary/15"
              />
            </label>
            <label className="grid gap-1.5">
              <span className="field-label">Sort</span>
              <select name="sort" defaultValue={sort} className="h-11 rounded-control border border-border/70 bg-bg/70 px-3 text-sm text-fg outline-none focus:border-primary/45 focus:ring-2 focus:ring-primary/15">
                <option value="popular">Most picked</option>
                <option value="winrate">Highest win rate</option>
                <option value="name">Name</option>
              </select>
            </label>
            <button type="submit" className="type-ui min-h-11 self-end rounded-control bg-primary px-5 font-semibold text-primary-fg transition hover:bg-primary/92">
              Apply
            </button>
          </form>
        </div>
      </Card>

      {!response ? (
        <Card className="p-6">
          <h2 className="type-section">{noun} analytics are unavailable</h2>
          <p className="type-ui mt-2 text-muted">The analytics service could not be reached. Try again after the next data refresh.</p>
        </Card>
      ) : entries.length === 0 ? (
        <Card className="p-6">
          <h2 className="type-section">No matching {kind}</h2>
          <p className="type-ui mt-2 text-muted">Try a different name or region.</p>
        </Card>
      ) : (
        <section className="grid gap-3 lg:grid-cols-2">
          {entries.map((entry) => {
            const itemMeta = itemMap?.items[String(entry.resourceId)];
            const runeMeta = runeData?.runeById[String(entry.resourceId)];
            const icon = isItems
              ? itemMap ? itemIconUrl(itemMap.version, entry.resourceId) : null
              : runeMeta ? runeIconUrl(runeMeta.icon) : null;
            const description = itemMeta?.plaintext ?? entry.description;
            return (
              <Link
                key={entry.resourceId}
                href={buildResourceHref(kind, entry.resourceId, activeRegion)}
                className="group rounded-panel border border-border/55 bg-surface/72 p-5 shadow-soft transition duration-200 hover:-translate-y-0.5 hover:border-primary/28 hover:shadow-card focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30"
              >
                <div className="flex gap-4">
                  {icon ? (
                    <Image src={icon} alt="" width={56} height={56} sizes="56px" className="h-14 w-14 shrink-0 rounded-xl border border-border/55 bg-bg object-cover" />
                  ) : (
                    <div
                      className="grid h-14 w-14 shrink-0 place-items-center rounded-xl border border-border/55 bg-bg text-lg font-semibold text-muted"
                      aria-label={`${noun} icon unavailable`}
                    >
                      ?
                    </div>
                  )}
                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <h2 className="type-section truncate transition group-hover:text-primary">{entry.name}</h2>
                      </div>
                      <span className="type-meta shrink-0 text-primary">Explore →</span>
                    </div>
                    {description ? <p className="mt-3 line-clamp-2 text-sm leading-6 text-fg/68">{description}</p> : null}
                  </div>
                </div>

                <div className="mt-5 grid grid-cols-3 gap-3 border-t border-border/30 pt-4">
                  <Stat label="Pick rate" value={formatPercent(entry.pickRate, { input: "ratio" })} />
                  <Stat label="Win rate" value={formatPercent(entry.winRate, { input: "ratio" })} valueClassName={winRateColorClass(entry.winRate)} />
                  <Stat label="Games" value={formatGames(entry.games)} />
                </div>

                {entry.topChampions.length > 0 ? (
                  <div className="mt-4 flex flex-wrap items-center gap-2">
                    <span className="type-kicker mr-1 text-fg/48">Often used by</span>
                    {entry.topChampions.map((champion) => {
                      const meta = champions?.champions[String(champion.championId)];
                      return (
                        <span key={`${champion.championId}-${champion.role}`} className="inline-flex items-center gap-1.5 rounded-full border border-border/45 bg-bg/55 py-1 pl-1 pr-2 text-xs text-fg/72">
                          {meta && champions ? <Image src={championIconUrl(champions.version, meta.id)} alt="" width={22} height={22} sizes="22px" className="rounded-full" /> : null}
                          <span>{championDisplayName(meta)} · {roleDisplayLabel(champion.role)}</span>
                        </span>
                      );
                    })}
                  </div>
                ) : null}
              </Link>
            );
          })}
        </section>
      )}
    </div>
  );
}

function Stat({ label, value, valueClassName = "" }: { label: string; value: string; valueClassName?: string }) {
  return (
    <div>
      <p className="type-kicker text-fg/45">{label}</p>
      <p className={`type-ui mt-1 font-semibold text-fg ${valueClassName}`}>{value}</p>
    </div>
  );
}
