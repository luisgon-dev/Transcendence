import Image from "next/image";
import Link from "next/link";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { newRequestId } from "@/lib/requestId";
import { getSafeRequestContext } from "@/lib/requestContext";
import { formatDateTimeMs, formatDurationSeconds } from "@/lib/format";
import { decodeRiotIdPath, encodeRiotIdPath } from "@/lib/riotid";
import { logEvent } from "@/lib/serverLog";
import { safeDecodeURIComponent, toCodePoints } from "@/lib/textDebug";
import type { SummonerProfileResponse } from "@/components/SummonerProfileClient";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap,
  itemIconUrl,
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
  params: Promise<{ region: string; riotId: string; matchId: string }>;
}) {
  const resolvedParams = await params;
  const verbosity = getErrorVerbosity();
  const ctx = verbosity === "verbose" ? await getSafeRequestContext() : null;
  const pageRequestId =
    verbosity === "verbose"
      ? (ctx?.headers["x-trn-request-id"] ?? newRequestId())
      : null;

  const paramsAny = resolvedParams as unknown as Record<string, unknown>;
  const riotIdRaw = (paramsAny.riotId ?? paramsAny.riotid) as unknown;
  const riotIdPath =
    typeof riotIdRaw === "string" ? riotIdRaw : riotIdRaw == null ? "" : String(riotIdRaw);

  if (verbosity === "verbose") {
    logEvent("info", "summoner match detail page invoked", {
      requestId: pageRequestId,
      route: "summoners/[region]/[riotId]/matches/[matchId]",
      region: resolvedParams.region,
      paramsKeys: Object.keys(paramsAny),
      riotIdRaw: riotIdRaw ?? null,
      riotIdRawString: riotIdPath,
      matchId: resolvedParams.matchId,
      riotIdRawCodePoints: toCodePoints(riotIdRaw),
      ...ctx
    });
  }

  const riotId = decodeRiotIdPath(riotIdPath);
  if (!riotId) {
    if (verbosity === "verbose") {
      const decoded = safeDecodeURIComponent(riotIdRaw);
      const decodedValue = decoded.ok ? decoded.value : null;
      const decodedCodePoints = decodedValue ? toCodePoints(decodedValue) : null;

      logEvent("error", "riotId decode failed", {
        requestId: pageRequestId,
        route: "summoners/[region]/[riotId]/matches/[matchId]",
        region: resolvedParams.region,
        matchId: resolvedParams.matchId,
        paramsKeys: Object.keys(paramsAny),
        riotIdRaw: riotIdRaw ?? null,
        riotIdRawString: riotIdPath,
        riotIdRawCodePoints: toCodePoints(riotIdRaw),
        decoded: decodedValue,
        decodedCodePoints,
        decodeError: decoded.ok ? null : decoded.error,
        asciiDashIndex: decodedValue ? decodedValue.lastIndexOf("-") : null,
        hashIndex: decodedValue ? decodedValue.lastIndexOf("#") : null,
        ...ctx
      });
    }

    return (
      <BackendErrorCard
        title="Match Details"
        message="Invalid summoner URL. Expected /summoners/{region}/{gameName}-{tagLine}."
        requestId={pageRequestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  region: resolvedParams.region,
                  paramsKeys: Object.keys(paramsAny),
                  riotIdRaw: riotIdRaw ?? null,
                  riotIdRawString: riotIdPath,
                  riotIdRawCodePoints: toCodePoints(riotIdRaw),
                  matchId: resolvedParams.matchId
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const profileResult = await fetchBackendJson<unknown>(
    `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
      resolvedParams.region
    )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`,
    { cache: "no-store" }
  );

  const profileBody = profileResult.body as
    | SummonerProfileResponse
    | { message?: string }
    | null;

  if (profileResult.status === 202) {
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
          href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}`}
        >
          Back to profile
        </Link>
      </Card>
    );
  }

  if (!profileResult.ok || !profileBody || typeof profileBody !== "object") {
    return (
      <BackendErrorCard
        title="Match Details"
        message={
          profileResult.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : profileResult.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load summoner profile."
        }
        requestId={profileResult.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  status: profileResult.status,
                  errorKind: profileResult.errorKind
                },
                null,
                2
              )
            : null
        }
      />
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
    fetchBackendJson<MatchDetailDto>(
      `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
        profile.summonerId
      )}/matches/${encodeURIComponent(resolvedParams.matchId)}`,
      { cache: "no-store" }
    )
  ]);

  if (!matchRes.ok) {
    return (
      <BackendErrorCard
        title="Match Details"
        message={
          matchRes.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : matchRes.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load match details."
        }
        requestId={matchRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: matchRes.status, errorKind: matchRes.errorKind }, null, 2)
            : null
        }
      >
        <Link
          className="inline-flex text-sm text-primary hover:underline"
          href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}/matches`}
        >
          Back to match history
        </Link>
      </BackendErrorCard>
    );
  }

  const match = matchRes.body!;
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
              href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}/matches`}
            >
              Back to match history
            </Link>
            <Link
              className="text-sm text-fg/80 hover:text-fg hover:underline"
              href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}`}
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
                            <RuneSetupDisplay
                              primaryStyleId={p.runes.primaryStyleId}
                              subStyleId={p.runes.subStyleId}
                              primarySelections={p.runes.primarySelections ?? []}
                              subSelections={p.runes.subSelections ?? []}
                              statShards={p.runes.statShards ?? []}
                              runeById={runeById}
                              styleById={styleById}
                              iconSize={16}
                            />
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

