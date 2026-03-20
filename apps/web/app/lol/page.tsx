import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { getBackendBaseUrl } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

const QUICK_LINKS = [
  { label: "All Tier List", href: "/lol/tierlist" },
  { label: "Top Lane", href: "/lol/tierlist?role=TOP" },
  { label: "Jungle", href: "/lol/tierlist?role=JUNGLE" },
  { label: "Mid Lane", href: "/lol/tierlist?role=MIDDLE" },
  { label: "Bot Lane", href: "/lol/tierlist?role=BOTTOM" },
  { label: "Support", href: "/lol/tierlist?role=UTILITY" }
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
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/lol/analytics/tierlist?${tierListQuery.toString()}`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  const patch = version.split(".").slice(0, 2).join(".");
  const entries = tierListRes.ok
    ? normalizeTierListEntries(tierListRes.body?.entries ?? [])
    : [];
  const topRows = entries.slice(0, 8);
  const trendingRows = entries
    .slice()
    .sort((a, b) => b.winRate - a.winRate || b.games - a.games)
    .slice(0, 3);

  return (
    <div className="grid gap-10">
      <section className="glass-card mesh-highlight rounded-[2rem] p-6 sm:p-8 md:p-10">
        {/* Identity zone */}
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">Patch {patch}</Badge>
          <Badge className="border-success/40 bg-success/10 text-success">Live Patch Data</Badge>
          <Badge>{activeRegionLabel}</Badge>
        </div>

        <h1 className="mt-4 max-w-2xl font-[var(--font-sora)] text-3xl font-semibold tracking-tight sm:text-4xl">
          League of Legends picks, builds, and matchup tools for the current patch.
        </h1>
        <p className="mt-2 max-w-2xl text-sm text-fg/75 sm:text-base">
          Start with the tier list, drill into champion pages by role, or pull up player history in a few clicks.
        </p>

        {/* Action zone */}
        <GlobalSearchLauncher variant="hero" className="mt-8 h-14 w-full max-w-2xl px-4 text-left" />

        {/* Navigation zone */}
        <div className="mt-8 space-y-3">
          <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} className="flex flex-wrap gap-2" />

          <div className="flex flex-wrap items-center gap-2">
            {QUICK_LINKS.map((link) => (
              <Link
                key={link.href}
                href={`${link.href}${activeRegion !== "ALL" ? `${link.href.includes("?") ? "&" : "?"}region=${encodeURIComponent(activeRegion)}` : ""}`}
                className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-2 text-sm text-fg/80 transition hover:bg-white/[0.08] hover:text-fg active:bg-white/[0.10]"
              >
                {link.label}
              </Link>
            ))}
            <span className="mx-1 h-4 w-px bg-border/50" aria-hidden="true" />
            <Link
              href={`/lol/matchups${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.08] hover:text-fg"
            >
              Matchups
            </Link>
            <Link
              href={`/lol/pro-builds${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
              className="rounded-full border border-primary/50 bg-primary/10 px-3 py-1.5 text-sm text-primary transition hover:bg-primary/20"
            >
              Pro Builds
            </Link>
            <Link
              href="/lol/summoners/na/Faker-KR1"
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.08] hover:text-fg"
            >
              Player Search
            </Link>
          </div>
        </div>
      </section>

      <section className="grid gap-5 lg:grid-cols-[1.6fr_1fr]">
        <Card className="p-0">
          <div className="flex items-center justify-between border-b border-border/50 px-5 py-3.5">
            <div>
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Top Champions · Platinum+</h2>
              <p className="mt-0.5 text-xs text-muted">Best-performing picks for this patch</p>
            </div>
            <Link href={`/lol/tierlist${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`} className="text-xs text-primary hover:underline">
              View full tier list
            </Link>
          </div>
          {topRows.length === 0 ? (
            <p className="px-4 py-4 text-sm text-muted">Tier list data is unavailable right now.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[420px] text-left text-sm">
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
                          <Link href={`/lol/champions/${entry.championId}?role=${entry.role}${activeRegion !== "ALL" ? `&region=${encodeURIComponent(activeRegion)}` : ""}`} className="flex min-w-0 items-center gap-2.5 hover:underline">
                            <Image
                              src={championIconUrl(version, champ?.id ?? "Unknown")}
                              alt={name}
                              width={30}
                              height={30}
                              className="rounded-md"
                            />
                            <span className="min-w-0">
                              <span className="block truncate font-medium">{name}</span>
                              {title ? <span className="block truncate text-xs text-muted">{title}</span> : null}
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

        <div className="grid content-start gap-5">
          <Card className="p-5">
            <h2 className="font-[var(--font-sora)] text-lg font-semibold">Hot Right Now</h2>
            <div className="mt-4 grid gap-2">
              {trendingRows.length === 0 ? (
                <p className="text-sm text-muted">No featured champions to show right now.</p>
              ) : (
                trendingRows.map((entry) => {
                  const champ = champions[String(entry.championId)];
                  const name = champ?.name ?? `Champion ${entry.championId}`;
                  return (
                    <Link
                      key={`${entry.championId}-trend`}
                      href={`/lol/champions/${entry.championId}?role=${entry.role}${activeRegion !== "ALL" ? `&region=${encodeURIComponent(activeRegion)}` : ""}`}
                      className="flex items-center gap-3 rounded-lg border border-border/60 bg-white/[0.03] px-3 py-2.5 transition hover:bg-white/[0.08]"
                    >
                      <Image
                        src={championIconUrl(version, champ?.id ?? "Unknown")}
                        alt={name}
                        width={32}
                        height={32}
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

          <Card className="border-primary/30 bg-gradient-to-br from-primary/8 to-primary-2/5 p-5">
            <h2 className="font-[var(--font-sora)] text-base font-semibold text-primary">Player Tools</h2>
            <p className="mt-1.5 text-sm text-fg/80">
              Matchup breakdowns, pro builds, and player profiles.
            </p>
            <div className="mt-3.5 flex flex-wrap gap-2">
              <Link
                href={`/lol/matchups${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
                className="inline-flex rounded-md border border-primary/40 bg-primary/15 px-3 py-1.5 text-sm font-medium text-primary transition hover:bg-primary/25"
              >
                Matchups
              </Link>
              <Link
                href={`/lol/pro-builds${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
                className="inline-flex rounded-md border border-border/70 bg-white/[0.05] px-3 py-1.5 text-sm text-fg/85 transition hover:bg-white/[0.10]"
              >
                Pro Builds
              </Link>
              <Link
                href="/lol/summoners/na/Faker-KR1"
                className="inline-flex rounded-md border border-border/70 bg-white/[0.05] px-3 py-1.5 text-sm text-fg/85 transition hover:bg-white/[0.10]"
              >
                Player Page
              </Link>
            </div>
          </Card>
        </div>
      </section>
    </div>
  );
}
