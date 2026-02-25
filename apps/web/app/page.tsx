import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { fetchBackendJson } from "@/lib/backendCall";
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

export default async function HomePage() {
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/analytics/tierlist`, {
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
    <div className="grid gap-6">
      <section className="glass-card mesh-highlight relative overflow-hidden rounded-[2rem] p-6 sm:p-8">
        <div className="pointer-events-none absolute -left-24 top-0 h-56 w-56 rounded-full bg-primary/20 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-24 right-0 h-56 w-56 rounded-full bg-primary-2/20 blur-3xl" />

        <div className="relative">
          <div className="flex flex-wrap items-center gap-2">
            <Badge className="border-primary/40 bg-primary/10 text-primary">Patch {patch}</Badge>
            <Badge className="border-success/40 bg-success/10 text-success">Live Analysis</Badge>
          </div>

          <h1 className="mt-4 max-w-2xl font-[var(--font-sora)] text-3xl font-semibold tracking-tight sm:text-4xl">
            Transcendence
          </h1>
          <p className="mt-2 max-w-2xl text-sm text-fg/75 sm:text-base">
            League of Legends win rates, builds, tier lists, and matchup intelligence.
          </p>

          <GlobalSearchLauncher variant="hero" className="mt-6 h-14 w-full max-w-2xl px-4 text-left" />

          <div className="mt-5 flex flex-wrap gap-2">
            {QUICK_LINKS.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.08] hover:text-fg"
              >
                {link.label}
              </Link>
            ))}
            <Link
              href="/matchups"
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/80 transition hover:bg-white/[0.08] hover:text-fg"
            >
              Matchups
            </Link>
            <Link
              href="/pro-builds"
              className="rounded-full border border-primary/50 bg-primary/10 px-3 py-1.5 text-sm text-primary transition hover:bg-primary/20"
            >
              Pro Builds
            </Link>
          </div>
        </div>
      </section>

      <section className="grid gap-4 lg:grid-cols-[1.5fr_1fr]">
        <Card className="p-0">
          <div className="flex items-center justify-between border-b border-border/50 px-4 py-3">
            <div>
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Tier List Platinum+</h2>
              <p className="text-xs text-muted">Top performers this patch</p>
            </div>
            <Link href="/tierlist" className="text-xs text-primary hover:underline">
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
                          <Link href={`/champions/${entry.championId}?role=${entry.role}`} className="flex min-w-0 items-center gap-2.5 hover:underline">
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

        <div className="grid gap-4">
          <Card className="p-5">
            <h2 className="font-[var(--font-sora)] text-lg font-semibold">Trending</h2>
            <div className="mt-3 grid gap-2.5">
              {trendingRows.length === 0 ? (
                <p className="text-sm text-muted">No trend data available.</p>
              ) : (
                trendingRows.map((entry) => {
                  const champ = champions[String(entry.championId)];
                  const name = champ?.name ?? `Champion ${entry.championId}`;
                  return (
                    <Link
                      key={`${entry.championId}-trend`}
                      href={`/champions/${entry.championId}?role=${entry.role}`}
                      className="rounded-lg border border-border/60 bg-white/[0.03] p-3 transition hover:bg-white/[0.08]"
                    >
                      <p className="text-sm font-medium text-fg">{name}</p>
                      <p className="mt-1 text-xs text-muted">
                        {roleDisplayLabel(entry.role)} • {formatPercent(entry.winRate, { decimals: 1 })} WR
                      </p>
                    </Link>
                  );
                })
              )}
            </div>
          </Card>

          <Card className="border-primary/45 bg-gradient-to-br from-primary/15 to-primary-2/10 p-5">
            <h2 className="font-[var(--font-sora)] text-lg font-semibold text-primary">Upgrade to Pro</h2>
            <p className="mt-2 text-sm text-fg/85">
              Pro builds and scouting are being prepared. Explore the preview and upcoming feature scope.
            </p>
            <Link
              href="/pro-builds/222"
              className="mt-4 inline-flex rounded-md border border-primary/40 bg-primary/15 px-3 py-1.5 text-sm font-medium text-primary transition hover:bg-primary/25"
            >
              Open Pro Preview
            </Link>
          </Card>
        </div>
      </section>
    </div>
  );
}
