import { BackendErrorCard } from "@/components/BackendErrorCard";
import { SummonerProfileClient } from "@/components/SummonerProfileClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { newRequestId } from "@/lib/requestId";
import { getSafeRequestContext } from "@/lib/requestContext";
import { decodeRiotIdPath } from "@/lib/riotid";
import { logEvent } from "@/lib/serverLog";
import { safeDecodeURIComponent, toCodePoints } from "@/lib/textDebug";

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

export default async function SummonerProfilePage({
  params,
  searchParams
}: {
  params: Promise<{ region: string; riotId: string }>;
  searchParams?: Promise<{ page?: string; queue?: string; expandMatchId?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const verbosity = getErrorVerbosity();
  const ctx = verbosity === "verbose" ? await getSafeRequestContext() : null;
  const pageRequestId =
    verbosity === "verbose"
      ? (ctx?.headers["x-trn-request-id"] ?? newRequestId())
      : null;

  // Some environments appear to provide the dynamic param key with different casing.
  const paramsAny = resolvedParams as unknown as Record<string, unknown>;
  const riotIdRaw = (paramsAny.riotId ?? paramsAny.riotid) as unknown;
  const riotIdPath =
    typeof riotIdRaw === "string" ? riotIdRaw : riotIdRaw == null ? "" : String(riotIdRaw);

  if (verbosity === "verbose") {
    logEvent("info", "summoner page invoked", {
      requestId: pageRequestId,
      route: "summoners/[region]/[riotId]",
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
        route: "summoners/[region]/[riotId]",
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
        title="Summoner"
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

  const url = `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
    resolvedParams.region
  )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`;

  const result = await fetchBackendJson<unknown>(url, { cache: "no-store" });

  let initialStatus = result.status;
  let initialBody: unknown = result.body;

  if (!result.ok && initialStatus !== 202) {
    const messageFromBackend =
      isRecord(result.body) && typeof result.body.message === "string"
        ? (result.body.message as string)
        : null;

    const message =
      messageFromBackend ??
      (result.errorKind === "timeout"
        ? "Timed out reaching the backend."
        : result.errorKind === "unreachable"
          ? "We are having trouble reaching the backend."
          : "Backend request failed.");

    logEvent("warn", "summoner profile fetch failed", {
      requestId: result.requestId,
      status: result.status,
      errorKind: result.errorKind
    });

    initialBody = {
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
  } else if (initialStatus === 202 && isRecord(initialBody)) {
    initialBody = { ...initialBody, requestId: result.requestId };
  }

  return (
    <SummonerProfileClient
      region={resolvedParams.region}
      gameName={riotId.gameName}
      tagLine={riotId.tagLine}
      initialStatus={initialStatus}
      initialBody={initialBody}
      initialPage={Math.max(1, Number(resolvedSearchParams?.page ?? "1") || 1)}
      initialQueue={resolvedSearchParams?.queue ?? "ALL"}
      initialExpandMatchId={resolvedSearchParams?.expandMatchId ?? null}
    />
  );
}
