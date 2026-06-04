import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { fetchBackendJson } from "@/lib/backendCall";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { getBackendBaseUrl } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];
const EXAMPLE_SUMMONER_HREF = "/lol/summoners/kr/Hide%20on%20bush-KR1";

const SECONDARY_LINKS = [
  { label: "Champions", href: "/lol/champions" },
  { label: "Pro Builds", href: "/lol/pro-builds" }
] as const;

export default async function HomePage({
  searchParams
}: {
  searchParams?: Promise<{ region?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const tierListQuery = new URLSearchParams();
  if (activeRegion !== "ALL") tierListQuery.set("region", activeRegion);
  const [{ version, champions }, analyticsStatus, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchLolAnalyticsStatus(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/lol/analytics/tierlist?${tierListQuery.toString()}`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  const patch = analyticsStatus?.patch ?? tierListRes.body?.patch ?? null;
  const entries = tierListRes.ok
    ? normalizeTierListEntries(tierListRes.body?.entries ?? [])
    : [];
  const topRows = entries.slice(0, 8);
  const trendingRows = entries
    .slice()
    .sort((a, b) => b.winRate - a.winRate || b.games - a.games)
    .slice(0, 3);
  const hrefWithRegion = (href: string) =>
    activeRegion !== "ALL"
      ? `${href}${href.includes("?") ? "&" : "?"}region=${encodeURIComponent(activeRegion)}`
      : href;

  return (
    <div className="grid gap-10">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        {/* Identity zone */}
        <div className="flex flex-wrap items-center gap-1.5 sm:gap-2">
          {patch ? (
            <Badge className="border-primary/40 bg-primary/10 text-primary">Patch {patch}</Badge>
          ) : null}
          <Badge className="border-success/40 bg-success/10 text-success">Current Analytics Data</Badge>
          <Badge>{activeRegionLabel}</Badge>
        </div>

        <h1 className="type-display mt-4 max-w-3xl">
          League tier lists, matchups, and builds.
        </h1>

        {/* Action zone */}
        <div className="mt-8 grid gap-3 lg:grid-cols-[minmax(0,16rem)_minmax(0,1fr)] lg:items-center">
          <Link
            href={hrefWithRegion("/lol/tierlist")}
            className="type-ui inline-flex h-14 items-center justify-center rounded-control bg-primary px-5 text-center font-semibold text-primary-fg transition hover:bg-primary/92"
          >
            Browse tier list
          </Link>
          <GlobalSearchLauncher
            variant="hero"
            className="h-14 w-full px-4 text-left lg:max-w-none"
          />
        </div>

        {/* Navigation zone */}
        <div className="mt-8 grid gap-4 border-t border-border/25 pt-4">
          <div className="grid gap-2 sm:grid-cols-[auto_1fr] sm:items-center sm:gap-4">
            <p className="type-kicker text-fg/65">Region</p>
            <AnalyticsRegionFilter
              options={regionOptions}
              activeRegion={activeRegion}
              className="flex flex-wrap gap-x-4 gap-y-1.5"
            />
          </div>

          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            {SECONDARY_LINKS.map((link) => (
              <Link
                key={link.href}
                href={hrefWithRegion(link.href)}
                className="type-ui inline-flex items-center border-b border-border/40 px-0.5 pb-1 text-fg/68 transition hover:border-border/68 hover:text-fg"
              >
                {link.label}
              </Link>
            ))}
          </div>
        </div>
      </section>

      <section className="grid gap-5 lg:grid-cols-[1.6fr_1fr]">
        <Card className="p-0">
          <div className="flex items-center justify-between border-b border-border/50 px-5 py-3.5">
            <div>
              <h2 className="type-section">Top Champions · Platinum+</h2>
              <p className="type-ui mt-1 text-muted">Best-performing picks for this patch</p>
            </div>
            <Link href={hrefWithRegion("/lol/tierlist")} className="type-ui font-semibold text-primary hover:underline">
              View full tier list
            </Link>
          </div>
          {topRows.length === 0 ? (
            <div className="px-4 py-4">
              <p className="text-sm text-fg/75">Tier list data is unavailable right now.</p>
              <p className="mt-1 text-xs text-muted">This usually means data for the current patch is still being processed. Try selecting a different region above or check back soon.</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[420px] text-left text-sm">
                <thead className="type-overline text-muted">
                  <tr className="border-b border-border/30">
                    <th className="px-4 py-2">Champion</th>
                    <th className="px-3 py-2">Lane</th>
                    <th className="px-3 py-2 text-right">Win Rate</th>
                    <th className="px-3 py-2 text-right">Games</th>
                  </tr>
                </thead>
                <tbody>
                  {topRows.map((entry, index) => {
                    const champ = champions[String(entry.championId)];
                    const name = champ?.name ?? `Champion ${entry.championId}`;
                    const title = champ?.title ?? "";
                    return (
                      <tr key={`${entry.championId}-${entry.role}`} className="border-b border-border/20">
                        <td className="px-4 py-2.5">
                          <Link href={hrefWithRegion(`/lol/champions/${entry.championId}?role=${entry.role}`)} className="flex min-w-0 items-center gap-2.5 hover:underline">
                            <Image
                              src={championIconUrl(version, champ?.id ?? "Unknown")}
                              alt={name}
                              width={30}
                              height={30}
                              sizes="30px"
                              priority={index === 0}
                              className="rounded-md"
                            />
                            <span className="min-w-0">
                              <span className="block truncate font-medium">{name}</span>
                              {title ? <span className="block truncate text-xs text-muted">{title}</span> : null}
                            </span>
                          </Link>
                        </td>
                        <td className="px-3 py-2.5 text-xs text-fg/80">
                          <span className="inline-flex items-center gap-1.5">
                            <LaneIcon role={entry.role} className="h-4 w-4 shrink-0 text-fg/55" />
                            {roleDisplayLabel(entry.role)}
                          </span>
                        </td>
                        <td className="px-3 py-2.5 text-right text-fg/90">
                          {formatPercent(entry.winRate, { decimals: 2 })}
                        </td>
                        <td className="px-3 py-2.5 text-right text-fg/70">{formatGames(entry.games)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </Card>

        <div className="grid content-start gap-5">
          <Card className="p-5">
            <h2 className="type-section">Hot Right Now</h2>
            <div className="mt-4 grid gap-2">
              {trendingRows.length === 0 ? (
                <p className="text-sm text-muted">No featured champions to show right now.</p>
              ) : (
                trendingRows.map((entry, index) => {
                  const champ = champions[String(entry.championId)];
                  const name = champ?.name ?? `Champion ${entry.championId}`;
                  return (
                    <Link
                      key={`${entry.championId}-trend`}
                      href={hrefWithRegion(`/lol/champions/${entry.championId}?role=${entry.role}`)}
                      className="flex items-center gap-3 border-b border-border/20 py-2.5 transition hover:border-border/45"
                    >
                      <Image
                        src={championIconUrl(version, champ?.id ?? "Unknown")}
                        alt={name}
                        width={32}
                        height={32}
                        sizes="32px"
                        priority={index === 0}
                        className="rounded-md"
                      />
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-fg">{name}</p>
                        <p className="text-xs text-muted">
                          {roleDisplayLabel(entry.role)} · {formatPercent(entry.winRate, { decimals: 1 })} WR
                        </p>
                      </div>
                    </Link>
                  );
                })
              )}
            </div>
          </Card>

          <Card className="p-5">
            <p className="type-kicker text-fg/56">Player Tools</p>
            <div className="mt-4 grid gap-3 border-t border-border/25 pt-4">
              <Link
                href={hrefWithRegion("/lol/champions")}
                className="type-ui inline-flex items-center justify-between gap-3 text-fg/84 transition hover:text-fg"
              >
                <span>Champions</span>
                <span className="text-fg/40" aria-hidden="true">/</span>
              </Link>
              <Link
                href={hrefWithRegion("/lol/pro-builds")}
                className="type-ui inline-flex items-center justify-between gap-3 border-t border-border/20 pt-3 text-fg/84 transition hover:text-fg"
              >
                <span>Pro Builds</span>
                <span className="text-fg/40" aria-hidden="true">/</span>
              </Link>
              <Link
                href={EXAMPLE_SUMMONER_HREF}
                className="type-ui inline-flex items-center justify-between gap-3 border-t border-border/20 pt-3 text-fg/84 transition hover:text-fg"
              >
                <span>Example Profile</span>
                <span className="text-fg/40" aria-hidden="true">/</span>
              </Link>
            </div>
          </Card>
        </div>
      </section>
    </div>
  );
}
