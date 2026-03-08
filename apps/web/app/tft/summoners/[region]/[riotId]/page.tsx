import { Card } from "@/components/ui/Card";
import { TftSummonerProfileClient } from "@/components/TftSummonerProfileClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { decodeRiotIdPath } from "@/lib/riotid";
import { type TftAcceptedResponse, type TftSummonerProfile } from "@/lib/tft";

export default async function TftSummonerPage({
  params
}: {
  params: Promise<{ region: string; riotId: string }>;
}) {
  const { region, riotId } = await params;
  const decoded = decodeRiotIdPath(riotId);

  if (!decoded) {
    return (
      <Card className="p-6">
        <p className="text-lg font-semibold text-fg">Invalid TFT summoner URL.</p>
        <p className="mt-2 text-sm text-fg/75">
          {"Expected /tft/summoners/{region}/{gameName}-{tagLine}."}
        </p>
      </Card>
    );
  }

  const result = await fetchBackendJson<TftSummonerProfile | TftAcceptedResponse>(
    `${getBackendBaseUrl()}/api/tft/summoners/${encodeURIComponent(region)}/${encodeURIComponent(decoded.gameName)}/${encodeURIComponent(decoded.tagLine)}`,
    { cache: "no-store" }
  );

  const initialPayload =
    result.status === 202
      ? {
          kind: "accepted" as const,
          accepted: (result.body as TftAcceptedResponse | null) ?? { message: "TFT profile not found in store yet." }
        }
      : {
          kind: "profile" as const,
          profile: result.body as TftSummonerProfile
        };

  return (
    <TftSummonerProfileClient
      region={region}
      gameName={decoded.gameName}
      tagLine={decoded.tagLine}
      initialPayload={initialPayload}
    />
  );
}
