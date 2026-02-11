import Image from "next/image";
import Link from "next/link";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { getErrorVerbosity } from "@/lib/env";
import { formatPercent } from "@/lib/format";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";

type TierGrade = "S" | "A" | "B" | "C" | "D";
type TierMovement = "NEW" | "UP" | "DOWN" | "SAME";

type TierListEntry = {
  championId: number;
  role: string;
  tier: TierGrade;
  compositeScore: number;
  winRate: number;
  pickRate: number;
  games: number;
  movement: TierMovement;
  previousTier?: TierGrade | null;
};

type TierListResponse = {
  patch: string;
  role?: string | null;
  rankTier?: string | null;
  entries: TierListEntry[];
};

const ROLES = ["ALL", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;
const RANK_TIERS = [
  "all",
  "IRON",
  "BRONZE",
  "SILVER",
  "GOLD",
  "PLATINUM",
  "EMERALD",
  "DIAMOND",
  "MASTER",
  "GRANDMASTER",
  "CHALLENGER"
] as const;

function movementLabel(m: TierMovement) {
  switch (m) {
    case "UP":
      return "Up";
    case "DOWN":
      return "Down";
    case "NEW":
      return "New";
    case "SAME":
      return "Same";
  }
}

function movementClass(m: TierMovement) {
  switch (m) {
    case "UP":
      return "text-emerald-300";
    case "DOWN":
      return "text-red-300";
    case "NEW":
      return "text-primary";
    case "SAME":
      return "text-fg/70";
  }
}

export default async function TierListPage({
  searchParams
}: {
  searchParams?: Promise<{ role?: string; rankTier?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const qs = new URLSearchParams();
  const roleParam = (resolvedSearchParams?.role ?? "").toUpperCase();
  const rankParam = (resolvedSearchParams?.rankTier ?? "").toUpperCase();

  if (roleParam && roleParam !== "ALL") qs.set("role", roleParam);
  if (rankParam && rankParam !== "ALL") qs.set("rankTier", rankParam);

  const verbosity = getErrorVerbosity();
  const res = await fetchBackendJson<TierListResponse>(
    `${getBackendBaseUrl()}/api/analytics/tierlist?${qs.toString()}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!res.ok) {
    return (
      <BackendErrorCard
        title="Tier List"
        message={
          res.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : res.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load tier list from the backend."
        }
        requestId={res.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: res.status, errorKind: res.errorKind }, null, 2)
            : null
        }
      />
    );
  }

  const tierlist = res.body!;
  const { version, champions } = await fetchChampionMap();

  const groups: Record<TierGrade, TierListEntry[]> = {
    S: [],
    A: [],
    B: [],
    C: [],
    D: []
  };
  for (const e of tierlist.entries) groups[e.tier].push(e);

  const tiers: TierGrade[] = ["S", "A", "B", "C", "D"];

  return (
    <div className="grid gap-6">
      <header className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Patch {tierlist.patch}
          </Badge>
          <Badge>Role: {tierlist.role ?? "ALL"}</Badge>
          <Badge>Tier: {tierlist.rankTier ?? "all"}</Badge>
        </div>
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Tier List
        </h1>
        <p className="text-sm text-fg/75">
          Composite ranking (win rate + pick rate) with movement indicators.
        </p>

        <form className="mt-2 flex flex-wrap items-end gap-2" method="get">
          <label className="grid gap-1">
            <span className="text-xs text-muted">Role</span>
            <select
              name="role"
              defaultValue={roleParam || "ALL"}
              className="h-10 min-w-[160px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            >
              {ROLES.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1">
            <span className="text-xs text-muted">Rank Tier</span>
            <select
              name="rankTier"
              defaultValue={rankParam || "all"}
              className="h-10 min-w-[180px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            >
              {RANK_TIERS.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </label>

          <button
            type="submit"
            className="h-10 rounded-md border border-border/70 bg-white/5 px-4 text-sm text-fg/85 shadow-glass hover:bg-white/10"
          >
            Apply
          </button>
        </form>
      </header>

      <div className="grid gap-6">
        {tiers.map((tier) => {
          const entries = groups[tier];
          if (entries.length === 0) return null;

          return (
            <Card key={tier} className="p-5">
              <div className="flex items-center justify-between">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                  Tier {tier}
                </h2>
                <p className="text-sm text-muted">{entries.length} champions</p>
              </div>

              <div className="mt-4 grid gap-2">
                {entries.map((e) => {
                  const champ = champions[String(e.championId)];
                  const champName = champ?.name ?? `Champion ${e.championId}`;
                  const champId = champ?.id ?? "Unknown";

                  return (
                    <div
                      key={`${tier}-${e.role}-${e.championId}`}
                      className="flex items-center gap-3 rounded-lg border border-border/60 bg-white/5 px-3 py-2"
                    >
                      <Image
                        src={championIconUrl(version, champId)}
                        alt={champName}
                        width={32}
                        height={32}
                        className="rounded-md"
                      />

                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                          <Link
                            href={`/champions/${e.championId}?role=${encodeURIComponent(e.role)}${tierlist.rankTier && tierlist.rankTier !== "all" ? `&rankTier=${encodeURIComponent(tierlist.rankTier)}` : ""}`}
                            className="truncate text-sm font-semibold text-fg hover:underline"
                          >
                            {champName}
                          </Link>
                          <span className="text-xs text-muted">{e.role}</span>
                          <span
                            className={`text-xs font-medium ${movementClass(e.movement)}`}
                            title={
                              e.previousTier
                                ? `Previous: ${e.previousTier}`
                                : undefined
                            }
                          >
                            {movementLabel(e.movement)}
                          </span>
                        </div>

                        <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg/70">
                          <span>Win {formatPercent(e.winRate)}</span>
                          <span>Pick {formatPercent(e.pickRate)}</span>
                          <span>{e.games.toLocaleString()} games</span>
                          <span>Score {e.compositeScore.toFixed(3)}</span>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
