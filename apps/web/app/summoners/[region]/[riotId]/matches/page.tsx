import { redirect } from "next/navigation";

import { decodeRiotIdPath, encodeRiotIdPath } from "@/lib/riotid";

export default async function SummonerMatchesRedirectPage({
  params,
  searchParams
}: {
  params: Promise<{ region: string; riotId: string }>;
  searchParams?: Promise<{ page?: string; queue?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;

  const paramsAny = resolvedParams as unknown as Record<string, unknown>;
  const riotIdRaw = (paramsAny.riotId ?? paramsAny.riotid) as unknown;
  const riotIdPath = typeof riotIdRaw === "string" ? riotIdRaw : "";
  const parsed = decodeRiotIdPath(riotIdPath);
  const canonical = parsed ? encodeRiotIdPath(parsed) : riotIdPath;

  const qs = new URLSearchParams();
  if (resolvedSearchParams?.page) qs.set("page", resolvedSearchParams.page);
  if (resolvedSearchParams?.queue) qs.set("queue", resolvedSearchParams.queue);

  const query = qs.toString();
  redirect(`/summoners/${encodeURIComponent(resolvedParams.region)}/${canonical}${query ? `?${query}` : ""}`);
}
