import { RuneTreeDisplay } from "@/components/RuneTreeDisplay";
import { hasRunes, type MatchParticipant, type RuneStatic } from "@/components/lol-profile/shared";
import { cn } from "@/lib/cn";

// Single source of the participant.runes → RuneTreeDisplay mapping. Shared by the
// scoreboard's Runes tab (side-by-side compare) and the keystone hover tooltip, so
// the rune page renders identically wherever it surfaces. Compact + single-column
// so it fits a narrow cell or a tooltip without the lopsided-card problem the old
// per-card "Show Runes" toggle caused.
export function ParticipantRuneCard({
  participant,
  runeStatic,
  className
}: {
  participant: MatchParticipant;
  runeStatic: RuneStatic | null;
  className?: string;
}) {
  if (!runeStatic || !hasRunes(participant.runes)) {
    return <p className={cn("type-caption text-muted", className)}>Runes unavailable.</p>;
  }

  const runes = participant.runes;

  return (
    <RuneTreeDisplay
      primaryStyleId={runes.primaryStyleId ?? 0}
      subStyleId={runes.subStyleId ?? 0}
      primarySelections={runes.primarySelections ?? []}
      subSelections={runes.subSelections ?? []}
      statShards={runes.statShards ?? []}
      trees={runeStatic.trees ?? []}
      runeById={runeStatic.runeById}
      styleById={runeStatic.styleById}
      density="compact"
      gridClassName="grid w-full gap-2"
      className={className}
    />
  );
}
