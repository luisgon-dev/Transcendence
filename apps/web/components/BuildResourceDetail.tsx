import Image from "next/image";
import Link from "next/link";

import { DataBar } from "@/components/ui/DataBar";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { Table, TableScroll, Th } from "@/components/ui/Table";
import {
  buildResourceHref,
  roleShares,
  type BuildResourceChampionStat,
  type BuildResourceDetailResponse,
  type BuildResourceKind
} from "@/lib/buildResources";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { formatLift, liftToneClass } from "@/lib/buildLab";
import { cn } from "@/lib/cn";
import { formatDurationSeconds, formatGames, formatPercent } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { parseItemDescription } from "@/lib/itemDescription";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  itemIconUrl,
  runeIconUrl,
  type ChampionMap,
  type ItemDetail,
  type ItemMap,
  type RuneStaticData
} from "@/lib/staticData";

// Champion-role rows under this many games are folded behind a disclosure: their win rates swing
// tens of points on a handful of games, and leading with them buries the rows worth reading.
const THIN_SAMPLE_GAMES = 30;
// The table answers "who builds it"; past the first ten pairs it is a long tail, so the rest folds.
const VISIBLE_CHAMPION_ROWS = 10;

function formatGold(value: number) {
  return `${value.toLocaleString("en-US")} gold`;
}

const STAGE_ORDINALS = ["1st", "2nd", "3rd", "4th", "5th", "6th"];

function stageLabel(isBoots: boolean, stage: number) {
  return isBoots ? "First boots" : `${STAGE_ORDINALS[stage - 1] ?? `${stage}th`} item`;
}

function formatGameClock(minutes: number | null | undefined) {
  return minutes == null ? "-" : formatDurationSeconds(minutes * 60);
}

// "25% Critical Strike Chance" → value "25%", label "Critical Strike Chance".
function splitStat(line: string) {
  const match = /^(\S*\d\S*)\s+(.+)$/.exec(line);
  return match ? { value: match[1], label: match[2] } : { value: null, label: line };
}

function ItemRow({
  itemId,
  itemMap,
  gold
}: {
  itemId: number;
  itemMap: ItemMap | null;
  gold?: number | null;
}) {
  const name = itemMap?.items[String(itemId)]?.name ?? `Item ${itemId}`;
  return (
    <li className="flex max-w-none items-center gap-3 px-4 py-1.5 sm:px-5">
      {itemMap ? (
        <Image
          src={itemIconUrl(itemMap.version, itemId)}
          alt=""
          width={28}
          height={28}
          sizes="28px"
          className="shrink-0 rounded-md border border-border/60 bg-bg"
        />
      ) : null}
      <span className="min-w-0 flex-1 truncate text-sm text-fg">{name}</span>
      {gold ? <span className="type-caption type-tabular tabular-nums text-muted">{formatGold(gold)}</span> : null}
    </li>
  );
}

export async function BuildResourceDetail({
  kind,
  response,
  champions,
  itemMap,
  itemDetail,
  runeData,
  activeRegionLabel
}: {
  kind: BuildResourceKind;
  response: BuildResourceDetailResponse;
  champions: ChampionMap | null;
  itemMap: ItemMap | null;
  itemDetail: ItemDetail | null;
  runeData: RuneStaticData | null;
  activeRegionLabel: string;
}) {
  const isItems = kind === "items";
  const resource = response.resource;
  const itemMeta = itemMap?.items[String(resource.resourceId)];
  const runeMeta = runeData?.runeById[String(resource.resourceId)];
  const icon = isItems
    ? itemMap ? itemIconUrl(itemMap.version, resource.resourceId) : null
    : runeMeta ? runeIconUrl(runeMeta.icon) : null;
  const description = itemMeta?.plaintext ?? resource.description;
  const { stats, effects } = parseItemDescription(itemDetail?.description);
  const recipe = itemDetail?.from ?? [];
  const upgrades = itemDetail?.into ?? [];
  // A reference link is only a link if its destination exists: /lol/builds 404s while buildLab is
  // off, so the reference flag can never render one on its own.
  const flags = await analyticsFeatureFlags();
  const showBuildLabLinks = flags.buildReferenceLinks && flags.buildLab;
  const stableRows = response.championStats.filter((row) => row.games >= THIN_SAMPLE_GAMES);
  const thinRows = response.championStats.filter((row) => row.games < THIN_SAMPLE_GAMES);
  const visibleRows = stableRows.slice(0, VISIBLE_CHAMPION_ROWS);
  const foldedRows = [...stableRows.slice(VISIBLE_CHAMPION_ROWS), ...thinRows];
  const roles = roleShares(response.championStats);
  const purchases = response.purchases ?? null;
  const isBoots = purchases?.kind === "BOOTS";

  const table = (rows: BuildResourceChampionStat[]) => (
    <TableScroll>
      <Table className="min-w-[640px] text-sm">
        <thead className="border-b border-border">
          <tr>
            <Th className="w-full pl-4 sm:pl-5">Champion</Th>
            <Th>Role</Th>
            <Th align="right">Pick rate</Th>
            <Th align="right">Win rate</Th>
            <Th align="right">Share</Th>
            <Th align="right" className={cn(!showBuildLabLinks && "pr-4 sm:pr-5")}>
              Games
            </Th>
            {showBuildLabLinks ? (
              <Th align="right" className="pr-4 sm:pr-5">
                <span className="sr-only">Analyze</span>
              </Th>
            ) : null}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const champion = champions?.champions[String(row.championId)];
            const query = new URLSearchParams({ role: row.role });
            if (response.region !== "ALL") query.set("region", response.region);
            return (
              <tr
                key={`${row.championId}-${row.role}`}
                className="tierlist-row tierlist-cv border-b border-border/50 last:border-b-0"
              >
                <td className="py-2 pl-4 pr-3 sm:pl-5">
                  <Link
                    href={`/lol/champions/${row.championId}?${query.toString()}`}
                    className="group inline-flex items-center gap-2.5 rounded-md font-semibold text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  >
                    {champion && champions ? (
                      <Image
                        src={championIconUrl(champions.version, champion.id)}
                        alt=""
                        width={32}
                        height={32}
                        sizes="32px"
                        className="rounded-md border border-border/50"
                      />
                    ) : null}
                    <span className="transition-colors group-hover:text-primary">{championDisplayName(champion)}</span>
                  </Link>
                </td>
                <td className="whitespace-nowrap px-3 py-2 text-fg/72">
                  <span className="inline-flex items-center gap-1.5">
                    <LaneIcon role={row.role} className="h-4 w-4 text-muted" />
                    {roleDisplayLabel(row.role)}
                  </span>
                </td>
                <td className="type-tabular px-3 py-2 text-right tabular-nums text-fg/80">
                  {formatPercent(row.pickRate, { input: "ratio" })}
                </td>
                <td className="px-3 py-2 text-right">
                  <DataBar value={row.winRate} games={row.games} className="justify-end" />
                </td>
                <td className="type-tabular px-3 py-2 text-right tabular-nums text-fg/70">
                  {formatPercent(row.shareOfResourceUses, { input: "ratio" })}
                </td>
                <td
                  className={cn(
                    "type-tabular px-3 py-2 text-right tabular-nums text-fg/70",
                    !showBuildLabLinks && "pr-4 sm:pr-5"
                  )}
                >
                  {formatGames(row.games)}
                </td>
                {showBuildLabLinks ? (
                  <td className="py-2 pl-3 pr-4 text-right sm:pr-5">
                    <Link
                      href={`/lol/builds/${row.championId}?${query.toString()}`}
                      className="type-caption font-semibold text-muted transition-colors hover:text-primary"
                    >
                      Build Lab
                    </Link>
                  </td>
                ) : null}
              </tr>
            );
          })}
        </tbody>
      </Table>
    </TableScroll>
  );

  return (
    <div className="grid grid-cols-1 gap-4">
      <header className="toolbar grid gap-4 px-4 py-4 sm:px-5">
        <Link
          href={buildResourceHref(kind, null, response.region)}
          className="type-caption w-fit rounded-sm text-muted transition-colors hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        >
          ← All {kind}
        </Link>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
          {icon ? (
            <Image
              src={icon}
              alt=""
              width={64}
              height={64}
              sizes="64px"
              priority
              className={cn(
                "h-16 w-16 shrink-0 border border-border/60 bg-bg",
                isItems ? "rounded-card" : "rounded-full p-1"
              )}
            />
          ) : null}
          <div className="min-w-0 flex-1">
            <h1 className="type-page-title text-fg">{resource.name}</h1>
            {description ? <p className="type-ui mt-1 text-muted">{description}</p> : null}
            {stats.length > 0 ? (
              <ul aria-label="Stats" className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-sm">
                {stats.map((line) => {
                  const { value, label } = splitStat(line);
                  return (
                    <li key={line} className="max-w-none text-fg/80">
                      {value ? <span className="type-tabular font-semibold tabular-nums text-fg">{value} </span> : null}
                      {label}
                    </li>
                  );
                })}
              </ul>
            ) : null}
          </div>
          {showBuildLabLinks ? (
            <Link
              href="/lol/builds"
              className="type-ui font-semibold text-primary transition-colors hover:text-primary/80"
            >
              Compare in champion context
            </Link>
          ) : null}
        </div>
        {/* A readout line, not hero metrics: the table below is the answer. */}
        <dl className="flex flex-wrap items-center gap-x-6 gap-y-2 border-t border-border/70 pt-3 text-sm">
          <div className="flex items-center gap-2">
            <dt className="text-muted">Win rate</dt>
            <dd>
              <DataBar value={resource.winRate} games={resource.games} />
            </dd>
          </div>
          <div className="flex items-center gap-2">
            <dt className="text-muted">Pick rate</dt>
            <dd className="type-tabular font-semibold tabular-nums text-fg">
              {formatPercent(resource.pickRate, { input: "ratio" })}
            </dd>
          </div>
          <div className="flex items-center gap-2">
            <dt className="text-muted">Player-games</dt>
            <dd className="type-tabular font-semibold tabular-nums text-fg">{formatGames(resource.games)}</dd>
          </div>
          {itemDetail?.totalGold ? (
            <div className="flex items-center gap-2">
              <dt className="text-muted">Cost</dt>
              <dd className="type-tabular font-semibold tabular-nums text-fg">{formatGold(itemDetail.totalGold)}</dd>
            </div>
          ) : null}
          <div className="type-caption ml-auto text-muted">
            Patch {response.patch} · {activeRegionLabel}
          </div>
        </dl>
      </header>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        <div className="grid min-w-0 gap-4">
          {purchases ? (
            <section aria-labelledby="resource-purchases-heading" className="page-panel overflow-hidden">
              <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-1 px-4 py-4 sm:px-5">
                <div>
                  <h2 id="resource-purchases-heading" className="type-title text-fg">
                    When it&apos;s bought
                  </h2>
                  <p className="type-caption type-tabular mt-1 tabular-nums text-muted">
                    {purchases.averageMinutes != null
                      ? `Finished at ${formatGameClock(purchases.averageMinutes)} on average · `
                      : null}
                    {formatGames(purchases.games)} completions
                    {purchases.region !== response.region ? " · all regions" : null}
                  </p>
                </div>
                <p className="type-caption max-w-md text-muted">
                  Edge compares it with every other {isBoots ? "pair of boots" : "item finished in the same slot"},
                  in games with the same gold lead.
                  {purchases.stages.some((stage) => stage.isLowSample) ? " Faded rows have under 100 games." : null}
                </p>
              </div>
              <div className="border-t border-border">
                <TableScroll>
                  <Table className="min-w-[560px] text-sm">
                    <thead className="border-b border-border">
                      <tr>
                        <Th className="w-full pl-4 sm:pl-5">Bought as</Th>
                        <Th align="right">Finished at</Th>
                        <Th align="right">{isBoots ? "Share of boots" : "Share of slot"}</Th>
                        <Th align="right">Games</Th>
                        <Th align="right">Win rate</Th>
                        <Th align="right" className="pr-4 sm:pr-5">
                          Edge
                        </Th>
                      </tr>
                    </thead>
                    <tbody>
                      {purchases.stages.map((stage) => {
                        const lift = stage.adjustedWinRate - stage.stageWinRate;
                        return (
                          <tr
                            key={stage.stage}
                            className={cn(
                              "border-b border-border/50 last:border-b-0",
                              stage.isLowSample && "opacity-60"
                            )}
                          >
                            <td className="whitespace-nowrap py-2.5 pl-4 pr-3 font-semibold text-fg sm:pl-5">
                              {stageLabel(isBoots, stage.stage)}
                            </td>
                            <td className="type-tabular px-3 py-2.5 text-right tabular-nums text-fg/80">
                              {formatGameClock(stage.averageMinutes)}
                            </td>
                            <td className="type-tabular px-3 py-2.5 text-right tabular-nums text-fg/80">
                              {formatPercent(stage.shareOfStage, { input: "ratio" })}
                            </td>
                            <td className="type-tabular px-3 py-2.5 text-right tabular-nums text-fg/70">
                              {formatGames(stage.games)}
                            </td>
                            <td className="px-3 py-2.5 text-right">
                              <DataBar value={stage.winRate} games={stage.games} className="justify-end" />
                            </td>
                            <td
                              className={cn(
                                "type-tabular whitespace-nowrap py-2.5 pl-3 pr-4 text-right font-semibold tabular-nums sm:pr-5",
                                liftToneClass(lift, stage.isLowSample)
                              )}
                              title={`Gold-adjusted ${formatPercent(stage.adjustedWinRate, { input: "ratio" })} against ${formatPercent(stage.stageWinRate, { input: "ratio" })} for the slot`}
                            >
                              {formatLift(lift)}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </Table>
                </TableScroll>
              </div>
            </section>
          ) : null}

          <section aria-labelledby="resource-users-heading" className="page-panel overflow-hidden">
            <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-1 px-4 py-4 sm:px-5">
              <h2 id="resource-users-heading" className="type-title text-fg">
                {isItems ? "Who builds it" : "Who takes it"}
              </h2>
              <p className="type-caption max-w-xl text-muted">
                Pick rate is within each champion-role sample; share is that pair&apos;s cut of every use.
              </p>
            </div>
            {response.championStats.length === 0 ? (
              <p className="border-t border-border px-5 py-6 text-sm text-muted">
                No champion-level sample is available for this scope.
              </p>
            ) : (
              <div className="border-t border-border">
                {visibleRows.length > 0 ? (
                  table(visibleRows)
                ) : (
                  <p className="px-5 py-5 text-sm text-muted">
                    No champion-role pair has {THIN_SAMPLE_GAMES}+ games with this {isItems ? "item" : "rune"} yet.
                  </p>
                )}
                {foldedRows.length > 0 ? (
                  <details className="group border-t border-border">
                    <summary className="type-ui flex cursor-pointer list-none items-center gap-2 px-4 py-3 text-muted transition-colors hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary/40 sm:px-5 [&::-webkit-details-marker]:hidden">
                      <svg
                        viewBox="0 0 12 12"
                        aria-hidden="true"
                        className="size-3 shrink-0 transition-transform duration-150 group-open:rotate-90"
                      >
                        <path d="M4.5 3 7.5 6 4.5 9" stroke="currentColor" strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round" />
                      </svg>
                      {foldedRows.length} more
                      {thinRows.length > 0 ? `, ${thinRows.length} of them under ${THIN_SAMPLE_GAMES} games` : null}
                    </summary>
                    <div className="border-t border-border/60">{table(foldedRows)}</div>
                  </details>
                ) : null}
              </div>
            )}
          </section>
        </div>

        <aside className="grid gap-4">
          {roles.length > 0 ? (
            <section aria-labelledby="resource-roles-heading" className="page-panel overflow-hidden">
              <h2 id="resource-roles-heading" className="type-section px-4 py-3.5 text-fg sm:px-5">
                By role
              </h2>
              <ul className="border-t border-border">
                {roles.map((role) => (
                  <li
                    key={role.role}
                    className="flex max-w-none items-center justify-between gap-3 border-b border-border/50 px-4 py-2.5 last:border-b-0 sm:px-5"
                  >
                    <span className="flex min-w-0 items-center gap-2.5">
                      <LaneIcon role={role.role} className="h-4 w-4 shrink-0 text-muted" />
                      <span className="grid min-w-0">
                        <span className="truncate font-semibold text-fg">{roleDisplayLabel(role.role)}</span>
                        <span
                          className="type-caption type-tabular truncate tabular-nums text-muted"
                          title={`${formatGames(role.games)} games`}
                        >
                          {formatPercent(role.share, { input: "ratio" })} of uses
                        </span>
                      </span>
                    </span>
                    <DataBar value={role.wins / role.games} games={role.games} />
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          {effects.length > 0 ? (
            <section aria-labelledby="resource-effects-heading" className="page-panel overflow-hidden">
              <h2 id="resource-effects-heading" className="type-section px-4 py-3.5 text-fg sm:px-5">
                What it does
              </h2>
              <ul className="border-t border-border">
                {effects.map((effect) => (
                  <li
                    key={`${effect.kind}-${effect.name}`}
                    className="grid max-w-none gap-0.5 border-b border-border/50 px-4 py-3 last:border-b-0 sm:px-5"
                  >
                    <span className="text-sm">
                      <span className="font-semibold text-fg">{effect.name}</span>
                      <span className="type-caption ml-2 text-muted">{effect.kind === "active" ? "Active" : "Passive"}</span>
                    </span>
                    {effect.text ? <span className="text-sm text-fg/75">{effect.text}</span> : null}
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          {recipe.length > 0 || upgrades.length > 0 ? (
            <section aria-labelledby="resource-recipe-heading" className="page-panel overflow-hidden">
              <h2 id="resource-recipe-heading" className="type-section px-4 py-3.5 text-fg sm:px-5">
                Recipe
              </h2>
              {recipe.length > 0 ? (
                <ul className="border-t border-border py-1.5">
                  {recipe.map((component, index) => (
                    <ItemRow
                      key={`${component.itemId}-${index}`}
                      itemId={component.itemId}
                      itemMap={itemMap}
                      gold={component.totalGold}
                    />
                  ))}
                  {itemDetail?.combineGold ? (
                    <li className="type-caption flex max-w-none justify-between gap-3 px-4 pt-1.5 text-muted sm:px-5">
                      <span>Combine</span>
                      <span className="type-tabular tabular-nums">{formatGold(itemDetail.combineGold)}</span>
                    </li>
                  ) : null}
                </ul>
              ) : null}
              {upgrades.length > 0 ? (
                <div className="border-t border-border pt-3">
                  <h3 className="type-caption px-4 font-semibold text-muted sm:px-5">Builds into</h3>
                  <ul className="py-1.5">
                    {upgrades.map((upgradeId) => (
                      <ItemRow key={upgradeId} itemId={upgradeId} itemMap={itemMap} />
                    ))}
                  </ul>
                </div>
              ) : null}
            </section>
          ) : null}
        </aside>
      </div>

      <p className="type-caption max-w-3xl text-muted">
        Descriptive outcomes from completed Ranked Solo/Duo games. Build timing, champion strength,
        player skill, and game length all move the result; a higher row does not prove the{" "}
        {isItems ? "item" : "rune"} caused the win.
      </p>
    </div>
  );
}
