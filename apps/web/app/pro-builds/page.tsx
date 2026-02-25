import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function ProBuildsIndexPage() {
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/analytics/tierlist`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  const championsToShow = (tierListRes.ok
    ? normalizeTierListEntries(tierListRes.body?.entries ?? [])
        .slice()
        .sort((a, b) => b.games - a.games)
        .filter(
          (entry, idx, arr) => arr.findIndex((candidate) => candidate.championId === entry.championId) === idx
        )
        .slice(0, 12)
    : []
  ).map((entry) => entry.championId);

  return (
    <div className="grid gap-6">
      <header className="grid gap-2">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Pro Builds
        </h1>
        <p className="text-sm text-fg/75">
          Preview the upcoming pro analytics experience. High-ELO and pro match pipelines are in progress.
        </p>
      </header>

      <Card className="border-primary/40 bg-primary/10 p-5">
        <h2 className="font-[var(--font-sora)] text-lg font-semibold text-primary">Coming Soon</h2>
        <p className="mt-2 text-sm text-fg/85">
          The backend endpoint for pro player match ingestion is not yet available. This page is a public teaser and
          the final data views will be enabled once pro data is integrated.
        </p>
      </Card>

      <Card className="p-5">
        <h2 className="font-[var(--font-sora)] text-lg font-semibold">Open Champion Preview</h2>
        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {championsToShow.map((championId) => {
            const champion = champions[String(championId)];
            const championName = champion?.name ?? `Champion ${championId}`;
            return (
              <Link
                key={championId}
                href={`/pro-builds/${championId}`}
                className="rounded-lg border border-border/60 bg-white/[0.03] p-3 transition hover:bg-white/[0.08]"
              >
                <div className="flex items-center gap-2.5">
                  <Image
                    src={championIconUrl(version, champion?.id ?? "Unknown")}
                    alt={championName}
                    width={34}
                    height={34}
                    className="rounded-md"
                  />
                  <p className="truncate text-sm font-medium text-fg">{championName}</p>
                </div>
              </Link>
            );
          })}
        </div>
      </Card>
    </div>
  );
}
