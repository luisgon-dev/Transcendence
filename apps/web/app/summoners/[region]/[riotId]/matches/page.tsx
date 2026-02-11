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
import { decodeRiotIdPath, encodeRiotIdPath } from "@/lib/riotid";
import { formatDateTimeMs, formatDurationSeconds } from "@/lib/format";
import { logEvent } from "@/lib/serverLog";
import { safeDecodeURIComponent, toCodePoints } from "@/lib/textDebug";
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
  runesDetail?: {
    primaryStyleId: number;
    subStyleId: number;
    primarySelections: number[];
    subSelections: number[];
    statShards: number[];
  } | null;
};

type PagedResultDto<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

function fmtDate(ms: number) {
  return formatDateTimeMs(ms);
}

export default async function SummonerMatchesPage({
  params,
  searchParams
}: {
  params: Promise<{ region: string; riotId: string }>;
  searchParams?: Promise<{ page?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
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
    logEvent("info", "summoner matches page invoked", {
      requestId: pageRequestId,
      route: "summoners/[region]/[riotId]/matches",
      region: resolvedParams.region,
      paramsKeys: Object.keys(paramsAny),
      riotIdRaw: riotIdRaw ?? null,
      riotIdRawCodePoints: toCodePoints(riotIdRaw),
      riotIdRawString: riotIdPath,
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
        route: "summoners/[region]/[riotId]/matches",
        region: resolvedParams.region,
        paramsKeys: Object.keys(paramsAny),
        riotIdRaw: riotIdRaw ?? null,
        riotIdRawCodePoints: toCodePoints(riotIdRaw),
        riotIdRawString: riotIdPath,
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
        title="Match History"
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
                  riotIdRawCodePoints: toCodePoints(riotIdRaw)
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const page = Math.max(1, Number(resolvedSearchParams?.page ?? "1") || 1);
  const pageSize = 20;

  const profileUrl = `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
    resolvedParams.region
  )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`;

  const profileResult = await fetchBackendJson<unknown>(profileUrl, {
    cache: "no-store"
  });

  const profileBody = profileResult.body as
    | SummonerProfileResponse
    | { message?: string }
    | null;

  if (profileResult.status === 202) {
    return (
      <Card className="p-6">
        <h1 className="font-[var(--font-sora)] text-2xl font-semibold">
          Match History
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          We don&apos;t have match data for this player yet. Start an update on
          the profile page, then come back here once it finishes.
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
    const msg =
      profileResult.errorKind === "timeout"
        ? "Timed out reaching the backend."
        : profileResult.errorKind === "unreachable"
          ? "We are having trouble reaching the backend."
          : "Failed to load summoner profile.";

    return (
      <BackendErrorCard
        title="Match History"
        message={msg}
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
    Promise.all([
      fetchChampionMap(),
      fetchItemMap(),
      fetchSummonerSpellMap(),
      fetchRunesReforged()
    ]),
    fetchBackendJson<PagedResultDto<RecentMatchSummaryDto>>(
      `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
        profile.summonerId
      )}/matches/recent?page=${page}&pageSize=${pageSize}`,
      { cache: "no-store" }
    )
  ]);

  if (!matchesRes.ok) {
    return (
      <BackendErrorCard
        title="Match History"
        message={
          matchesRes.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : matchesRes.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load match history."
        }
        requestId={matchesRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                { status: matchesRes.status, errorKind: matchesRes.errorKind },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const matches = matchesRes.body!;
  const [{ version, champions }, itemStatic, spellStatic, runeStatic] = staticData;

  const itemVersion = itemStatic.version;
  const spellVersion = spellStatic.version;
  const items = itemStatic.items;
  const spells = spellStatic.spells;
  const runeById = runeStatic.runeById;
  const styleById = runeStatic.styleById;

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
            href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}`}
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

          const spell1 = spells[String(m.summonerSpell1Id)];
          const spell2 = spells[String(m.summonerSpell2Id)];
          const keystone = runeById[String(m.runes.keystoneId)];
          const primaryStyle = styleById[String(m.runes.primaryStyleId)];
          const subStyle = styleById[String(m.runes.subStyleId)];

          const matchHref = `/summoners/${resolvedParams.region}/${encodeRiotIdPath(
            riotId
          )}/matches/${encodeURIComponent(m.matchId)}`;

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
                  <span className="text-fg/70">
                    {formatDurationSeconds(m.durationSeconds)}
                  </span>
                </div>
              </div>

              <div className="mt-3 grid gap-2">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex items-center gap-2">
                    {spell1 ? (
                      <Image
                        src={summonerSpellIconUrl(spellVersion, spell1.id)}
                        alt={spell1.name}
                        title={spell1.name}
                        width={22}
                        height={22}
                        className="rounded-md"
                      />
                    ) : (
                      <div className="h-[22px] w-[22px] rounded-md border border-border/60 bg-black/30" />
                    )}
                    {spell2 ? (
                      <Image
                        src={summonerSpellIconUrl(spellVersion, spell2.id)}
                        alt={spell2.name}
                        title={spell2.name}
                        width={22}
                        height={22}
                        className="rounded-md"
                      />
                    ) : (
                      <div className="h-[22px] w-[22px] rounded-md border border-border/60 bg-black/30" />
                    )}

                    <div className="mx-1 h-4 w-px bg-border/60" />

                    {keystone ? (
                      <Image
                        src={runeIconUrl(keystone.icon)}
                        alt={keystone.name}
                        title={keystone.name}
                        width={22}
                        height={22}
                        className="rounded-md bg-black/20 p-0.5"
                      />
                    ) : (
                      <div className="h-[22px] w-[22px] rounded-md border border-border/60 bg-black/30" />
                    )}
                    {primaryStyle ? (
                      <Image
                        src={runeIconUrl(primaryStyle.icon)}
                        alt={primaryStyle.name}
                        title={primaryStyle.name}
                        width={22}
                        height={22}
                        className="rounded-md bg-black/20 p-0.5"
                      />
                    ) : null}
                    {subStyle ? (
                      <Image
                        src={runeIconUrl(subStyle.icon)}
                        alt={subStyle.name}
                        title={subStyle.name}
                        width={22}
                        height={22}
                        className="rounded-md bg-black/20 p-0.5"
                      />
                    ) : null}
                  </div>

                  <Link className="text-xs text-primary hover:underline" href={matchHref}>
                    View details
                  </Link>
                </div>

                <div className="flex flex-wrap gap-1.5">
                  {m.items.map((itemId, idx) => {
                    if (!itemId) {
                      return (
                        <div
                          key={`${m.matchId}-item-${idx}`}
                          className="h-7 w-7 rounded-md border border-border/60 bg-black/25"
                        />
                      );
                    }

                    const meta = items[String(itemId)];
                    const title = meta
                      ? `${meta.name}${meta.plaintext ? ` — ${meta.plaintext}` : ""}`
                      : `Item ${itemId}`;

                    return (
                      <Image
                        key={`${m.matchId}-item-${idx}-${itemId}`}
                        src={itemIconUrl(itemVersion, itemId)}
                        alt={meta?.name ?? `Item ${itemId}`}
                        title={title}
                        width={28}
                        height={28}
                        className="rounded-md"
                      />
                    );
                  })}
                </div>

                {m.runesDetail ? (
                  <details className="rounded-md border border-border/60 bg-black/15 px-2 py-1.5">
                    <summary className="cursor-pointer text-xs text-fg/80">
                      Rune setup
                    </summary>
                    <div className="mt-2">
                      <RuneSetupDisplay
                        primaryStyleId={m.runesDetail.primaryStyleId}
                        subStyleId={m.runesDetail.subStyleId}
                        primarySelections={m.runesDetail.primarySelections ?? []}
                        subSelections={m.runesDetail.subSelections ?? []}
                        statShards={m.runesDetail.statShards ?? []}
                        runeById={runeById}
                        styleById={styleById}
                        iconSize={18}
                      />
                    </div>
                  </details>
                ) : null}
              </div>
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
          href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}/matches?page=${prevPage}`}
        >
          Previous
        </Link>
        <Link
          className={`rounded-md border px-3 py-2 text-sm ${
            matches.page >= matches.totalPages
              ? "pointer-events-none border-border/50 bg-white/5 text-muted"
              : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
          }`}
          href={`/summoners/${resolvedParams.region}/${encodeRiotIdPath(riotId)}/matches?page=${nextPage}`}
        >
          Next
        </Link>
      </div>
    </div>
  );
}

