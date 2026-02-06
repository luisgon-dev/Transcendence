import { notFound } from "next/navigation";

import { SummonerProfileClient } from "@/components/SummonerProfileClient";
import { getBackendBaseUrl } from "@/lib/env";
import { decodeRiotIdPath } from "@/lib/riotid";

export default async function SummonerProfilePage({
  params
}: {
  params: { region: string; riotId: string };
}) {
  const riotId = decodeRiotIdPath(params.riotId);
  if (!riotId) notFound();

  const url = `${getBackendBaseUrl()}/api/summoners/${encodeURIComponent(
    params.region
  )}/${encodeURIComponent(riotId.gameName)}/${encodeURIComponent(riotId.tagLine)}`;

  const res = await fetch(url, { cache: "no-store" });
  const body = await res.json().catch(() => null);

  return (
    <SummonerProfileClient
      region={params.region}
      gameName={riotId.gameName}
      tagLine={riotId.tagLine}
      initialStatus={res.status}
      initialBody={body}
    />
  );
}

