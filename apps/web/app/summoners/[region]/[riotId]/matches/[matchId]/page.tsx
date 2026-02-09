import Image from "next/image";
import Link from "next/link";
import { notFound } from "next/navigation";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { getBackendBaseUrl } from "@/lib/env";
import { formatDateTimeMs, formatDurationSeconds } from "@/lib/format";
import { decodeRiotIdPath, encodeRiotIdPath } from "@/lib/riotid";
import type { SummonerProfileResponse } from "@/components/SummonerProfileClient";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap,
  itemIconUrl,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";

type MatchDetailDto = {
  matchId: string;
  matchDate: number;
  duration: number;
  queueType: string;
  patch?: string | null;
  participants: ParticipantDetailDto[];
};

type ParticipantDetailDto = {
  puuid?: string | null;
  gameName?: string | null;
  tagLine?: string | null;
  teamId: number;
  championId: number;
  teamPosition?: string | null;
  win: boolean;
  kills: number;
  deaths: number;
  assists: number;
  champLevel: number;
  goldEarned: number;
  totalDamageDealtToChampions: number;
  visionScore: number;
  totalMinionsKilled: number;
  neutralMinionsKilled: number;
  summonerSpell1Id: number;
  summonerSpell2Id: number;
  items: number[];
  runes: {
    primaryStyleId: number;
    subStyleId: number;
    primarySelections: number[];
    subSelections: number[];
    statShards: number[];
  };
};

function cs(p: ParticipantDetailDto) {
  return (p.totalMinionsKilled ?? 0) + (p.neutralMinionsKilled ?? 0);
}

function fmtKda(p: ParticipantDetailDto) {
  return `${p.kills}/${p.deaths}/${p.assists}`;
}

export default async function MatchDetailPage({
  params
}: {
  params: { region: string; riotId: string; matchId: string };
}) {
  const riotId = decodeRiotIdPath(params.riotId);
  if (!riotId) notFound();

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
          Match Details
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          This player&apos;s data is still updating. Go back to the profile page
          to start an update.
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
          Match Details
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
          Match Details
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          This backend response is missing <code>summonerId</code>.
        </p>
      </Card>
    );
  }

  const [staticData, matchRes] = await Promise.all([
    Promise.all([
      fetchChampionMap(),
      fetchItemMap(),
      fetchSummonerSpellMap(),
      fetchRunesReforged()
    ]),
    fetch(
      `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
        profile.summonerId
      )}/matches/${encodeURIComponent(params.matchId)}`,
      { cache: "no-store" }
    )
  ]);

  if (!matchRes.ok) {
    return (
      <Card className="p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Match Details
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          Failed to load match details.
        </p>
        <Link
          className="mt-4 inline-flex text-sm text-primary hover:underline"
          href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}/matches`}
        >
          Back to match history
        </Link>
      </Card>
    );
  }

  const match = (await matchRes.json()) as MatchDetailDto;
  const [{ version, champions }, itemStatic, spellStatic, runeStatic] = staticData;
  const itemVersion = itemStatic.version;
  const spellVersion = spellStatic.version;
  const items = itemStatic.items;
  const spells = spellStatic.spells;
  const runeById = runeStatic.runeById;
  const styleById = runeStatic.styleById;

  const targetKey = `${riotId.gameName.toLowerCase()}#${riotId.tagLine.toLowerCase()}`;

  const teams = [100, 200].map((teamId) => ({
    teamId,
    participants: match.participants
      .filter((p) => p.teamId === teamId)
      .sort((a, b) => (a.teamPosition ?? "").localeCompare(b.teamPosition ?? ""))
  }));

  return (
    <div className="grid gap-6">
      <header className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <Badge>
            {profile.gameName}#{profile.tagLine}
          </Badge>
          <Badge>{match.queueType}</Badge>
          <Badge>{formatDurationSeconds(match.duration)}</Badge>
          <Badge>{formatDateTimeMs(match.matchDate)}</Badge>
          {match.patch ? <Badge>Patch {match.patch}</Badge> : null}
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3">
          <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
            Match Details
          </h1>
          <div className="flex items-center gap-4">
            <Link
              className="text-sm text-primary hover:underline"
              href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}/matches`}
            >
              Back to match history
            </Link>
            <Link
              className="text-sm text-fg/80 hover:text-fg hover:underline"
              href={`/summoners/${params.region}/${encodeRiotIdPath(riotId)}`}
            >
              Profile
            </Link>
          </div>
        </div>
      </header>

      <div className="grid gap-6">
        {teams.map((t) => {
          const win = t.participants.some((p) => p.win);
          return (
            <Card
              key={t.teamId}
              className={`p-5 ${
                win ? "border-emerald-400/20" : "border-red-400/20"
              }`}
            >
              <div className="flex items-center justify-between gap-3">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                  Team {t.teamId}{" "}
                  <span className="text-sm text-muted">
                    ({win ? "Win" : "Loss"})
                  </span>
                </h2>
              </div>

              <div className="mt-4 overflow-x-auto">
                <table className="w-full min-w-[900px] text-left text-sm">
                  <thead className="text-xs text-muted">
                    <tr>
                      <th className="py-2 pr-4">Player</th>
                      <th className="py-2 pr-4">Role</th>
                      <th className="py-2 pr-4">KDA</th>
                      <th className="py-2 pr-4">CS</th>
                      <th className="py-2 pr-4">Gold</th>
                      <th className="py-2 pr-4">Damage</th>
                      <th className="py-2 pr-4">Vision</th>
                      <th className="py-2 pr-4">Spells</th>
                      <th className="py-2 pr-4">Runes</th>
                      <th className="py-2 pr-4">Items</th>
                    </tr>
                  </thead>
                  <tbody>
                    {t.participants.map((p, idx) => {
                      const champ = champions[String(p.championId)];
                      const champName = champ?.name ?? `Champion ${p.championId}`;
                      const champSlug = champ?.id ?? "Unknown";

                      const pKey = `${(p.gameName ?? "").toLowerCase()}#${(
                        p.tagLine ?? ""
                      ).toLowerCase()}`;
                      const isTarget = pKey === targetKey;

                      const spell1 = spells[String(p.summonerSpell1Id)];
                      const spell2 = spells[String(p.summonerSpell2Id)];

                      const keystone = runeById[String(p.runes.primarySelections?.[0] ?? 0)];
                      const primaryStyle = styleById[String(p.runes.primaryStyleId)];
                      const subStyle = styleById[String(p.runes.subStyleId)];

                      return (
                        <tr
                          key={`${t.teamId}-${p.championId}-${idx}`}
                          className={`border-t border-border/50 ${
                            isTarget ? "bg-white/5" : ""
                          }`}
                        >
                          <td className="py-2 pr-4">
                            <div className="flex items-center gap-3">
                              <Image
                                src={championIconUrl(version, champSlug)}
                                alt={champName}
                                width={28}
                                height={28}
                                className="rounded-md"
                              />
                              <div className="min-w-0">
                                <div className="truncate font-medium">
                                  {p.gameName ?? "Unknown"}
                                  {p.tagLine ? (
                                    <span className="text-muted">
                                      #{p.tagLine}
                                    </span>
                                  ) : null}
                                </div>
                                <div className="text-xs text-muted">
                                  Lvl {p.champLevel}
                                </div>
                              </div>
                            </div>
                          </td>
                          <td className="py-2 pr-4 text-xs text-fg/80">
                            {p.teamPosition ?? "-"}
                          </td>
                          <td className="py-2 pr-4 font-medium">
                            {fmtKda(p)}
                          </td>
                          <td className="py-2 pr-4">{cs(p)}</td>
                          <td className="py-2 pr-4">
                            {p.goldEarned.toLocaleString()}
                          </td>
                          <td className="py-2 pr-4">
                            {p.totalDamageDealtToChampions.toLocaleString()}
                          </td>
                          <td className="py-2 pr-4">{p.visionScore}</td>
                          <td className="py-2 pr-4">
                            <div className="flex items-center gap-1.5">
                              {spell1 ? (
                                <Image
                                  src={summonerSpellIconUrl(spellVersion, spell1.id)}
                                  alt={spell1.name}
                                  title={spell1.name}
                                  width={18}
                                  height={18}
                                  className="rounded"
                                />
                              ) : null}
                              {spell2 ? (
                                <Image
                                  src={summonerSpellIconUrl(spellVersion, spell2.id)}
                                  alt={spell2.name}
                                  title={spell2.name}
                                  width={18}
                                  height={18}
                                  className="rounded"
                                />
                              ) : null}
                            </div>
                          </td>
                          <td className="py-2 pr-4">
                            <div className="flex items-center gap-1.5">
                              {keystone ? (
                                <Image
                                  src={runeIconUrl(keystone.icon)}
                                  alt={keystone.name}
                                  title={keystone.name}
                                  width={18}
                                  height={18}
                                  className="rounded bg-black/20 p-0.5"
                                />
                              ) : null}
                              {primaryStyle ? (
                                <Image
                                  src={runeIconUrl(primaryStyle.icon)}
                                  alt={primaryStyle.name}
                                  title={primaryStyle.name}
                                  width={18}
                                  height={18}
                                  className="rounded bg-black/20 p-0.5"
                                />
                              ) : null}
                              {subStyle ? (
                                <Image
                                  src={runeIconUrl(subStyle.icon)}
                                  alt={subStyle.name}
                                  title={subStyle.name}
                                  width={18}
                                  height={18}
                                  className="rounded bg-black/20 p-0.5"
                                />
                              ) : null}
                            </div>
                          </td>
                          <td className="py-2 pr-4">
                            <div className="flex items-center gap-1.5">
                              {p.items.map((itemId, itemIdx) => {
                                if (!itemId) {
                                  return (
                                    <div
                                      key={`${t.teamId}-${idx}-item-${itemIdx}`}
                                      className="h-[18px] w-[18px] rounded border border-border/60 bg-black/20"
                                    />
                                  );
                                }
                                const meta = items[String(itemId)];
                                const title = meta
                                  ? `${meta.name}${meta.plaintext ? ` — ${meta.plaintext}` : ""}`
                                  : `Item ${itemId}`;
                                return (
                                  <Image
                                    key={`${t.teamId}-${idx}-${itemIdx}-${itemId}`}
                                    src={itemIconUrl(itemVersion, itemId)}
                                    alt={meta?.name ?? `Item ${itemId}`}
                                    title={title}
                                    width={18}
                                    height={18}
                                    className="rounded"
                                  />
                                );
                              })}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </Card>
          );
        })}
      </div>
    </div>
  );
}

