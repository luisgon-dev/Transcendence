import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { WinRateText } from "@/components/WinRateText";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";

type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"];
type ChampionBuildsResponse = components["schemas"]["ChampionBuildsResponse"];

const REGION_TABS = ["ALL", "KR", "EUW", "NA", "CN"] as const;

export default async function ProBuildsChampionPage({
  params
}: {
  params: Promise<{ championId: string }>;
}) {
  const resolvedParams = await params;
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return <BackendErrorCard title="Pro Builds" message="Invalid champion id." />;
  }

  const verbosity = getErrorVerbosity();
  const [{ version, champions }, winratesRes, buildsRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/winrates`,
      { next: { revalidate: 60 * 60 } }
    ),
    fetchBackendJson<ChampionBuildsResponse>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/builds?role=BOTTOM`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  if (!winratesRes.ok && !buildsRes.ok) {
    return (
      <BackendErrorCard
        title="Pro Builds"
        message={
          winratesRes.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : winratesRes.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load champion data."
        }
        requestId={winratesRes.requestId || buildsRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  winrates: { status: winratesRes.status, errorKind: winratesRes.errorKind },
                  builds: { status: buildsRes.status, errorKind: buildsRes.errorKind }
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const winrates = winratesRes.ok ? winratesRes.body : null;
  const builds = buildsRes.ok ? buildsRes.body : null;
  const champion = champions[String(championId)];
  const championName = champion?.name ?? `Champion ${championId}`;

  const mostPlayed = (winrates?.byRoleTier ?? [])
    .slice()
    .sort((a, b) => (b.games ?? 0) - (a.games ?? 0))[0];
  const primaryBuild = (builds?.builds ?? [])
    .slice()
    .sort((a, b) => (b.games ?? 0) - (a.games ?? 0))[0];

  return (
    <div className="grid gap-6">
      <header className="grid gap-3">
        <div className="flex items-center gap-3">
          <Image
            src={championIconUrl(version, champion?.id ?? "Unknown")}
            alt={championName}
            width={60}
            height={60}
            className="rounded-xl border border-border/60"
          />
          <div>
            <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
              Pro Builds
            </h1>
            <p className="text-sm text-fg/75">{championName}</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/45 bg-primary/10 text-primary">Public Preview</Badge>
          <Badge>Pro feed backend: Pending</Badge>
          {winrates?.patch ? <Badge>Patch {winrates.patch}</Badge> : null}
        </div>
      </header>

      <Card className="p-5">
        <h2 className="font-[var(--font-sora)] text-lg font-semibold">Recent Pro Matches</h2>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {REGION_TABS.map((region) => (
            <span
              key={region}
              className={`rounded-full border px-2.5 py-1 text-xs ${
                region === "ALL"
                  ? "border-primary/45 bg-primary/10 text-primary"
                  : "border-border/60 bg-white/[0.03] text-fg/75"
              }`}
            >
              {region}
            </span>
          ))}
        </div>
        <div className="mt-4 rounded-lg border border-dashed border-border/60 bg-white/[0.02] p-4">
          <p className="text-sm text-fg/85">
            Pro match timeline is not available yet. Once backend support lands, this section will show verified
            high-ELO and pro matches with runes, item spikes, and timestamps.
          </p>
        </div>
      </Card>

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">Most Common Build</h2>
          {primaryBuild ? (
            <div className="mt-3 grid gap-2 text-sm">
              <p className="text-fg/85">
                We can show current public analytics while pro data is pending.
              </p>
              <p className="text-xs text-muted">
                <WinRateText value={primaryBuild.winRate} decimals={1} games={primaryBuild.games} />
              </p>
            </div>
          ) : (
            <p className="mt-3 text-sm text-muted">No baseline build samples available.</p>
          )}
        </Card>

        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">Top Players</h2>
          <div className="mt-3 rounded-lg border border-dashed border-border/60 bg-white/[0.02] p-4">
            <p className="text-sm text-fg/85">
              Leaderboard data is disabled until pro profile ingestion is integrated.
            </p>
          </div>
        </Card>
      </div>

      <Card className="border-primary/40 bg-primary/10 p-5">
        <h2 className="font-[var(--font-sora)] text-lg font-semibold text-primary">What is already live</h2>
        <div className="mt-2 grid gap-1 text-sm text-fg/90">
          <p>Champion win rates and standard build analytics are available today.</p>
          {mostPlayed ? (
            <p className="text-xs text-fg/80">
              Current most-played role sample:{" "}
              <span className="font-medium text-fg">{mostPlayed.role ?? "Unknown"}</span>{" "}
              <WinRateText value={mostPlayed.winRate} decimals={1} games={mostPlayed.games} />
            </p>
          ) : null}
        </div>
        <div className="mt-4 flex flex-wrap gap-2 text-sm">
          <Link
            href={`/champions/${championId}`}
            className="rounded-md border border-primary/40 bg-primary/15 px-3 py-1.5 text-primary hover:bg-primary/25"
          >
            Open Champion Details
          </Link>
          <Link
            href={`/matchups/${championId}`}
            className="rounded-md border border-border/70 bg-white/[0.05] px-3 py-1.5 text-fg/85 hover:bg-white/[0.10]"
          >
            Open Matchup Analysis
          </Link>
        </div>
      </Card>
    </div>
  );
}
