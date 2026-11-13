import type { components } from "@transcendence/api-client";
import { ItemBuildDisplay } from "@/components/ItemBuildDisplay";
import { RuneTreeDisplay } from "@/components/RuneTreeDisplay";
import { WinRateText } from "@/components/WinRateText";
import type { ItemMap, RuneStaticData } from "@/lib/staticData";

type ChampionBuildRow = NonNullable<
  NonNullable<components["schemas"]["ChampionProfileAnalyticsResponse"]["builds"]>["builds"]
>[number];
type ItemLookup = ItemMap["items"];

export function ChevronGlyph() {
  return (
    <svg
      viewBox="0 0 12 12"
      aria-hidden="true"
      className="size-3 shrink-0 text-muted transition-transform duration-150 group-open:rotate-90 motion-reduce:transition-none"
    >
      <path
        d="M4.5 3 7.5 6 4.5 9"
        stroke="currentColor"
        strokeWidth="1.4"
        fill="none"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

/**
 * The descriptive observed build variants. Recommended is open (and alternatives collapse) on the
 * legacy path; when the adjusted recommendation is live the whole list sits behind one disclosure,
 * so nothing auto-expands.
 */
export function BuildVariants({
  rows,
  itemVersion,
  items,
  runeTrees,
  runeById,
  styleById,
  openFirst
}: {
  rows: ChampionBuildRow[];
  itemVersion: string;
  items: ItemLookup;
  runeTrees: RuneStaticData["trees"];
  runeById: RuneStaticData["runeById"];
  styleById: RuneStaticData["styleById"];
  openFirst: boolean;
}) {
  return (
    <div className="grid gap-4">
      <p className="type-note text-muted">
        Recommended balances sample size and results (games × win rate). An alternative may show a
        higher rate from fewer games.
      </p>
      {rows.map((b, idx) => (
        <details
          key={`${b.primaryStyleId ?? 0}-${b.subStyleId ?? 0}-${(b.coreItems ?? []).join("-")}-${(b.items ?? []).join("-")}`}
          open={openFirst && idx === 0}
          className="group rounded-lg border border-border/60 bg-surface-2/40"
        >
          <summary className="flex cursor-pointer list-none items-center justify-between gap-2 p-3 [&::-webkit-details-marker]:hidden">
            <span className="flex items-center gap-2 text-sm font-semibold text-fg">
              <ChevronGlyph />
              {idx === 0 ? "Recommended Build · Most proven" : `Alternative ${idx}`}
            </span>
            <span className="text-xs text-muted">
              <WinRateText value={b.winRate} decimals={1} games={b.games} />
            </span>
          </summary>

          <div className="border-t border-border/40 p-3">
            {/* Items: Core + Situational */}
            <ItemBuildDisplay
              allItems={b.items ?? []}
              coreItems={b.coreItems ?? []}
              situationalItems={b.situationalItems ?? []}
              version={itemVersion}
              items={items}
              winRate={b.winRate}
              games={b.games}
            />

            {/* Runes */}
            <div className="mt-3 border-t border-border/40 pt-3">
              <p className="mb-2 text-xs font-medium text-muted">Runes</p>
              <RuneTreeDisplay
                primaryStyleId={b.primaryStyleId ?? 0}
                subStyleId={b.subStyleId ?? 0}
                primarySelections={b.primaryRunes ?? []}
                subSelections={b.subRunes ?? []}
                statShards={b.statShards ?? []}
                trees={runeTrees}
                runeById={runeById}
                styleById={styleById}
              />
            </div>
          </div>
        </details>
      ))}
    </div>
  );
}
