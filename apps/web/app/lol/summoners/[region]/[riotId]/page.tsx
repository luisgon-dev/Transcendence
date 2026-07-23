import type { Metadata } from "next";
import { Suspense } from "react";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { SummonerProfileClient } from "@/components/SummonerProfileClient";
import type {
  ApiErrorResponse,
  MatchSummary,
  PagedResultDto,
  RankHistoryEntry,
  SummonerLookupResponse
} from "@/components/lol-profile/shared";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { newRequestId } from "@/lib/requestId";
import { getSafeRequestContext } from "@/lib/requestContext";
import { decodeRiotIdPath, encodeRiotIdPath } from "@/lib/riotid";
import { socialImageUrl } from "@/lib/seo";
import { logEvent } from "@/lib/serverLog";
import {
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap
} from "@/lib/staticData";
import { safeDecodeURIComponent, toCodePoints } from "@/lib/textDebug";

import Loading from "./loading";

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

function isSummonerLookupResponse(value: unknown): value is SummonerLookupResponse {
  if (!isRecord(value)) return false;
  if (value.status === "ready") return isRecord(value.profile);
  return value.status === "refreshing" || value.status === "missing";
}

export async function generateMetadata({
  params
}: {
  params: Promise<{ region: string; riotId: string }>;
}): Promise<Metadata> {
  const { region, riotId: riotIdPath } = await params;
  const riotId = decodeRiotIdPath(riotIdPath);
  if (!riotId) {
    return {
      title: "Summoner profile",
      robots: { index: false, follow: false }
    };
  }

  const displayName = `${riotId.gameName}#${riotId.tagLine}`;
  const regionLabel = region.toUpperCase();
  const title = `${displayName} Rank and Match History`;
  const description = `View ${displayName}'s ${regionLabel} rank, match history, champion pool, performance, and recent form.`;
  const canonical = `/lol/summoners/${encodeURIComponent(region.toLowerCase())}/${encodeRiotIdPath(riotId)}`;
  const image = socialImageUrl(title, `${regionLabel} summoner profile`, "Rank, recent form, champion pool, and match history");

  return {
    title,
    description,
    alternates: { canonical },
    openGraph: {
      type: "profile",
      title,
      description,
      url: canonical,
      images: [{ url: image, width: 1200, height: 630, alt: `${displayName} profile` }]
    },
    twitter: { card: "summary_large_image", title, description, images: [image] }
  };
}

export default async function SummonerProfilePage({
  params,
  searchParams
}: {
  params: Promise<{ region: string; riotId: string }>;
  searchParams?: Promise<{
    page?: string;
    queue?: string;
    sort?: string;
    champion?: string;
    expandMatchId?: string;
  }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const initialPage = Math.max(1, Number(resolvedSearchParams?.page ?? "1") || 1);

  // Some environments appear to provide the dynamic param key with different casing.
  const paramsAny = resolvedParams as unknown as Record<string, unknown>;
  const riotIdRaw = (paramsAny.riotId ?? paramsAny.riotid) as unknown;
  const riotIdPath =
    typeof riotIdRaw === "string" ? riotIdRaw : riotIdRaw == null ? "" : String(riotIdRaw);
  const riotId = decodeRiotIdPath(riotIdPath);

  if (!riotId) {
    const verbosity = getErrorVerbosity();
    const ctx = verbosity === "verbose" ? await getSafeRequestContext() : null;
    const pageRequestId =
      verbosity === "verbose" ? (ctx?.headers["x-trn-request-id"] ?? newRequestId()) : null;

    if (verbosity === "verbose") {
      const decoded = safeDecodeURIComponent(riotIdRaw);
      const decodedValue = decoded.ok ? decoded.value : null;
      logEvent("error", "riotId decode failed", {
        requestId: pageRequestId,
        route: "summoners/[region]/[riotId]",
        region: resolvedParams.region,
        paramsKeys: Object.keys(paramsAny),
        riotIdRaw: riotIdRaw ?? null,
        riotIdRawCodePoints: toCodePoints(riotIdRaw),
        riotIdRawString: riotIdPath,
        decoded: decodedValue,
        decodedCodePoints: decodedValue ? toCodePoints(decodedValue) : null,
        decodeError: decoded.ok ? null : decoded.error,
        asciiDashIndex: decodedValue ? decodedValue.lastIndexOf("-") : null,
        hashIndex: decodedValue ? decodedValue.lastIndexOf("#") : null,
        ...ctx
      });
    }

    return (
      <BackendErrorCard
        title="Summoner"
        message="This player link isn't valid. Search for the player by name from the home page instead."
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

  return (
    <Suspense fallback={<Loading />}>
      <SummonerProfileData
        region={resolvedParams.region}
        riotIdPath={riotIdPath}
        riotId={riotId}
        paramsKeys={Object.keys(paramsAny)}
        riotIdRaw={riotIdRaw}
        initialPage={initialPage}
        initialQueue={resolvedSearchParams?.queue ?? "ALL"}
        initialSort={resolvedSearchParams?.sort ?? "DATE_DESC"}
        initialChampion={resolvedSearchParams?.champion ?? ""}
        initialExpandMatchId={resolvedSearchParams?.expandMatchId ?? null}
      />
    </Suspense>
  );
}

async function SummonerProfileData({
  region,
  riotIdPath,
  riotId,
  paramsKeys,
  riotIdRaw,
  initialPage,
  initialQueue,
  initialSort,
  initialChampion,
  initialExpandMatchId
}: {
  region: string;
  riotIdPath: string;
  riotId: { gameName: string; tagLine: string };
  paramsKeys: string[];
  riotIdRaw: unknown;
  initialPage: number;
  initialQueue: string;
  initialSort: string;
  initialChampion: string;
  initialExpandMatchId: string | null;
}) {
  const verbosity = getErrorVerbosity();
  const ctx = verbosity === "verbose" ? await getSafeRequestContext() : null;
  const pageRequestId =
    verbosity === "verbose"
      ? (ctx?.headers["x-trn-request-id"] ?? newRequestId())
      : null;

  if (verbosity === "verbose") {
    logEvent("info", "summoner page invoked", {
      requestId: pageRequestId,
      route: "summoners/[region]/[riotId]",
      region,
      paramsKeys,
      riotIdRaw: riotIdRaw ?? null,
      riotIdRawCodePoints: toCodePoints(riotIdRaw),
      riotIdRawString: riotIdPath,
      ...ctx
    });
  }

  const url = `${getBackendBaseUrl()}/api/lol/summoners/${encodeURIComponent(
    region
  )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`;

  const staticDataPromise = Promise.allSettled([
    fetchChampionMap(),
    fetchItemMap(),
    fetchSummonerSpellMap(),
    fetchRunesReforged()
  ]);
  const result = await fetchBackendJson<SummonerLookupResponse>(url, { cache: "no-store" });

  let initialLookup: SummonerLookupResponse | null = null;
  let initialError: ApiErrorResponse | null = null;

  if (!result.ok) {
    const errorBody: unknown = result.body;
    // Backend errors are ProblemDetails (RFC 7807): `detail` is the specific message, `title` the
    // generic one. Some admin-style bodies use `message`. Prefer the most specific available.
    const messageFromBackend = isRecord(errorBody)
      ? (typeof errorBody.detail === "string"
          ? errorBody.detail
          : typeof errorBody.message === "string"
            ? errorBody.message
            : typeof errorBody.title === "string"
              ? errorBody.title
              : null)
      : null;

    const message =
      messageFromBackend ??
      (result.errorKind === "timeout"
        ? "This page is taking too long to load."
        : result.errorKind === "unreachable"
          ? "We couldn't load this player right now."
          : "We couldn't load this player.");

    logEvent("warn", "summoner profile fetch failed", {
      requestId: result.requestId,
      status: result.status,
      errorKind: result.errorKind
    });

    initialError = {
      message,
      requestId: result.requestId,
      ...(verbosity === "verbose"
        ? {
            detail: JSON.stringify(
              { status: result.status, errorKind: result.errorKind },
              null,
              2
            )
          }
        : null)
    };
  } else if (isSummonerLookupResponse(result.body)) {
    initialLookup = result.body;
  } else {
    initialError = {
      message: "The player lookup returned an invalid response.",
      code: "INVALID_RESPONSE",
      requestId: result.requestId
    };
  }

  let initialHistory: PagedResultDto<MatchSummary> | null = null;
  let initialRankHistory: RankHistoryEntry[] | null = null;

  const initialProfile = initialLookup?.status === "ready" ? initialLookup.profile : null;
  if (initialProfile?.summonerId) {
    const summonerId = encodeURIComponent(initialProfile.summonerId);
    const baseUrl = `${getBackendBaseUrl()}/api/lol/summoners/${summonerId}`;
    const [historyResult, rankHistoryResult] = await Promise.all([
      fetchBackendJson<PagedResultDto<MatchSummary>>(
        `${baseUrl}/matches/recent?page=${initialPage}&pageSize=20`,
        { cache: "no-store" }
      ),
      fetchBackendJson<RankHistoryEntry[]>(`${baseUrl}/stats/rank-history`, {
        cache: "no-store"
      })
    ]);

    if (historyResult.ok) initialHistory = historyResult.body;
    if (rankHistoryResult.ok && Array.isArray(rankHistoryResult.body)) {
      initialRankHistory = rankHistoryResult.body;
    }
  }

  const [championResult, itemResult, spellResult, runeResult] = await staticDataPromise;

  return (
    <SummonerProfileClient
      region={region}
      gameName={riotId.gameName}
      tagLine={riotId.tagLine}
      initialLookup={initialLookup}
      initialError={initialError}
      initialPage={initialPage}
      initialQueue={initialQueue}
      initialSort={initialSort}
      initialChampion={initialChampion}
      initialExpandMatchId={initialExpandMatchId}
      initialHistory={initialHistory}
      initialRankHistory={initialRankHistory}
      initialChampionStatic={championResult.status === "fulfilled" ? championResult.value : null}
      initialItemStatic={itemResult.status === "fulfilled" ? itemResult.value : null}
      initialSpellStatic={spellResult.status === "fulfilled" ? spellResult.value : null}
      initialRuneStatic={runeResult.status === "fulfilled" ? runeResult.value : null}
    />
  );
}
