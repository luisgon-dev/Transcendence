import Image from "next/image";

import { sortRuneSelections } from "@/components/lol-profile/shared";
import { cn } from "@/lib/cn";
import { itemDisplayName } from "@/lib/gameDisplay";
import {
  itemIconUrl,
  runeIconUrl,
  summonerSpellIconUrl,
  type ItemMap,
  type RuneStaticData,
  type SummonerSpellMap
} from "@/lib/staticData";

// Small loadout pieces shared by the Pro Solo Q index feed and the champion pro-build page.

export function cleanItemIds(itemIds: readonly number[] | null | undefined): number[] {
  return (itemIds ?? []).filter((itemId) => Number.isFinite(itemId) && itemId > 0);
}

export function ItemStrip({
  itemIds,
  itemStatic,
  size = 26,
  className
}: {
  itemIds: readonly number[] | null | undefined;
  itemStatic: ItemMap;
  size?: number;
  className?: string;
}) {
  const ids = cleanItemIds(itemIds);
  if (ids.length === 0) return <span className="type-caption text-muted">No items</span>;
  return (
    <span className={cn("flex flex-wrap items-center gap-1", className)}>
      {ids.map((itemId, idx) => {
        const meta = itemStatic.items[String(itemId)];
        const name = itemDisplayName(meta);
        return (
          <Image
            key={`${itemId}-${idx}`}
            src={itemIconUrl(itemStatic.version, itemId)}
            alt={name}
            title={meta?.plaintext ? `${name}: ${meta.plaintext}` : name}
            width={size}
            height={size}
            sizes={`${size}px`}
            className="rounded-md border border-border/50"
          />
        );
      })}
    </span>
  );
}

export function SpellPair({
  spellIds,
  spellStatic,
  size = 22,
  className
}: {
  spellIds: ReadonlyArray<number | null | undefined>;
  spellStatic: SummonerSpellMap;
  size?: number;
  className?: string;
}) {
  const spells = spellIds
    .map((id) => (id ? spellStatic.spells[String(id)] : undefined))
    .filter((spell) => spell != null);
  if (spells.length === 0) return null;
  return (
    <span className={cn("flex items-center gap-1", className)}>
      {spells.map((spell, idx) => (
        <Image
          key={`${spell.id}-${idx}`}
          src={summonerSpellIconUrl(spellStatic.version, spell.id)}
          alt={spell.name}
          title={spell.name}
          width={size}
          height={size}
          sizes={`${size}px`}
          className="rounded-md border border-border/50"
        />
      ))}
    </span>
  );
}

export function keystoneId(
  primaryRunes: readonly number[] | null | undefined,
  runeStatic: RuneStaticData
): number | null {
  return sortRuneSelections([...(primaryRunes ?? [])], runeStatic.runeSortById)[0] ?? null;
}

/** Keystone over the secondary tree, the same at-a-glance rune read the match history uses. */
export function RunePair({
  primaryRunes,
  subStyleId,
  runeStatic,
  size = 22,
  className
}: {
  primaryRunes: readonly number[] | null | undefined;
  subStyleId: number | null | undefined;
  runeStatic: RuneStaticData;
  size?: number;
  className?: string;
}) {
  const keystone = runeStatic.runeById[String(keystoneId(primaryRunes, runeStatic) ?? 0)];
  const subStyle = runeStatic.styleById[String(subStyleId ?? 0)];
  if (!keystone && !subStyle) return null;
  return (
    <span className={cn("flex items-center gap-1", className)}>
      {keystone ? (
        <Image
          src={runeIconUrl(keystone.icon)}
          alt={keystone.name}
          title={keystone.name}
          width={size}
          height={size}
          sizes={`${size}px`}
          className="rounded-full bg-surface-2/70"
        />
      ) : null}
      {subStyle ? (
        <Image
          src={runeIconUrl(subStyle.icon)}
          alt={subStyle.name}
          title={subStyle.name}
          width={Math.round(size * 0.8)}
          height={Math.round(size * 0.8)}
          sizes={`${size}px`}
          className="rounded-full bg-surface-2/70 p-0.5"
        />
      ) : null}
    </span>
  );
}
