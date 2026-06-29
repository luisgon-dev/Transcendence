import { Fragment } from "react";
import Image from "next/image";
import type { components } from "@transcendence/api-client";

import { cn } from "@/lib/cn";
import { WinRateText } from "@/components/WinRateText";
import { Confidence } from "@/components/ui/Confidence";
import { itemIconUrl, summonerSpellIconUrl } from "@/lib/staticData";

type SummonerSpellPairDto = components["schemas"]["SummonerSpellPairDto"];
type SkillOrderDto = components["schemas"]["SkillOrderDto"];
type StarterItemSetDto = components["schemas"]["StarterItemSetDto"];
type ItemChoiceDto = components["schemas"]["ItemChoiceDto"];
type CoreItemStepDto = components["schemas"]["CoreItemStepDto"];
type SituationalSlotDto = components["schemas"]["SituationalSlotDto"];

type ItemMeta = { name: string; plaintext?: string };
type ItemMap = Record<string, ItemMeta>;
type SpellMap = Record<string, { id: string; name: string }>;

function formatGameClock(minutes?: number | null) {
  if (minutes == null || !Number.isFinite(minutes) || minutes <= 0) return "";
  const totalSeconds = Math.round(minutes * 60);
  const mm = Math.floor(totalSeconds / 60);
  const ss = totalSeconds % 60;
  return `${mm}:${String(ss).padStart(2, "0")}`;
}

function ordinalLabel(slot: number) {
  switch (slot) {
    case 4:
      return "4th";
    case 5:
      return "5th";
    case 6:
      return "6th";
    default:
      return `${slot}th`;
  }
}

function Section({
  label,
  children,
  className
}: {
  label: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("grid content-start gap-2", className)}>
      <p className="type-overline text-muted">{label}</p>
      {children}
    </div>
  );
}

function ItemIcon({
  itemId,
  version,
  items,
  size = 30
}: {
  itemId: number;
  version: string;
  items: ItemMap;
  size?: number;
}) {
  if (!itemId) {
    return (
      <div
        className="rounded-md border border-border/50 bg-surface-2/60"
        style={{ width: size, height: size }}
      />
    );
  }
  const meta = items[String(itemId)];
  const title = meta
    ? `${meta.name}${meta.plaintext ? ` — ${meta.plaintext}` : ""}`
    : `Item ${itemId}`;
  return (
    <Image
      src={itemIconUrl(version, itemId)}
      alt={meta?.name ?? `Item ${itemId}`}
      title={title}
      width={size}
      height={size}
      sizes={`${size}px`}
      className="rounded-md border border-border/50"
    />
  );
}

function SpellPairIcons({
  spell1Id,
  spell2Id,
  version,
  spells,
  size = 26
}: {
  spell1Id: number;
  spell2Id: number;
  version: string;
  spells: SpellMap;
  size?: number;
}) {
  return (
    <div className="flex items-center gap-1">
      {[spell1Id, spell2Id].map((id, idx) => {
        const spell = spells[String(id)];
        if (!spell) {
          return (
            <div
              key={idx}
              className="rounded-md border border-border/50 bg-surface-2/60"
              style={{ width: size, height: size }}
            />
          );
        }
        return (
          <Image
            key={idx}
            src={summonerSpellIconUrl(version, spell.id)}
            alt={spell.name}
            title={spell.name}
            width={size}
            height={size}
            sizes={`${size}px`}
            className="rounded-md border border-border/50"
          />
        );
      })}
    </div>
  );
}

function AbilityPill({ letter, emphasis = false }: { letter: string; emphasis?: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex h-6 w-6 items-center justify-center rounded-control border text-xs font-semibold",
        emphasis
          ? "border-border-strong bg-surface-2 text-fg"
          : "border-border/60 bg-surface-2/50 text-fg/80"
      )}
    >
      {letter}
    </span>
  );
}

export function BuildBreakdown({
  summonerSpells,
  skillOrder,
  startingItems,
  boots,
  coreBuildPath,
  situationalSlots,
  itemVersion,
  items,
  spellVersion,
  spells,
  minGames,
  className
}: {
  summonerSpells?: SummonerSpellPairDto[] | null;
  skillOrder?: SkillOrderDto | null;
  startingItems?: StarterItemSetDto[] | null;
  boots?: ItemChoiceDto[] | null;
  coreBuildPath?: CoreItemStepDto[] | null;
  situationalSlots?: SituationalSlotDto[] | null;
  itemVersion: string;
  items: ItemMap;
  spellVersion: string;
  spells: SpellMap;
  /** Sample-size threshold for the per-variant confidence pips (falls back to the default). */
  minGames?: number;
  className?: string;
}) {
  const spellPairs = summonerSpells ?? [];
  const starters = startingItems ?? [];
  const bootOptions = boots ?? [];
  const corePath = coreBuildPath ?? [];
  const situational = situationalSlots ?? [];

  const maxOrderLetters = (skillOrder?.maxOrder ?? "")
    .split(">")
    .map((letter) => letter.trim())
    .filter(Boolean);
  const firstThreeLetters = (skillOrder?.firstThree ?? "").split("").filter(Boolean);

  const hasAnySection =
    spellPairs.length > 0 ||
    maxOrderLetters.length > 0 ||
    starters.length > 0 ||
    bootOptions.length > 0 ||
    corePath.length > 0 ||
    situational.length > 0;

  if (!hasAnySection) return null;

  return (
    <div className={cn("grid gap-4", className)}>
      <div className="grid gap-4 sm:grid-cols-2">
        {spellPairs.length > 0 ? (
          <Section label="Summoner Spells">
            <div className="grid gap-2">
              {spellPairs.slice(0, 2).map((pair, idx) => (
                <div key={idx} className="flex items-center justify-between gap-2">
                  <SpellPairIcons
                    spell1Id={pair.spell1Id ?? 0}
                    spell2Id={pair.spell2Id ?? 0}
                    version={spellVersion}
                    spells={spells}
                  />
                  <span className="flex items-center gap-1.5">
                    <WinRateText value={pair.winRate} games={pair.games} pickRate={pair.pickRate} className="text-xs" />
                    {pair.games != null ? <Confidence games={pair.games} minGames={minGames} /> : null}
                  </span>
                </div>
              ))}
            </div>
          </Section>
        ) : null}

        {maxOrderLetters.length > 0 ? (
          <Section label="Skill Order">
            <div className="grid gap-2">
              <div className="flex flex-wrap items-center gap-1.5">
                <span className="w-14 text-xs text-muted">Priority</span>
                {maxOrderLetters.map((letter, idx) => (
                  <Fragment key={idx}>
                    {idx > 0 ? <span className="text-xs text-muted">›</span> : null}
                    <AbilityPill letter={letter} emphasis={idx === 0} />
                  </Fragment>
                ))}
                {skillOrder?.winRate != null ? (
                  <>
                    <WinRateText
                      value={skillOrder.winRate}
                      games={skillOrder.games}
                      pickRate={skillOrder.pickRate}
                      className="ml-1 text-xs"
                    />
                    {skillOrder.games != null ? (
                      <Confidence games={skillOrder.games} minGames={minGames} />
                    ) : null}
                  </>
                ) : null}
              </div>
              {firstThreeLetters.length > 0 ? (
                <div className="flex items-center gap-1.5">
                  <span className="w-14 text-xs text-muted">First 3</span>
                  {firstThreeLetters.map((letter, idx) => (
                    <AbilityPill key={idx} letter={letter} />
                  ))}
                </div>
              ) : null}
            </div>
          </Section>
        ) : null}

        {starters.length > 0 ? (
          <Section label="Starting Items">
            <div className="grid gap-2">
              {starters.slice(0, 2).map((set, idx) => (
                <div key={idx} className="flex items-center justify-between gap-2">
                  <div className="flex flex-wrap items-center gap-1">
                    {(set.items ?? []).map((itemId, itemIdx) => (
                      <ItemIcon
                        key={itemIdx}
                        itemId={itemId}
                        version={itemVersion}
                        items={items}
                        size={26}
                      />
                    ))}
                  </div>
                  <span className="flex items-center gap-1.5">
                    <WinRateText value={set.winRate} games={set.games} pickRate={set.pickRate} className="text-xs" />
                    {set.games != null ? <Confidence games={set.games} minGames={minGames} /> : null}
                  </span>
                </div>
              ))}
            </div>
          </Section>
        ) : null}

        {bootOptions.length > 0 ? (
          <Section label="Boots">
            <div className="grid gap-2">
              {bootOptions.slice(0, 3).map((boot, idx) => (
                <div key={idx} className="flex items-center justify-between gap-2">
                  <ItemIcon itemId={boot.itemId ?? 0} version={itemVersion} items={items} size={26} />
                  <span className="flex items-center gap-1.5">
                    <WinRateText value={boot.winRate} games={boot.games} pickRate={boot.pickRate} className="text-xs" />
                    {boot.games != null ? <Confidence games={boot.games} minGames={minGames} /> : null}
                  </span>
                </div>
              ))}
            </div>
          </Section>
        ) : null}
      </div>

      {corePath.length > 0 ? (
        <Section label="Core Build Path">
          <div className="flex flex-wrap items-start gap-x-1.5 gap-y-3">
            {corePath.map((step, idx) => (
              <Fragment key={idx}>
                {idx > 0 ? (
                  <span className="self-start pt-2 text-muted" aria-hidden>
                    →
                  </span>
                ) : null}
                <div className="grid justify-items-center gap-1">
                  <ItemIcon itemId={step.itemId ?? 0} version={itemVersion} items={items} size={40} />
                  <span className="type-tabular text-[11px] tabular-nums text-muted">
                    {formatGameClock(step.avgCompletionMinute)}
                  </span>
                  <WinRateText value={step.winRate} decimals={0} className="text-[11px]" />
                </div>
              </Fragment>
            ))}
          </div>
        </Section>
      ) : null}

      {situational.length > 0 ? (
        <Section label="Situational (4th–6th)">
          <div className="grid gap-2">
            {situational.map((slot) => (
              <div key={slot.slot} className="flex items-start gap-2">
                <span className="w-9 shrink-0 pt-1.5 text-xs text-muted">
                  {ordinalLabel(slot.slot ?? 0)}
                </span>
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                  {(slot.options ?? []).map((option, idx) => (
                    <div key={idx} className="flex items-center gap-1">
                      <ItemIcon
                        itemId={option.itemId ?? 0}
                        version={itemVersion}
                        items={items}
                        size={26}
                      />
                      <WinRateText
                        value={option.winRate}
                        games={option.games}
                        pickRate={option.pickRate}
                        className="text-[11px]"
                      />
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </Section>
      ) : null}
    </div>
  );
}
