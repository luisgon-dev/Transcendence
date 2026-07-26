import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { Card } from "@/components/ui/Card";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";
import { formatStrengthDelta } from "@/lib/tierlist";

type ChampionSynergiesResponse = components["schemas"]["ChampionSynergiesResponse"];

function partnerHeading(role: string) {
  if (role === "BOTTOM") return "Best support partners";
  if (role === "UTILITY") return "Best bot-lane carries";
  if (role === "JUNGLE") return "Best lane partners";
  return "Best jungle partners";
}

export function ChampionSynergyPanel({
  championName,
  synergies,
  champions,
  version,
  linkQuery
}: {
  championName: string;
  synergies: ChampionSynergiesResponse | null;
  champions: Record<string, { id: string; name: string; title?: string }>;
  version: string;
  linkQuery: string;
}) {
  if (!synergies) return null;
  const partners = synergies.bestPartners ?? [];

  return (
    <Card className="overflow-hidden p-0" id="synergies">
      <div className="flex flex-wrap items-end justify-between gap-3 border-b border-border/40 px-5 py-4">
        <div>
          <p className="type-kicker text-primary/80">Duo chemistry</p>
          <h2 className="type-section mt-2">{partnerHeading(synergies.role)}</h2>
          <p className="type-ui mt-1 text-muted">
            Same-team outcomes with {championName} as {roleDisplayLabel(synergies.role)}.
          </p>
        </div>
        {synergies.totalGames > 0 ? (
          <p className="type-caption text-muted">
            Baseline {formatPercent(synergies.baselineWinRate, { input: "ratio" })} · {formatGames(synergies.totalGames)} games
          </p>
        ) : null}
      </div>

      {partners.length === 0 ? (
        <p className="p-5 text-sm text-muted">
          No partner pairing has enough games in this region, rank, and patch scope yet.
        </p>
      ) : (
        <div className="grid gap-px bg-border/35 sm:grid-cols-2 xl:grid-cols-5">
          {partners.slice(0, 5).map((partner, index) => {
            const champion = champions[String(partner.partnerChampionId)];
            const params = new URLSearchParams(linkQuery);
            params.set("role", partner.partnerRole);
            const deltaPositive = partner.winRateDelta > 0;
            const deltaNegative = partner.winRateDelta < 0;
            return (
              <Link
                key={`${partner.partnerChampionId}-${partner.partnerRole}`}
                href={`/lol/champions/${partner.partnerChampionId}${params.size ? `?${params}` : ""}`}
                className="group min-w-0 bg-surface/95 p-4 transition hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary/30"
              >
                <div className="flex items-center gap-3">
                  {champion ? (
                    <Image
                      src={championIconUrl(version, champion.id)}
                      alt=""
                      width={42}
                      height={42}
                      sizes="42px"
                      priority={index === 0}
                      className="rounded-xl border border-border/45"
                    />
                  ) : null}
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-fg transition group-hover:text-primary">
                      {championDisplayName(champion)}
                    </p>
                    <p className="mt-0.5 inline-flex items-center gap-1 text-xs text-muted">
                      <LaneIcon role={partner.partnerRole} className="h-3.5 w-3.5" />
                      {roleDisplayLabel(partner.partnerRole)}
                    </p>
                  </div>
                </div>
                <div className="mt-4 flex items-end justify-between gap-2 border-t border-border/25 pt-3">
                  <div>
                    <p className="type-kicker text-fg/45">Pair win rate</p>
                    <p className={`type-ui mt-1 font-semibold ${winRateColorClass(partner.winRate)}`}>
                      {formatPercent(partner.winRate, { input: "ratio" })}
                    </p>
                  </div>
                  <div className="text-right">
                    <p className={`type-ui font-semibold ${deltaPositive ? "text-success" : deltaNegative ? "text-wr-low" : "text-fg/65"}`}>
                      {formatStrengthDelta(partner.winRateDelta)}
                    </p>
                    <p className="type-caption mt-1 text-muted">{formatGames(partner.games)} games</p>
                  </div>
                </div>
              </Link>
            );
          })}
        </div>
      )}

      <p className="border-t border-border/35 px-5 py-3 text-xs leading-5 text-muted">
        Ranked by confidence-adjusted lift, not raw win rate. Pair results are descriptive and still reflect player skill, patch balance, and selection effects.
      </p>
    </Card>
  );
}
