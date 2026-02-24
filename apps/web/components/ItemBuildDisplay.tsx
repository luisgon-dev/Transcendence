import Image from "next/image";

import { cn } from "@/lib/cn";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";
import { itemIconUrl } from "@/lib/staticData";

type ItemMeta = { name: string; plaintext?: string };

function ItemRow({
  itemIds,
  label,
  version,
  items,
  winRate,
  games,
  iconSize = 28
}: {
  itemIds: number[];
  label: string;
  version: string;
  items: Record<string, ItemMeta>;
  winRate?: number | null;
  games?: number | null;
  iconSize?: number;
}) {
  if (itemIds.length === 0) return null;

  return (
    <div className="grid gap-1.5">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium text-muted">{label}</p>
        {winRate != null ? (
          <p className="text-xs text-muted">
            <span className={winRateColorClass(winRate)}>
              {formatPercent(winRate, { decimals: 1 })}
            </span>
            {games != null ? (
              <span className="ml-1">({formatGames(games)})</span>
            ) : null}
          </p>
        ) : null}
      </div>
      <div className="flex flex-wrap items-center gap-1.5">
        {itemIds.map((itemId, idx) => {
          if (!itemId) {
            return (
              <div
                key={`empty-${idx}`}
                className="rounded-md border border-border/60 bg-black/25"
                style={{ width: iconSize, height: iconSize }}
              />
            );
          }
          const meta = items[String(itemId)];
          const title = meta
            ? `${meta.name}${meta.plaintext ? ` \u2014 ${meta.plaintext}` : ""}`
            : `Item ${itemId}`;
          return (
            <Image
              key={`${idx}-${itemId}`}
              src={itemIconUrl(version, itemId)}
              alt={meta?.name ?? `Item ${itemId}`}
              title={title}
              width={iconSize}
              height={iconSize}
              className="rounded-md"
            />
          );
        })}
      </div>
    </div>
  );
}

export function ItemBuildDisplay({
  allItems,
  coreItems,
  situationalItems,
  version,
  items,
  winRate,
  games,
  className
}: {
  allItems: number[];
  coreItems: number[];
  situationalItems: number[];
  version: string;
  items: Record<string, ItemMeta>;
  winRate?: number | null;
  games?: number | null;
  className?: string;
}) {
  return (
    <div className={cn("grid gap-3", className)}>
      {coreItems.length > 0 ? (
        <ItemRow
          itemIds={coreItems}
          label="Core Build"
          version={version}
          items={items}
          winRate={winRate}
          games={games}
        />
      ) : allItems.length > 0 ? (
        <ItemRow
          itemIds={allItems}
          label="Full Build"
          version={version}
          items={items}
          winRate={winRate}
          games={games}
        />
      ) : null}

      {situationalItems.length > 0 ? (
        <ItemRow
          itemIds={situationalItems}
          label="Situational"
          version={version}
          items={items}
        />
      ) : null}
    </div>
  );
}
