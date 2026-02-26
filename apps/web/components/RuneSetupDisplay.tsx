import Image from "next/image";

import { runeIconUrl } from "@/lib/staticData";
import { cn } from "@/lib/cn";

type RuneMeta = { name: string; icon: string };
type StyleMeta = { name: string; icon: string };

function sortRuneIds(runeIds: number[], runeSortById?: Record<string, number>) {
  if (!runeSortById) return runeIds;

  return runeIds
    .slice()
    .sort((a, b) => {
      const aKey = runeSortById[String(a)] ?? Number.MAX_SAFE_INTEGER;
      const bKey = runeSortById[String(b)] ?? Number.MAX_SAFE_INTEGER;
      return aKey - bKey;
    });
}

function RuneIcon({
  runeId,
  runeById,
  size,
  className
}: {
  runeId: number;
  runeById: Record<string, RuneMeta>;
  size: number;
  className?: string;
}) {
  const rune = runeById[String(runeId)];
  if (!rune) {
    return (
      <div
        className={cn("rounded-full border border-border/60 bg-black/20", className)}
        style={{ width: size, height: size }}
      />
    );
  }

  return (
    <Image
      src={runeIconUrl(rune.icon)}
      alt={rune.name}
      title={rune.name}
      width={size}
      height={size}
      className={cn("rounded-full border border-border/35 bg-black/20 p-0.5", className)}
    />
  );
}

function StyleIcon({
  styleId,
  styleById,
  size
}: {
  styleId: number;
  styleById: Record<string, StyleMeta>;
  size: number;
}) {
  const style = styleById[String(styleId)];
  if (!style) {
    return (
      <div
        className="rounded-md border border-border/60 bg-black/20"
        style={{ width: size, height: size }}
      />
    );
  }

  return (
    <Image
      src={runeIconUrl(style.icon)}
      alt={style.name}
      title={style.name}
      width={size}
      height={size}
      className="rounded-md bg-black/20 p-0.5"
    />
  );
}

export function RuneSetupDisplay({
  primaryStyleId,
  subStyleId,
  primarySelections,
  subSelections,
  statShards,
  runeById,
  styleById,
  runeSortById,
  iconSize = 24,
  density = "default",
  className
}: {
  primaryStyleId: number;
  subStyleId: number;
  primarySelections: number[];
  subSelections: number[];
  statShards: number[];
  runeById: Record<string, RuneMeta>;
  styleById: Record<string, StyleMeta>;
  runeSortById?: Record<string, number>;
  iconSize?: number;
  density?: "default" | "compact";
  className?: string;
}) {
  const isCompact = density === "compact";
  const primaryRunes = sortRuneIds(primarySelections, runeSortById).slice(0, 4);
  const secondaryRunes = sortRuneIds(subSelections, runeSortById).slice(0, 2);
  const shards = sortRuneIds(statShards, runeSortById).slice(0, 3);

  return (
    <div className={cn("grid gap-2", className)}>
      <div className={cn("grid grid-cols-2", isCompact ? "gap-2" : "gap-3")}>
        <div className={cn("rounded-lg border border-border/45 bg-black/10", isCompact ? "p-1.5" : "p-2")}>
          <div className={cn("flex items-center gap-2", isCompact ? "mb-1" : "mb-2")}>
            <StyleIcon
              styleId={primaryStyleId}
              styleById={styleById}
              size={iconSize + (isCompact ? 1 : 2)}
            />
            <span className={cn("uppercase tracking-wide text-muted", isCompact ? "text-[9px]" : "text-[10px]")}>
              Primary
            </span>
          </div>
          <div className={cn("grid", isCompact ? "gap-1" : "gap-1.5")}>
            {primaryRunes.map((runeId, idx) => (
              <RuneIcon
                key={`primary-${idx}-${runeId}`}
                runeId={runeId}
                runeById={runeById}
                size={iconSize + (idx === 0 ? (isCompact ? 3 : 6) : 0)}
              />
            ))}
          </div>
        </div>

        <div className={cn("rounded-lg border border-border/45 bg-black/10", isCompact ? "p-1.5" : "p-2")}>
          <div className={cn("flex items-center gap-2", isCompact ? "mb-1" : "mb-2")}>
            <StyleIcon
              styleId={subStyleId}
              styleById={styleById}
              size={iconSize + (isCompact ? 1 : 2)}
            />
            <span className={cn("uppercase tracking-wide text-muted", isCompact ? "text-[9px]" : "text-[10px]")}>
              Secondary
            </span>
          </div>
          <div className={cn("grid", isCompact ? "gap-1" : "gap-1.5")}>
            {secondaryRunes.map((runeId, idx) => (
              <RuneIcon
                key={`sub-${idx}-${runeId}`}
                runeId={runeId}
                runeById={runeById}
                size={iconSize}
              />
            ))}
          </div>
        </div>
      </div>

      <div className={cn("rounded-lg border border-border/45 bg-black/10", isCompact ? "p-1.5" : "p-2")}>
        <div className={cn("flex items-center gap-2", isCompact ? "mb-0.5" : "mb-1")}>
          <span className={cn("uppercase tracking-wide text-muted", isCompact ? "text-[9px]" : "text-[10px]")}>
            Shards
          </span>
        </div>
        <div className={cn("flex items-center", isCompact ? "gap-1" : "gap-1.5")}>
          {shards.map((runeId, idx) => (
            <RuneIcon
              key={`shard-${idx}-${runeId}`}
              runeId={runeId}
              runeById={runeById}
              size={iconSize}
            />
          ))}
        </div>
      </div>
      {primaryRunes.length === 0 && secondaryRunes.length === 0 && shards.length === 0 ? (
        <p className="text-xs text-muted">Runes unavailable.</p>
      ) : null}
      <div className="sr-only">
        Primary runes: {primaryRunes.join(", ")}. Secondary runes: {secondaryRunes.join(", ")}.
      </div>
    </div>
  );
}
