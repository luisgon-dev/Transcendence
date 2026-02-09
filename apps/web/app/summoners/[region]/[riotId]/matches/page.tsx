import Image from "next/image";
import Link from "next/link";
import { notFound } from "next/navigation";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { getBackendBaseUrl } from "@/lib/env";
import { decodeRiotIdPath, encodeRiotIdPath } from "@/lib/riotid";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import type { SummonerProfileResponse } from "@/components/SummonerProfileClient";

type RecentMatchSummaryDto = {
  matchId: string;
  matchDate: number;
  durationSeconds: number;
  queueType: string;
  win: boolean;
  championId: number;
  teamPosition?: string | null;
  kills: number;
  deaths: number;
  assists: number;
  visionScore: number;
  damageToChamps: number;
  csPerMin: number;
  summonerSpell1Id: number;
  summonerSpell2Id: number;
  items: number[];
  runes: { primaryStyleId: number; subStyleId: number; keystoneId: number };
};

type PagedResultDto<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

function fmtDate(ms: number) {
  try {
    return new Date(ms).toLocaleString();
  } catch {
    return String(ms);
  }
}

export default async function SummonerMatchesPage({
  params,
  searchParams
}: {
  params: { region: string; riotId: string };
  searchParams?: { page?: string };
}) {
  const riotId = decodeRiotIdPath(params.riotId);
  if (!riotId) notFound();

  const page = Math.max(1, Number(searchParams?.page ?? "1") || 1);
  const pageSize = 20;

  const profileRes = await fetch(
    `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
      params.region
    )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`,
    { cache: "no-store" }
  );
  const profileBody = (await profileRes.json().catch(() => null)) as
    | SummonerProfileResponse
    | { message?: string }
    | null;

  if (profileRes.status === 202) {
    return (
      <Card className="p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Match History
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          This summoner isn&apos;t ready yet. Go to the profile page to queue a
          refresh and poll until data is available.
        </p>
        <Link
          className="mt-4 inline-flex text-sm text-primary hover:underline"
          href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}`}
        >
          Back to profile
        </Link>
      </Card>
    );
  }

  if (!profileRes.ok || !profileBody || typeof profileBody !== "object") {
    return (
      <Card className="p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Match History
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          Failed to load summoner profile.
        </p>
      </Card>
    );
  }

  const profile = profileBody as SummonerProfileResponse;
  if (!profile.summonerId) {
    return (
      <Card className="p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Match History
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          This backend response is missing <code>summonerId</code>, which is
          required for the paged match history endpoints.
        </p>
      </Card>
    );
  }

  const [staticData, matchesRes] = await Promise.all([
    fetchChampionMap(),
    fetch(
      `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
        profile.summonerId
      )}/matches/recent?page=${page}&pageSize=${pageSize}`,
      { cache: "no-store" }
    )
  ]);

  if (!matchesRes.ok) {
    return (
      <Card className="p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Match History
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          Failed to load match history.
        </p>
      </Card>
    );
  }

  const matches = (await matchesRes.json()) as PagedResultDto<RecentMatchSummaryDto>;
  const { version, champions } = staticData;

  const prevPage = Math.max(1, matches.page - 1);
  const nextPage = Math.min(matches.totalPages, matches.page + 1);

  return (
    <div className="grid gap-6">
      <header className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <Badge>{profile.gameName}#{profile.tagLine}</Badge>
          <Badge>
            Page {matches.page} / {matches.totalPages}
          </Badge>
          <Badge>{matches.totalCount.toLocaleString()} total</Badge>
        </div>
        <div className="flex items-center justify-between">
          <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
            Match History
          </h1>
          <Link
            className="text-sm text-primary hover:underline"
            href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}`}
          >
            Back to profile
          </Link>
        </div>
      </header>

      <div className="grid gap-2">
        {matches.items.map((m) => {
          const champ = champions[String(m.championId)];
          const name = champ?.name ?? `Champion ${m.championId}`;
          const champId = champ?.id ?? "Unknown";

          return (
            <Card
              key={m.matchId}
              className={`p-4 ${
                m.win ? "border-emerald-400/30 bg-emerald-500/10" : "border-red-400/30 bg-red-500/10"
              }`}
            >
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-3">
                  <Image
                    src={championIconUrl(version, champId)}
                    alt={name}
                    width={38}
                    height={38}
                    className="rounded-lg"
                  />
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-fg">
                      {name}{" "}
                      <span className="text-xs text-muted">({m.queueType})</span>
                    </p>
                    <p className="text-xs text-muted">{fmtDate(m.matchDate)}</p>
                  </div>
                </div>

                <div className="flex items-center gap-4 text-sm">
                  <span className="text-fg/85">
                    {m.kills}/{m.deaths}/{m.assists}
                  </span>
                  <span className="text-fg/70">{m.csPerMin.toFixed(1)} CS/min</span>
                  <span className="text-fg/70">{m.durationSeconds}s</span>
                </div>
              </div>

              <p className="mt-3 text-xs text-fg/70">
                Items: {m.items.join(", ")} · Runes: {m.runes.keystoneId} · Spells:{" "}
                {m.summonerSpell1Id}, {m.summonerSpell2Id}
              </p>
            </Card>
          );
        })}
      </div>

      <div className="flex items-center justify-between">
        <Link
          className={`rounded-md border px-3 py-2 text-sm ${
            matches.page <= 1
              ? "pointer-events-none border-border/50 bg-white/5 text-muted"
              : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
          }`}
          href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}/matches?page=${prevPage}`}
        >
          Previous
        </Link>
        <Link
          className={`rounded-md border px-3 py-2 text-sm ${
            matches.page >= matches.totalPages
              ? "pointer-events-none border-border/50 bg-white/5 text-muted"
              : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
          }`}
          href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}/matches?page=${nextPage}`}
        >
          Next
        </Link>
      </div>
    </div>
  );
}

