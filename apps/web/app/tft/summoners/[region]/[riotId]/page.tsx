import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Card } from "@/components/ui/Card";
import { TftSummonerProfileClient } from "@/components/TftSummonerProfileClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { decodeRiotIdPath } from "@/lib/riotid";
import { type TftAcceptedResponse, type TftSummonerProfile } from "@/lib/tft";

export default async function TftSummonerPage({
  params,
  searchParams
}: {
  params: Promise<{ region: string; riotId: string }>;
  searchParams?: Promise<{ page?: string; sort?: string }>;
}) {
  const { region, riotId } = await params;
  const resolved = searchParams ? await searchParams : undefined;
  const decoded = decodeRiotIdPath(riotId);

  if (!decoded) {
    return (
      <Card className="p-6">
        <p className="text-lg font-semibold text-fg">Invalid TFT player link.</p>
        <p className="mt-2 text-sm text-fg/75">
          {"Use /tft/summoners/{region}/{gameName}-{tagLine}."}
        </p>
      </Card>
    );
  }

  const result = await fetchBackendJson<TftSummonerProfile | TftAcceptedResponse>(
    `${getBackendBaseUrl()}/api/tft/summoners/${encodeURIComponent(region)}/${encodeURIComponent(decoded.gameName)}/${encodeURIComponent(decoded.tagLine)}`,
    { cache: "no-store" }
  );

  // fetchBackendJson reports ok=true for 202 (it's < 400); a non-ok result here is a
  // genuine backend error (timeout/unreachable/5xx), so fail closed instead of casting
  // a null/error body into the profile client.
  if (!result.ok) {
    const verbosity = getErrorVerbosity();
    return (
      <BackendErrorCard
        title={`${decoded.gameName}#${decoded.tagLine}`}
        message={
          result.errorKind === "timeout"
            ? "This profile is taking too long to load."
            : result.errorKind === "unreachable"
              ? "We couldn't reach the TFT service right now."
              : "We couldn't load this TFT profile."
        }
        hint="Try again in a moment."
        requestId={result.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: result.status, errorKind: result.errorKind }, null, 2)
            : null
        }
      />
    );
  }

  const initialPayload =
    result.status === 202
      ? {
          kind: "accepted" as const,
          accepted: (result.body as TftAcceptedResponse | null) ?? { message: "We are pulling this player's latest TFT matches now." }
        }
      : {
          kind: "profile" as const,
          profile: result.body as TftSummonerProfile
        };

  const initialPage = Math.max(1, Number(resolved?.page) || 1);
  const initialSort = resolved?.sort ?? "DATE_DESC";

  return (
    <TftSummonerProfileClient
      region={region}
      gameName={decoded.gameName}
      tagLine={decoded.tagLine}
      initialPayload={initialPayload}
      initialPage={initialPage}
      initialSort={initialSort}
    />
  );
}
