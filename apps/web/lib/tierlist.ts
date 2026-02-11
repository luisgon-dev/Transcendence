import type { components } from "@transcendence/api-client/schema";

export type UITierGrade = "S" | "A" | "B" | "C" | "D";
export type UITierMovement = "NEW" | "UP" | "DOWN" | "SAME";

export type UITierListEntry = {
  championId: number;
  role: string;
  tier: UITierGrade;
  compositeScore: number;
  winRate: number;
  pickRate: number;
  games: number;
  movement: UITierMovement;
  previousTier: UITierGrade | null;
};

type ApiTierListEntry = components["schemas"]["TierListEntry"];

export const TIER_ORDER: UITierGrade[] = ["S", "A", "B", "C", "D"];

export function decodeTierGrade(
  value: components["schemas"]["TierGrade"] | string | null | undefined
): UITierGrade | null {
  const normalized = typeof value === "string" ? value.toUpperCase() : value;

  switch (normalized) {
    case 0:
    case "S":
      return "S";
    case 1:
    case "A":
      return "A";
    case 2:
    case "B":
      return "B";
    case 3:
    case "C":
      return "C";
    case 4:
    case "D":
      return "D";
    default:
      return null;
  }
}

export function decodeTierMovement(
  value: components["schemas"]["TierMovement"] | string | null | undefined
): UITierMovement {
  const normalized = typeof value === "string" ? value.toUpperCase() : value;

  switch (normalized) {
    case 0:
    case "NEW":
      return "NEW";
    case 1:
    case "UP":
      return "UP";
    case 2:
    case "DOWN":
      return "DOWN";
    case 3:
    case "SAME":
      return "SAME";
    default:
      return "SAME";
  }
}

function asFiniteNumber(value: unknown, fallback = 0): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function asNonNegativeInteger(value: unknown, fallback = 0): number {
  const n = asFiniteNumber(value, fallback);
  return n > 0 ? Math.floor(n) : 0;
}

export function normalizeTierListEntries(
  rawEntries: ApiTierListEntry[] | null | undefined
): UITierListEntry[] {
  if (!rawEntries || rawEntries.length === 0) return [];

  const entries: UITierListEntry[] = [];

  for (const raw of rawEntries) {
    const tier = decodeTierGrade(raw.tier);
    if (!tier) continue;

    const championId = asFiniteNumber(raw.championId, Number.NaN);
    if (!Number.isInteger(championId) || championId <= 0) continue;

    entries.push({
      championId,
      role: typeof raw.role === "string" && raw.role ? raw.role : "ALL",
      tier,
      compositeScore: asFiniteNumber(raw.compositeScore, 0),
      winRate: asFiniteNumber(raw.winRate, 0),
      pickRate: asFiniteNumber(raw.pickRate, 0),
      games: asNonNegativeInteger(raw.games, 0),
      movement: decodeTierMovement(raw.movement),
      previousTier: decodeTierGrade(raw.previousTier)
    });
  }

  return entries;
}

export function movementLabel(movement: UITierMovement): string {
  switch (movement) {
    case "UP":
      return "Up";
    case "DOWN":
      return "Down";
    case "NEW":
      return "New";
    case "SAME":
    default:
      return "Same";
  }
}

export function movementClass(movement: UITierMovement): string {
  switch (movement) {
    case "UP":
      return "text-emerald-300";
    case "DOWN":
      return "text-red-300";
    case "NEW":
      return "text-primary";
    case "SAME":
    default:
      return "text-fg/70";
  }
}
