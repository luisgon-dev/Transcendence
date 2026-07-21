import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { LeaderboardFilters } from "@/components/LeaderboardFilters";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Toolbar } from "@/components/ui/Toolbar";
import { UpdatedAgo } from "@/components/UpdatedAgo";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { formatPercent } from "@/lib/format";
import {
  leaderboardSearchParams,
  normalizeLeaderboardFilters,
  type LeaderboardResponse
} from "@/lib/leaderboards";
import { normalizeLolRegionSlug, platformRegionToSlug } from "@/lib/lolRegions";
import { encodeRiotIdPath } from "@/lib/riotid";
import { roleDisplayLabel } from "@/lib/roles";
import { socialImageUrl } from "@/lib/seo";
import { fetchChampionMap, profileIconUrl } from "@/lib/staticData";

const title = "League of Legends Leaderboards";
const description = "Browse tracked ranked players by region, queue, champion, and role.";
const image = socialImageUrl(title, "Regional rankings", "Ranked ladders and champion specialists");

export const metadata: Metadata = {
  title,
  description,
  alternates: { canonical: "/lol/leaderboards" },
  openGraph: {
    type: "website",
    title,
    description,
    url: "/lol/leaderboards",
    images: [{ url: image, width: 1200, height: 630, alt: title }]
  },
  twitter: { card: "summary_large_image", title, description, images: [image] }
};

function rankLabel(tier: string | null, division: string | null, leaguePoints: number | null) {
  if (!tier) return "Unranked";
  const divisionLabel = ["MASTER", "GRANDMASTER", "CHALLENGER"].includes(tier) ? "" : ` ${division ?? ""}`;
  return `${tier.charAt(0)}${tier.slice(1).toLowerCase()}${divisionLabel} · ${leaguePoints ?? 0} LP`;
}

export default async function LeaderboardsPage({
  searchParams
}: {
  searchParams?: Promise<{ region?: string; queue?: string; championId?: string; role?: string }>;
}) {
  const rawFilters = searchParams ? await searchParams : {};
  const filters = normalizeLeaderboardFilters(rawFilters);
  filters.region = normalizeLolRegionSlug(filters.region);
  const query = leaderboardSearchParams(filters);

  const [{ version, champions }, response] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<LeaderboardResponse>(`${getBackendBaseUrl()}/api/lol/leaderboards?${query.toString()}`, {
      next: { revalidate: 60 }
    })
  ]);
  const activeChampion = filters.championId ? champions[String(filters.championId)] : null;
  const boardLabel = activeChampion
    ? `${activeChampion.name}${filters.role ? ` · ${roleDisplayLabel(filters.role)}` : ""}`
    : filters.queue === "flex" ? "Regional Flex Ladder" : "Regional Solo/Duo Ladder";

  const filterBar = <LeaderboardFilters filters={filters} champions={champions} />;
  if (!response.ok || !response.body) {
    return (
      <div className="grid gap-4">
        <Toolbar eyebrow="Ranked Competition" title="Leaderboards" filters={filterBar} />
        <BackendErrorCard
          title="Leaderboard unavailable"
          message="We couldn't load this ranked board right now."
          hint="Try another region or queue, or check back in a moment."
          requestId={response.requestId}
        />
      </div>
    );
  }

  const board = response.body;
  const profileRegion = platformRegionToSlug(board.region);

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="Ranked Competition"
        title="Leaderboards"
        meta={
          <>
            <Badge className="border-primary/40 bg-primary/10 text-primary">{boardLabel}</Badge>
            <span>{board.entries.length} tracked players</span>
            <span aria-hidden="true">·</span>
            <UpdatedAgo timestamp={board.generatedAtUtc} />
          </>
        }
        filters={filterBar}
      />

      <Card className="overflow-hidden p-0">
        {board.entries.length === 0 ? (
          <div className="grid place-items-center gap-2 px-6 py-16 text-center">
            <p className="type-section">No qualifying players yet</p>
            <p className="type-ui max-w-lg text-muted">
              This board fills as ranked matches are ingested. Try a broader champion role or another region.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-left text-sm">
              <thead className="type-overline text-muted">
                <tr className="border-b border-border/50 bg-surface-2/35">
                  <th className="w-16 px-4 py-3 text-right">Rank</th>
                  <th className="px-4 py-3">Player</th>
                  <th className="px-4 py-3">Ladder</th>
                  <th className="px-4 py-3 text-right">Record</th>
                  {filters.championId ? <th className="px-4 py-3 text-right">Champion games</th> : null}
                  {filters.championId ? <th className="px-4 py-3 text-right">Win rate</th> : null}
                  {filters.championId ? <th className="px-4 py-3 text-right">KDA</th> : null}
                </tr>
              </thead>
              <tbody>
                {board.entries.map((entry) => {
                  const profileHref = `/lol/summoners/${profileRegion}/${encodeRiotIdPath(entry)}`;
                  return (
                    <tr key={entry.summonerId} className="border-b border-border/25 last:border-b-0 hover:bg-surface-2/35">
                      <td className="type-tabular px-4 py-3 text-right font-semibold text-fg/65">{entry.position}</td>
                      <td className="px-4 py-3">
                        <Link href={profileHref} className="group flex min-w-0 items-center gap-3 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35">
                          <Image
                            src={profileIconUrl(version, entry.profileIconId)}
                            alt=""
                            width={36}
                            height={36}
                            sizes="36px"
                            className="rounded-lg bg-surface-2"
                          />
                          <span className="min-w-0">
                            <span className="block truncate font-semibold text-fg group-hover:text-primary">{entry.gameName}</span>
                            <span className="block truncate text-xs text-muted">#{entry.tagLine}</span>
                          </span>
                        </Link>
                      </td>
                      <td className="type-tabular px-4 py-3 text-fg/82">{rankLabel(entry.tier, entry.division, entry.leaguePoints)}</td>
                      <td className="type-tabular px-4 py-3 text-right tabular-nums">
                        <span className="text-success">{entry.rankedWins}W</span>{" "}
                        <span className="text-fg/58">{entry.rankedLosses}L</span>
                      </td>
                      {filters.championId ? <td className="type-tabular px-4 py-3 text-right tabular-nums">{entry.championGames ?? 0}</td> : null}
                      {filters.championId ? <td className="type-tabular px-4 py-3 text-right tabular-nums">{formatPercent(entry.championWinRate ?? 0, { decimals: 1 })}</td> : null}
                      {filters.championId ? <td className="type-tabular px-4 py-3 text-right tabular-nums">{(entry.championKda ?? 0).toFixed(2)}</td> : null}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
