import Image from "next/image";

import { runeIconUrl } from "@/lib/staticData";

type RuneMeta = { name: string; icon: string };
type StyleMeta = { name: string; icon: string };

function RuneIcon({
  runeId,
  runeById,
  size
}: {
  runeId: number;
  runeById: Record<string, RuneMeta>;
  size: number;
}) {
  const rune = runeById[String(runeId)];
  if (!rune) {
    return (
      <div
        className="rounded-md border border-border/60 bg-black/20"
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
      className="rounded-md bg-black/20 p-0.5"
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
  iconSize = 20
}: {
  primaryStyleId: number;
  subStyleId: number;
  primarySelections: number[];
  subSelections: number[];
  statShards: number[];
  runeById: Record<string, RuneMeta>;
  styleById: Record<string, StyleMeta>;
  iconSize?: number;
}) {
  return (
    <div className="grid gap-1.5">
      <div className="flex items-center gap-1.5">
        <StyleIcon styleId={primaryStyleId} styleById={styleById} size={iconSize} />
        {primarySelections.slice(0, 4).map((runeId, idx) => (
          <RuneIcon
            key={`primary-${idx}-${runeId}`}
            runeId={runeId}
            runeById={runeById}
            size={iconSize}
          />
        ))}
      </div>

      <div className="flex items-center gap-1.5">
        <StyleIcon styleId={subStyleId} styleById={styleById} size={iconSize} />
        {subSelections.slice(0, 2).map((runeId, idx) => (
          <RuneIcon
            key={`sub-${idx}-${runeId}`}
            runeId={runeId}
            runeById={runeById}
            size={iconSize}
          />
        ))}
      </div>

      <div className="flex items-center gap-1.5">
        <span className="w-5 text-[10px] text-muted">S</span>
        {statShards.slice(0, 3).map((runeId, idx) => (
          <RuneIcon
            key={`shard-${idx}-${runeId}`}
            runeId={runeId}
            runeById={runeById}
            size={iconSize}
          />
        ))}
      </div>
    </div>
  );
}
