import Image from "next/image";
import Link from "next/link";

import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { buttonClassName } from "@/components/ui/buttonStyles";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { Table, TableScroll, Th } from "@/components/ui/Table";
import { Toolbar } from "@/components/ui/Toolbar";
import type { AnalyticsRegionOption } from "@/lib/analyticsRegionShared";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import {
  buildResourceHref,
  filterAndSortBuildResources,
  type BuildResourceIndexResponse,
  type BuildResourceKind,
  type BuildResourceSort
} from "@/lib/buildResources";
import { cn } from "@/lib/cn";
import { formatGames, formatPercent } from "@/lib/format";
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

const KIND_TABS: Array<{ kind: BuildResourceKind; label: string }> = [
  { kind: "items", label: "Items" },
  { kind: "runes", label: "Runes" }
];

// Items and runes are one library under one nav entry, so the page header is a compact toolbar
// with an Items | Runes switch rather than a marketing hero: the table is the answer.
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
  const noun = isItems ? "item" : "rune";
  const entries = filterAndSortBuildResources(response?.entries ?? [], query, sort);
  // A reference link is only a link if its destination exists: /lol/builds 404s while buildLab is
  // off, so the reference flag can never render one on its own.
  const flags = await analyticsFeatureFlags();
  const showBuildLabLinks = flags.buildReferenceLinks && flags.buildLab;

  return (
    <div className="grid grid-cols-1 gap-4">
      <Toolbar
        title={isItems ? "Items" : "Runes"}
        meta={
          <>
            {response?.patch ? <span>Patch {response.patch}</span> : null}
            <span>{activeRegionLabel}</span>
            {response ? (
              <span className="type-tabular tabular-nums">
                {formatGames(response.totalParticipantGames)} player-games
              </span>
            ) : null}
          </>
        }
        actions={
          <>
            <nav aria-label="Library" className="flex items-center gap-1">
              {KIND_TABS.map((tab) => (
                <Link
                  key={tab.kind}
                  href={buildResourceHref(tab.kind, null, activeRegion)}
                  className="control-tab px-3"
                  data-active={tab.kind === kind}
                  aria-current={tab.kind === kind ? "page" : undefined}
                >
                  {tab.label}
                </Link>
              ))}
            </nav>
            {showBuildLabLinks ? (
              <Link href="/lol/builds" className={buttonClassName({ variant: "outline", size: "sm" })}>
                Compare in Build Lab
              </Link>
            ) : null}
          </>
        }
        filters={
          <>
            <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} variant="select" />
            <form
              action={`/lol/${kind}`}
              method="get"
              className="flex min-w-0 flex-1 flex-wrap items-center gap-2"
            >
              {activeRegion !== "ALL" ? <input type="hidden" name="region" value={activeRegion} /> : null}
              <label className="min-w-[12rem] flex-1">
                <span className="sr-only">Find {isItems ? "an item" : "a rune"}</span>
                <input
                  name="q"
                  type="search"
                  defaultValue={query}
                  placeholder={isItems ? "Find an item" : "Find a rune"}
                  className="control-input h-9"
                />
              </label>
              <label>
                <span className="sr-only">Sort</span>
                <select name="sort" defaultValue={sort} className="control-select h-9 w-auto pr-8">
                  <option value="popular">Most picked</option>
                  <option value="winrate">Highest win rate</option>
                  <option value="name">Name</option>
                </select>
              </label>
              <button type="submit" className={buttonClassName({ variant: "outline", size: "sm" })}>
                Apply
              </button>
            </form>
          </>
        }
      />

      {!response ? (
        <Card className="p-6">
          <h2 className="type-section">{isItems ? "Item" : "Rune"} analytics are unavailable</h2>
          <p className="type-ui mt-2 text-muted">
            The analytics service could not be reached. Try again after the next data refresh.
          </p>
        </Card>
      ) : entries.length === 0 ? (
        <Card className="p-6">
          <h2 className="type-section">No {noun} matches &ldquo;{query}&rdquo;</h2>
          <p className="type-ui mt-2 text-muted">Try a shorter name or another region.</p>
        </Card>
      ) : (
        <section aria-label={`${isItems ? "Item" : "Rune"} usage`} className="page-panel overflow-hidden">
          <TableScroll>
            <Table className="text-sm">
              <thead className="border-b border-border">
                <tr>
                  <Th className="w-12 pl-4 sm:pl-5">#</Th>
                  <Th className="w-full">{isItems ? "Item" : "Rune"}</Th>
                  <Th align="right" className="hidden sm:table-cell">
                    Pick rate
                  </Th>
                  <Th align="right">Win rate</Th>
                  <Th align="right" className="hidden md:table-cell">
                    Games
                  </Th>
                  <Th className="hidden pr-4 lg:table-cell sm:pr-5">Most used by</Th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry, index) => {
                  const itemMeta = itemMap?.items[String(entry.resourceId)];
                  const runeMeta = runeData?.runeById[String(entry.resourceId)];
                  const icon = isItems
                    ? itemMap
                      ? itemIconUrl(itemMap.version, entry.resourceId)
                      : null
                    : runeMeta
                      ? runeIconUrl(runeMeta.icon)
                      : null;
                  const description = itemMeta?.plaintext ?? entry.description;
                  return (
                    <tr
                      key={entry.resourceId}
                      className="tierlist-row tierlist-cv border-b border-border/50 last:border-b-0"
                    >
                      <td className="type-tabular py-2 pl-4 pr-3 tabular-nums text-muted sm:pl-5">
                        {index + 1}
                      </td>
                      <td className="max-w-0 py-2 pr-3">
                        <Link
                          href={buildResourceHref(kind, entry.resourceId, activeRegion)}
                          className="group flex min-w-0 items-center gap-3 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                        >
                          {icon ? (
                            <Image
                              src={icon}
                              alt=""
                              width={36}
                              height={36}
                              sizes="36px"
                              className={cn(
                                "h-9 w-9 shrink-0 border border-border/60 bg-bg",
                                isItems ? "rounded-md" : "rounded-full p-0.5"
                              )}
                            />
                          ) : (
                            <span aria-hidden className="h-9 w-9 shrink-0 rounded-md border border-border/60 bg-surface-2" />
                          )}
                          <span className="grid min-w-0">
                            <span className="truncate font-semibold text-fg transition-colors group-hover:text-primary">
                              {entry.name}
                            </span>
                            {description ? (
                              <span className="type-caption hidden truncate text-muted sm:block">{description}</span>
                            ) : null}
                          </span>
                        </Link>
                      </td>
                      <td className="type-tabular hidden px-3 py-2 text-right tabular-nums text-fg/80 sm:table-cell">
                        {formatPercent(entry.pickRate, { input: "ratio" })}
                      </td>
                      <td className="px-3 py-2 text-right">
                        <DataBar value={entry.winRate} games={entry.games} className="justify-end" />
                      </td>
                      <td className="type-tabular hidden px-3 py-2 text-right tabular-nums text-fg/70 md:table-cell">
                        {formatGames(entry.games)}
                      </td>
                      <td className="hidden py-2 pl-3 pr-4 lg:table-cell sm:pr-5">
                        <ul className="flex items-center gap-1">
                          {entry.topChampions.slice(0, 3).map((champion) => {
                            const meta = champions?.champions[String(champion.championId)];
                            const label = `${championDisplayName(meta)} · ${roleDisplayLabel(champion.role)}`;
                            return (
                              <li key={`${champion.championId}-${champion.role}`} title={label}>
                                {meta && champions ? (
                                  <Image
                                    src={championIconUrl(champions.version, meta.id)}
                                    alt={label}
                                    width={26}
                                    height={26}
                                    sizes="26px"
                                    className="rounded-md border border-border/50"
                                  />
                                ) : (
                                  <span className="sr-only">{label}</span>
                                )}
                              </li>
                            );
                          })}
                        </ul>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </Table>
          </TableScroll>
        </section>
      )}

      <p className="type-caption max-w-3xl text-muted">
        Win rates are observed in completed Ranked Solo/Duo games and still reflect champion strength,
        build timing, and game length. Read them beside the sample.
      </p>
    </div>
  );
}
