import type { ChampionMap } from "@/lib/staticData";
import type { UITierListEntry } from "@/lib/tierlist";
import { LANE_ROLES } from "@/lib/roles";

const MAX_STARTER_DIFFICULTY = 4;

export type StarterPick = UITierListEntry & {
  champion: ChampionMap["champions"][string];
};

/**
 * Selects one current-patch starter suggestion per lane. Riot's static
 * difficulty rating provides the approachability gate; our role-adjusted
 * strength score only ranks the stable candidates that pass it.
 */
export function selectStarterPicks(
  entries: UITierListEntry[],
  champions: ChampionMap["champions"]
): StarterPick[] {
  return LANE_ROLES.flatMap((role) => {
    const pick = entries
      .filter((entry) => {
        const champion = champions[String(entry.championId)];
        return (
          entry.role === role &&
          !entry.isLowSample &&
          champion?.difficulty !== undefined &&
          champion.difficulty <= MAX_STARTER_DIFFICULTY
        );
      })
      .sort((a, b) => b.strengthScore - a.strengthScore || b.games - a.games)[0];

    if (!pick) return [];
    return [{ ...pick, champion: champions[String(pick.championId)] }];
  });
}
