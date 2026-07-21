import Image from "next/image";
import Link from "next/link";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { buildResourceHref, type BuildResourceDetailResponse, type BuildResourceKind } from "@/lib/buildResources";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  itemIconUrl,
  runeIconUrl,
  type ChampionMap,
  type ItemMap,
  type RuneStaticData
} from "@/lib/staticData";

export function BuildResourceDetail({
  kind,
  response,
  champions,
  itemMap,
  runeData,
  activeRegionLabel
}: {
  kind: BuildResourceKind;
  response: BuildResourceDetailResponse;
  champions: ChampionMap | null;
  itemMap: ItemMap | null;
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

  return (
    <div className="grid gap-7">
      <Link href={buildResourceHref(kind, null, response.region)} className="type-ui w-fit text-fg/65 transition hover:text-primary">
        ← Back to {kind}
      </Link>

      <section className="page-hero p-6 sm:p-8">
        <div className="grid gap-6 md:grid-cols-[auto_minmax(0,1fr)] md:items-center">
          {icon ? (
            <Image src={icon} alt="" width={112} height={112} sizes="112px" priority className="h-24 w-24 rounded-2xl border border-border/60 bg-bg shadow-card sm:h-28 sm:w-28" />
          ) : (
            <div className="grid h-24 w-24 place-items-center rounded-2xl border border-border/60 bg-bg text-sm text-muted sm:h-28 sm:w-28">{resource.resourceId}</div>
          )}
          <div className="min-w-0">
            <div className="flex flex-wrap gap-2">
              <Badge>{activeRegionLabel}</Badge>
              <Badge className="border-primary/35 bg-primary/10 text-primary">Patch {response.patch}</Badge>
              <Badge>{isItems ? "Item" : "Rune"} {resource.resourceId}</Badge>
            </div>
            <h1 className="type-display mt-4">{resource.name}</h1>
            {description ? <p className="type-lead mt-3 max-w-3xl">{description}</p> : null}
          </div>
        </div>

        <div className="mt-7 grid gap-3 border-t border-border/30 pt-6 sm:grid-cols-3">
          <HeroStat label="Observed pick rate" value={formatPercent(resource.pickRate, { input: "ratio" })} />
          <HeroStat label="Observed win rate" value={formatPercent(resource.winRate, { input: "ratio" })} valueClassName={winRateColorClass(resource.winRate)} />
          <HeroStat label="Player-games" value={formatGames(resource.games)} />
        </div>
      </section>

      <Card className="overflow-hidden p-0">
        <div className="border-b border-border/45 px-5 py-5 sm:px-6">
          <p className="type-kicker text-primary/80">Champion fit</p>
          <h2 className="type-title mt-2">Who uses {resource.name}</h2>
          <p className="type-ui mt-2 max-w-3xl text-muted">
            Pick rate is measured inside each champion-role sample. Share shows how much that champion-role pair contributes to all observed uses.
          </p>
        </div>
        {response.championStats.length === 0 ? (
          <p className="p-6 text-sm text-muted">No champion-level sample is available for this scope.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-left text-sm">
              <thead className="type-overline text-muted">
                <tr className="border-b border-border/30">
                  <th className="px-5 py-3 sm:px-6">Champion</th>
                  <th className="px-3 py-3">Role</th>
                  <th className="px-3 py-3 text-right">Pick rate</th>
                  <th className="px-3 py-3 text-right">Win rate</th>
                  <th className="px-3 py-3 text-right">Share</th>
                  <th className="px-5 py-3 text-right sm:px-6">Games</th>
                </tr>
              </thead>
              <tbody>
                {response.championStats.map((row) => {
                  const champion = champions?.champions[String(row.championId)];
                  const query = new URLSearchParams({ role: row.role });
                  if (response.region !== "ALL") query.set("region", response.region);
                  return (
                    <tr key={`${row.championId}-${row.role}`} className="border-b border-border/20 transition hover:bg-surface-2/35">
                      <td className="px-5 py-3 sm:px-6">
                        <Link href={`/lol/champions/${row.championId}?${query.toString()}`} className="inline-flex items-center gap-2.5 font-semibold text-fg hover:text-primary">
                          {champion && champions ? <Image src={championIconUrl(champions.version, champion.id)} alt="" width={34} height={34} sizes="34px" className="rounded-lg" /> : null}
                          <span>{champion?.name ?? `Champion ${row.championId}`}</span>
                        </Link>
                      </td>
                      <td className="px-3 py-3 text-fg/72">
                        <span className="inline-flex items-center gap-1.5"><LaneIcon role={row.role} className="h-4 w-4 text-fg/52" />{roleDisplayLabel(row.role)}</span>
                      </td>
                      <td className="px-3 py-3 text-right">{formatPercent(row.pickRate, { input: "ratio" })}</td>
                      <td className={`px-3 py-3 text-right font-semibold ${winRateColorClass(row.winRate)}`}>{formatPercent(row.winRate, { input: "ratio" })}</td>
                      <td className="px-3 py-3 text-right text-fg/68">{formatPercent(row.shareOfResourceUses, { input: "ratio" })}</td>
                      <td className="px-5 py-3 text-right text-fg/72 sm:px-6">{formatGames(row.games)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <p className="type-caption max-w-4xl text-muted">
        These are descriptive outcomes from completed Ranked Solo/Duo games. Build timing, champion strength, player skill, and game length all affect the result; a higher row does not prove the resource caused the win.
      </p>
    </div>
  );
}

function HeroStat({ label, value, valueClassName = "" }: { label: string; value: string; valueClassName?: string }) {
  return (
    <div className="surface-subtle rounded-card p-4">
      <p className="type-kicker text-fg/50">{label}</p>
      <p className={`type-panel-title mt-2 ${valueClassName}`}>{value}</p>
    </div>
  );
}
