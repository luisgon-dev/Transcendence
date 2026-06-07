import Image from "next/image";

import { runeIconUrl, type RuneTree } from "@/lib/staticData";
import { STAT_SHARD_ROWS } from "@/lib/staticData";
import { cn } from "@/lib/cn";

type RuneMeta = { name: string; icon: string };

function RuneNode({
  id,
  fallbackName,
  fallbackIcon,
  runeById,
  selected,
  size,
  shape = "circle"
}: {
  id: number;
  fallbackName: string;
  fallbackIcon: string;
  runeById: Record<string, RuneMeta>;
  selected: boolean;
  size: number;
  shape?: "circle" | "square";
}) {
  const meta = runeById[String(id)];
  const name = meta?.name ?? fallbackName;
  const icon = meta?.icon ?? fallbackIcon;
  const radius = shape === "circle" ? "rounded-full" : "rounded-md";

  return (
    <span
      title={name}
      className={cn(
        "inline-flex items-center justify-center border transition-opacity",
        radius,
        selected
          ? "border-primary/70 bg-primary/10 shadow-[0_0_0_1px_var(--color-primary)] ring-1 ring-primary/60"
          : "border-border/40 bg-surface-2/60 opacity-35 grayscale"
      )}
      style={{ width: size, height: size }}
    >
      <Image
        src={runeIconUrl(icon)}
        alt={name}
        title={name}
        width={size}
        height={size}
        sizes={`${size}px`}
        className={cn(radius, "p-0.5")}
      />
    </span>
  );
}

function StyleHeader({
  styleId,
  styleById,
  label,
  size
}: {
  styleId: number;
  styleById: Record<string, RuneMeta>;
  label: string;
  size: number;
}) {
  const style = styleById[String(styleId)];
  return (
    <div className="mb-2 flex items-center gap-2">
      {style ? (
        <Image
          src={runeIconUrl(style.icon)}
          alt={style.name}
          width={size}
          height={size}
          sizes={`${size}px`}
          className="rounded-md"
        />
      ) : (
        <span className="rounded-md bg-surface-2/70" style={{ width: size, height: size }} />
      )}
      <span className="type-overline text-muted">{label}</span>
    </div>
  );
}

/**
 * Renders the full rune page like the in-client picker / u.gg: every rune in the
 * primary tree (keystone row + 3 rows) and secondary tree (3 non-keystone rows),
 * with the chosen runes highlighted and the rest dimmed, plus the 3×3 stat shards.
 */
export function RuneTreeDisplay({
  primaryStyleId,
  subStyleId,
  primarySelections,
  subSelections,
  statShards,
  trees,
  runeById,
  styleById,
  iconSize = 26,
  className
}: {
  primaryStyleId: number;
  subStyleId: number;
  primarySelections: number[];
  subSelections: number[];
  statShards: number[];
  trees: RuneTree[];
  runeById: Record<string, RuneMeta>;
  styleById: Record<string, RuneMeta>;
  iconSize?: number;
  className?: string;
}) {
  const primaryTree = trees.find((tree) => tree.id === primaryStyleId);
  const secondaryTree = trees.find((tree) => tree.id === subStyleId);
  const primarySet = new Set(primarySelections);
  const subSet = new Set(subSelections);

  // No structured trees available (e.g. static fetch failed) — render nothing rather than break.
  if (!primaryTree && !secondaryTree) {
    return <p className="type-caption text-muted">Runes unavailable.</p>;
  }

  const keystoneSize = iconSize + 6;

  return (
    <div className={cn("grid w-full gap-4 sm:grid-cols-2", className)}>
      {primaryTree ? (
        <div className="surface-subtle rounded-control p-3">
          <StyleHeader styleId={primaryStyleId} styleById={styleById} label="Primary" size={iconSize} />
          <div className="grid gap-2.5">
            {primaryTree.slots.map((slot, slotIdx) => (
              <div
                key={`p-slot-${slotIdx}`}
                className={cn(
                  "flex items-center justify-between gap-1.5",
                  slotIdx === 0 && "gap-2 border-b border-border/30 pb-2.5"
                )}
              >
                {slot.runes.map((rune) => (
                  <RuneNode
                    key={`p-${rune.id}`}
                    id={rune.id}
                    fallbackName={rune.name}
                    fallbackIcon={rune.icon}
                    runeById={runeById}
                    selected={primarySet.has(rune.id)}
                    size={slotIdx === 0 ? keystoneSize : iconSize}
                  />
                ))}
              </div>
            ))}
          </div>
        </div>
      ) : null}

      <div className="grid gap-3">
        {secondaryTree ? (
          <div className="surface-subtle rounded-control p-3">
            <StyleHeader styleId={subStyleId} styleById={styleById} label="Secondary" size={iconSize} />
            <div className="grid gap-2.5">
              {secondaryTree.slots.slice(1).map((slot, slotIdx) => (
                <div key={`s-slot-${slotIdx}`} className="flex items-center justify-between gap-1.5">
                  {slot.runes.map((rune) => (
                    <RuneNode
                      key={`s-${rune.id}`}
                      id={rune.id}
                      fallbackName={rune.name}
                      fallbackIcon={rune.icon}
                      runeById={runeById}
                      selected={subSet.has(rune.id)}
                      size={iconSize}
                    />
                  ))}
                </div>
              ))}
            </div>
          </div>
        ) : null}

        <div className="surface-subtle rounded-control p-3">
          <span className="type-overline text-muted">Shards</span>
          <div className="mt-2 grid gap-1.5">
            {STAT_SHARD_ROWS.map((row, rowIdx) => {
              const selectedForRow = statShards[rowIdx];
              return (
                <div key={`shard-row-${rowIdx}`} className="flex items-center justify-between gap-1.5">
                  {row.map((shardId, colIdx) => (
                    <RuneNode
                      key={`shard-${rowIdx}-${colIdx}-${shardId}`}
                      id={shardId}
                      fallbackName={`Shard ${shardId}`}
                      fallbackIcon=""
                      runeById={runeById}
                      selected={shardId === selectedForRow}
                      size={iconSize - 4}
                      shape="circle"
                    />
                  ))}
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}
