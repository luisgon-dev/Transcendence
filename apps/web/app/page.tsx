import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

const QUICK_LINKS = [
  { label: "S Tier", href: "/tierlist" },
  { label: "Best Top", href: "/tierlist?role=TOP" },
  { label: "Best Jungle", href: "/tierlist?role=JUNGLE" },
  { label: "Best Mid", href: "/tierlist?role=MIDDLE" },
  { label: "Best Bot", href: "/tierlist?role=BOTTOM" },
  { label: "Best Support", href: "/tierlist?role=UTILITY" }
] as const;

function withRegion(href: string, activeRegion: string) {
  if (activeRegion === "ALL") return href;
  return `${href}${href.includes("?") ? "&" : "?"}region=${encodeURIComponent(activeRegion)}`;
}

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

  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(
      `${getBackendBaseUrl()}/api/analytics/tierlist?${tierListQuery.toString()}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  const patch = version.split(".").slice(0, 2).join(".");
  const entries = tierListRes.ok ? normalizeTierListEntries(tierListRes.body?.entries ?? []) : [];
  const topRows = entries.slice(0, 8);
  const spotlightRows = topRows.slice(0, 3);
  const trendingRows = entries
    .slice()
    .sort((a, b) => b.winRate - a.winRate || b.games - a.games)
    .slice(0, 3);

  const topChampion = topRows[0] ?? null;
  const topChampionData = topChampion ? champions[String(topChampion.championId)] : null;
  const sampleNotice = (tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike;
  const roleCoverage = new Set(entries.map((entry) => entry.role)).size;

  return (
    <div className="grid gap-6 md:gap-7">
      <section className="glass-card mesh-highlight relative overflow-hidden rounded-[2rem] p-6 sm:p-8">
        <div className="pointer-events-none absolute inset-y-0 left-[-8rem] w-[18rem] rounded-full bg-primary/18 blur-3xl" />
        <div className="pointer-events-none absolute right-[-6rem] top-[-4rem] h-72 w-72 rounded-full bg-primary-2/18 blur-3xl" />
        <div className="pointer-events-none absolute bottom-[-6rem] right-20 h-52 w-52 rounded-full bg-cyan-400/10 blur-3xl" />

        <div className="relative grid gap-8 lg:grid-cols-[1.25fr_0.85fr] lg:items-start">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <Badge className="border-primary/40 bg-primary/10 text-primary">Patch {patch}</Badge>
              <Badge className="border-success/40 bg-success/10 text-success">Live Analysis</Badge>
              <Badge>{activeRegionLabel}</Badge>
            </div>

            <h1 className="mt-5 max-w-3xl font-[var(--font-sora)] text-4xl font-semibold tracking-tight sm:text-5xl">
              Read the patch with actual exposure behind the numbers.
            </h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-fg/75 sm:text-base">
              Tier lists, matchups, builds, and champion pages tied to the current patch, explicit
              region filters, and a visible confidence signal instead of a black-box rank.
            </p>

            <GlobalSearchLauncher variant="hero" className="mt-7 h-14 w-full max-w-2xl px-4 text-left" />

            <div className="mt-5 flex flex-wrap gap-2">
              <AnalyticsRegionFilter
                options={regionOptions}
                activeRegion={activeRegion}
                className="flex flex-wrap gap-2"
              />
            </div>

            <div className="mt-5 flex flex-wrap gap-2.5">
              {QUICK_LINKS.map((link) => (
                <Link
                  key={link.href}
                  href={withRegion(link.href, activeRegion)}
                  className="rounded-full border border-border/65 bg-white/[0.04] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.10] hover:text-fg"
                >
                  {link.label}
                </Link>
              ))}
              <Link
                href={withRegion("/matchups", activeRegion)}
                className="rounded-full border border-border/65 bg-white/[0.04] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.10] hover:text-fg"
              >
                Matchups
              </Link>
              <Link
                href={withRegion("/pro-builds", activeRegion)}
                className="rounded-full border border-border/65 bg-white/[0.04] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.10] hover:text-fg"
              >
                Pro Builds
              </Link>
            </div>
          </div>

          <div className="grid gap-4">
            <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-1">
              <Card className="border-white/10 bg-black/20 p-4">
                <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Meta Scope</p>
                <p className="mt-2 text-3xl font-semibold text-fg">{topRows.length}</p>
                <p className="text-sm text-fg/70">top-ranked champions surfaced for this region</p>
              </Card>
              <Card className="border-white/10 bg-black/20 p-4">
                <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Role Coverage</p>
                <p className="mt-2 text-3xl font-semibold text-fg">{roleCoverage}</p>
                <p className="text-sm text-fg/70">roles represented in the current patch slice</p>
              </Card>
              <Card className="border-white/10 bg-black/20 p-4">
                <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Featured Region</p>
                <p className="mt-2 text-2xl font-semibold text-fg">{activeRegionLabel}</p>
                <p className="text-sm text-fg/70">analytics are scoped and persisted explicitly</p>
              </Card>
            </div>

            <Card className="overflow-hidden border-primary/30 bg-gradient-to-br from-primary/14 via-black/10 to-primary-2/10 p-0">
              {topChampion ? (
                <Link
                  href={withRegion(`/champions/${topChampion.championId}?role=${topChampion.role}`, activeRegion)}
                  className="block p-5 transition hover:bg-white/[0.03]"
                >
                  <div className="flex items-center gap-4">
                    <Image
                      src={championIconUrl(version, topChampionData?.id ?? "Unknown")}
                      alt={topChampionData?.name ?? `Champion ${topChampion.championId}`}
                      width={64}
                      height={64}
                      className="rounded-2xl border border-white/10"
                    />
                    <div className="min-w-0">
                      <p className="text-[11px] uppercase tracking-[0.24em] text-primary/80">
                        Top Read Right Now
                      </p>
                      <h2 className="mt-1 truncate font-[var(--font-sora)] text-2xl font-semibold text-fg">
                        {topChampionData?.name ?? `Champion ${topChampion.championId}`}
                      </h2>
                      <p className="text-sm text-fg/70">
                        {roleDisplayLabel(topChampion.role)} ·{" "}
                        {formatPercent(topChampion.winRate, { decimals: 1 })} WR ·{" "}
                        {formatGames(topChampion.games)} games
                      </p>
                    </div>
                  </div>
                </Link>
              ) : (
                <div className="p-5">
                  <p className="text-sm text-fg/70">Top champion data is not available yet.</p>
                </div>
              )}
            </Card>
          </div>
        </div>
      </section>

      <AnalyticsSampleBanner sample={sampleNotice} />

      {spotlightRows.length > 0 ? (
        <section className="grid gap-4 lg:grid-cols-3">
          {spotlightRows.map((entry, index) => {
            const champ = champions[String(entry.championId)];
            const name = champ?.name ?? `Champion ${entry.championId}`;

            return (
              <Link
                key={`${entry.championId}-${entry.role}-spotlight`}
                href={withRegion(`/champions/${entry.championId}?role=${entry.role}`, activeRegion)}
                className="group"
              >
                <Card className="relative h-full overflow-hidden border-white/10 bg-black/20 p-5 transition hover:-translate-y-0.5 hover:bg-white/[0.05]">
                  <div className="pointer-events-none absolute right-[-1.25rem] top-[-1.25rem] h-24 w-24 rounded-full bg-primary/12 blur-2xl" />
                  <div className="relative flex items-start gap-4">
                    <Image
                      src={championIconUrl(version, champ?.id ?? "Unknown")}
                      alt={name}
                      width={56}
                      height={56}
                      className="rounded-2xl border border-white/10"
                    />
                    <div className="min-w-0">
                      <p className="text-[11px] uppercase tracking-[0.24em] text-primary/80">
                        Meta Spotlight #{index + 1}
                      </p>
                      <h2 className="mt-1 truncate font-[var(--font-sora)] text-xl font-semibold text-fg group-hover:text-primary">
                        {name}
                      </h2>
                      <p className="text-sm text-fg/70">{roleDisplayLabel(entry.role)}</p>
                    </div>
                  </div>
                  <div className="relative mt-5 grid grid-cols-2 gap-3 text-sm">
                    <div className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2">
                      <p className="text-[11px] uppercase tracking-[0.2em] text-fg/50">Win Rate</p>
                      <p className="mt-1 text-lg font-semibold text-fg">
                        {formatPercent(entry.winRate, { decimals: 2 })}
                      </p>
                    </div>
                    <div className="rounded-xl border border-white/10 bg-white/[0.03] px-3 py-2">
                      <p className="text-[11px] uppercase tracking-[0.2em] text-fg/50">Exposure</p>
                      <p className="mt-1 text-lg font-semibold text-fg">{formatGames(entry.games)}</p>
                    </div>
                  </div>
                </Card>
              </Link>
            );
          })}
        </section>
      ) : null}

      <section className="grid gap-4 lg:grid-cols-[1.45fr_0.9fr]">
        <Card className="overflow-hidden p-0">
          <div className="flex items-center justify-between border-b border-border/50 px-4 py-3">
            <div>
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Tier List Platinum+</h2>
              <p className="text-xs text-muted">Top performers this patch</p>
            </div>
            <Link href={withRegion("/tierlist", activeRegion)} className="text-xs text-primary hover:underline">
              View full tier list
            </Link>
          </div>
          {topRows.length === 0 ? (
            <p className="px-4 py-4 text-sm text-muted">Tier list data is currently unavailable.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[560px] text-left text-sm">
                <thead className="text-[11px] uppercase tracking-wider text-muted">
                  <tr className="border-b border-border/30">
                    <th className="px-4 py-2">Champion</th>
                    <th className="px-3 py-2">Role</th>
                    <th className="px-3 py-2 text-right">Win Rate</th>
                    <th className="px-3 py-2 text-right">Games</th>
                  </tr>
                </thead>
                <tbody>
                  {topRows.map((entry) => {
                    const champ = champions[String(entry.championId)];
                    const name = champ?.name ?? `Champion ${entry.championId}`;
                    const title = champ?.title ?? "";

                    return (
                      <tr key={`${entry.championId}-${entry.role}`} className="border-b border-border/20">
                        <td className="px-4 py-2.5">
                          <Link
                            href={withRegion(`/champions/${entry.championId}?role=${entry.role}`, activeRegion)}
                            className="flex min-w-0 items-center gap-2.5 hover:underline"
                          >
                            <Image
                              src={championIconUrl(version, champ?.id ?? "Unknown")}
                              alt={name}
                              width={30}
                              height={30}
                              className="rounded-md"
                            />
                            <span className="min-w-0">
                              <span className="block truncate font-medium">{name}</span>
                              {title ? (
                                <span className="block truncate text-xs text-muted">{title}</span>
                              ) : null}
                            </span>
                          </Link>
                        </td>
                        <td className="px-3 py-2.5 text-xs text-fg/80">{roleDisplayLabel(entry.role)}</td>
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

        <div className="grid gap-4">
          <Card className="p-5">
            <h2 className="font-[var(--font-sora)] text-lg font-semibold">Trending</h2>
            <p className="mt-1 text-sm text-fg/70">
              Fastest high-win-rate reads from the current patch slice.
            </p>
            <div className="mt-4 grid gap-2.5">
              {trendingRows.length === 0 ? (
                <p className="text-sm text-muted">No trend data available.</p>
              ) : (
                trendingRows.map((entry) => {
                  const champ = champions[String(entry.championId)];
                  const name = champ?.name ?? `Champion ${entry.championId}`;

                  return (
                    <Link
                      key={`${entry.championId}-trend`}
                      href={withRegion(`/champions/${entry.championId}?role=${entry.role}`, activeRegion)}
                      className="rounded-xl border border-border/60 bg-white/[0.03] p-3 transition hover:bg-white/[0.08]"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-medium text-fg">{name}</p>
                          <p className="mt-1 text-xs text-muted">
                            {roleDisplayLabel(entry.role)} · {formatGames(entry.games)} games
                          </p>
                        </div>
                        <p className="text-sm font-semibold text-primary">
                          {formatPercent(entry.winRate, { decimals: 1 })}
                        </p>
                      </div>
                    </Link>
                  );
                })
              )}
            </div>
          </Card>

          <Card className="border-white/10 bg-black/20 p-5">
            <h2 className="font-[var(--font-sora)] text-lg font-semibold">Explore the Data</h2>
            <p className="mt-2 text-sm text-fg/75">
              Jump into the patch from different angles depending on whether you want broad meta
              reads, opponent-specific counters, or champion-specific build detail.
            </p>
            <div className="mt-4 grid gap-2.5">
              <Link
                href={withRegion("/tierlist", activeRegion)}
                className="rounded-xl border border-border/60 bg-white/[0.03] px-4 py-3 text-sm text-fg/85 transition hover:bg-white/[0.08]"
              >
                Tier List by role and rank
              </Link>
              <Link
                href={withRegion("/matchups", activeRegion)}
                className="rounded-xl border border-border/60 bg-white/[0.03] px-4 py-3 text-sm text-fg/85 transition hover:bg-white/[0.08]"
              >
                Matchup explorer and counter pages
              </Link>
              <Link
                href={withRegion("/champions", activeRegion)}
                className="rounded-xl border border-border/60 bg-white/[0.03] px-4 py-3 text-sm text-fg/85 transition hover:bg-white/[0.08]"
              >
                Champion detail pages with builds and runes
              </Link>
              <Link
                href={withRegion("/pro-builds", activeRegion)}
                className="rounded-xl border border-border/60 bg-white/[0.03] px-4 py-3 text-sm text-fg/85 transition hover:bg-white/[0.08]"
              >
                Pro builds and recent high-ELO match feed
              </Link>
            </div>
          </Card>
        </div>
      </section>
    </div>
  );
}
