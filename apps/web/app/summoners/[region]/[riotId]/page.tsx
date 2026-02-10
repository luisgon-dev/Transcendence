import { BackendErrorCard } from "@/components/BackendErrorCard";
import { SummonerProfileClient } from "@/components/SummonerProfileClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { decodeRiotIdPath } from "@/lib/riotid";
import { logEvent } from "@/lib/serverLog";

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

export default async function SummonerProfilePage({
  params
}: {
  params: { region: string; riotId: string };
}) {
  const riotId = decodeRiotIdPath(params.riotId);
  if (!riotId) {
    return (
      <BackendErrorCard
        title="Summoner"
        message="Invalid summoner URL. Expected /summoners/{region}/{gameName}-{tagLine}."
      />
    );
  }

  const url = `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
    params.region
  )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`;

  const result = await fetchBackendJson<unknown>(url, { cache: "no-store" });
  const verbosity = getErrorVerbosity();

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
      region={params.region}
      gameName={riotId.gameName}
      tagLine={riotId.tagLine}
      initialStatus={initialStatus}
      initialBody={initialBody}
    />
  );
}

