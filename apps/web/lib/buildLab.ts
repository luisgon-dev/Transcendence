export const BUILD_LAB_ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;
export const BUILD_LAB_SECTIONS = ["items", "runes", "spells"] as const;
export const BUILD_LAB_MODES = ["supported", "impact", "common"] as const;

export type BuildLabRole = (typeof BUILD_LAB_ROLES)[number];
export type BuildLabSection = (typeof BUILD_LAB_SECTIONS)[number];
export type BuildLabMode = (typeof BUILD_LAB_MODES)[number];

/**
 * Mirrors `BuildLabService.MaximumItemPath`: the service follows six legendaries, so a path of five
 * locked legendaries is the deepest one that still has a next item to show. The backend REJECTS an
 * overlong path instead of truncating it, so an overflowing lock is refused here with a message.
 */
export const BUILD_LAB_MAX_ITEM_PATH = 5;

/** Families whose choice completes the selection instead of conditioning the next decision. */
export const BUILD_LAB_TERMINAL_FAMILIES: readonly string[] = [
  "STARTER",
  "BOOTS",
  "RUNE_PAGE",
  "SPELLS"
];

export function isTerminalBuildLabFamily(family: string) {
  return BUILD_LAB_TERMINAL_FAMILIES.includes(family);
}

export type BuildLabCoverage = {
  includedPatches: string[];
  patchWeights: number[];
  countedMatches: number;
  lastCountedAtUtc?: string | null;
  includedRegions: string[];
  rankScope: string;
};

export type BuildLabContext = {
  championId: number;
  role: string;
  opponentChampionId?: number | null;
  requestedPatch?: string | null;
  requestedRegion: string;
  section: string;
  mode: string;
};

export type BuildLabOption = {
  actionKey: string;
  actionIds: number[];
  games: number;
  pickRate: number;
  winRate: number;
  /** Win rate standardized to the decision's gold-difference mix. */
  adjustedWinRate: number;
  /** Adjusted win rate minus the decision's overall win rate. */
  lift: number;
  confidenceLow: number;
  confidenceHigh: number;
  averageTimingMinutes?: number | null;
  isLowSample: boolean;
};

export type BuildLabStage = {
  family: string;
  stage: number;
  label: string;
  games: number;
  winRate: number;
  /** MATCHUP, REGION or ALL. */
  scope: string;
  /** The requested matchup or region was too thin and all games answered instead. */
  isFallback: boolean;
  options: BuildLabOption[];
};

export type BuildLabResponse = {
  available: boolean;
  context: BuildLabContext;
  coverage: BuildLabCoverage;
  selectedPath: number[];
  stages: BuildLabStage[];
  unavailableReason?: string | null;
};

export type BuildLabState = {
  role: BuildLabRole;
  opponentChampionId?: number;
  patch?: string;
  region?: string;
  section: BuildLabSection;
  mode: BuildLabMode;
  /** Locked legendaries, in order: the prefix the next item is read under. */
  itemPath: number[];
  /** A locked keystone: every later rune slot is read conditioned on it. */
  keystone?: number;
  /** Terminal picks, kept so the build summary and permalink carry them. */
  starter: number[];
  boots?: number;
  runePage: number[];
  spellPair: number[];
};

function readIds(value: string | string[] | undefined, maximum: number) {
  const raw = Array.isArray(value) ? value : value ? value.split(",") : [];
  const parsed = raw
    .flatMap((part) => String(part).split(","))
    .map(Number)
    .filter((id) => Number.isInteger(id) && id > 0);
  return { ids: parsed.slice(0, maximum), overflow: parsed.length > maximum };
}

function readId(value: string | string[] | undefined) {
  const id = Number(Array.isArray(value) ? value[0] : value);
  return Number.isInteger(id) && id > 0 ? id : undefined;
}

// Mirrors BuildLabService.NormalizeToken so a hostile patch/region degrades to the default here
// instead of provoking a 400 the page would have to render as an error.
function readToken(value: string | string[] | undefined, maximumLength: number) {
  if (typeof value !== "string") return undefined;
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > maximumLength) return undefined;
  return /^[A-Za-z0-9._-]+$/.test(trimmed) ? trimmed : undefined;
}

export type BuildLabSelectionResult = { state: BuildLabState; error?: string };

/**
 * Applies a chosen option. A legendary extends the item path and a keystone conditions the rune slots;
 * every other family records a terminal pick. An overflowing path is refused, never trimmed.
 */
export function selectBuildLabOption(
  state: BuildLabState,
  family: string,
  stage: number,
  actionIds: number[]
): BuildLabSelectionResult {
  const ids = actionIds.filter((id) => Number.isInteger(id) && id > 0);
  if (ids.length === 0) return { state };

  switch (family) {
    case "ITEM":
      if (state.itemPath.length >= BUILD_LAB_MAX_ITEM_PATH) {
        return {
          state,
          error: `Build Lab follows ${BUILD_LAB_MAX_ITEM_PATH + 1} items. Undo a locked item before locking another — nothing was discarded.`
        };
      }
      return { state: { ...state, itemPath: [...state.itemPath, ids[0]] } };
    case "RUNE":
      // Only the keystone conditions anything; a later slot is recorded on the page it belongs to.
      return stage === 1 ? { state: { ...state, keystone: ids[0] } } : { state };
    case "STARTER":
      return { state: { ...state, starter: ids } };
    case "BOOTS":
      return { state: { ...state, boots: ids[0] } };
    case "RUNE_PAGE":
      return { state: { ...state, runePage: ids, keystone: ids[0] } };
    case "SPELLS":
      return { state: { ...state, spellPair: ids.slice(0, 2) } };
    default:
      return { state };
  }
}

/** Undoes the section's most recent conditioning choice. */
export function undoLastBuildLabSelection(state: BuildLabState): BuildLabState {
  if (state.section === "items") return { ...state, itemPath: state.itemPath.slice(0, -1) };
  if (state.section === "runes") return { ...state, keystone: undefined, runePage: [] };
  return { ...state, spellPair: [] };
}

/** Clears the current section's choices only; the other sections keep theirs. */
export function clearBuildLabSelection(state: BuildLabState): BuildLabState {
  if (state.section === "items") return { ...state, itemPath: [], starter: [], boots: undefined };
  if (state.section === "runes") return { ...state, keystone: undefined, runePage: [] };
  return { ...state, spellPair: [] };
}

/** Whether the current section has anything locked or picked. */
export function hasBuildLabSelection(state: BuildLabState) {
  if (state.section === "items") {
    return state.itemPath.length > 0 || state.starter.length > 0 || state.boots != null;
  }
  if (state.section === "runes") return state.keystone != null || state.runePage.length > 0;
  return state.spellPair.length > 0;
}

export function normalizeBuildLabState(
  searchParams: Record<string, string | string[] | undefined>
): { state: BuildLabState; issues: string[] } {
  const roleValue = String(searchParams.role ?? "MIDDLE").toUpperCase();
  const role = BUILD_LAB_ROLES.includes(roleValue as BuildLabRole)
    ? (roleValue as BuildLabRole)
    : "MIDDLE";
  const sectionValue = String(searchParams.section ?? "items").toLowerCase();
  const section = BUILD_LAB_SECTIONS.includes(sectionValue as BuildLabSection)
    ? (sectionValue as BuildLabSection)
    : "items";
  const modeValue = String(searchParams.mode ?? "supported").toLowerCase();
  const mode = BUILD_LAB_MODES.includes(modeValue as BuildLabMode)
    ? (modeValue as BuildLabMode)
    : "supported";

  const itemPath = readIds(searchParams.itemPath, BUILD_LAB_MAX_ITEM_PATH);
  const issues: string[] = [];
  if (itemPath.overflow) {
    issues.push(
      `The link carried more than ${BUILD_LAB_MAX_ITEM_PATH} locked items. The extra items were not applied.`
    );
  }

  return {
    state: {
      role,
      section,
      mode,
      opponentChampionId: readId(searchParams.opponentChampionId),
      patch: readToken(searchParams.patch, 32),
      region: readToken(searchParams.region, 16)?.toUpperCase(),
      itemPath: itemPath.ids,
      keystone: readId(searchParams.keystone),
      starter: readIds(searchParams.starter, 6).ids,
      boots: readId(searchParams.boots),
      runePage: readIds(searchParams.runePage, 12).ids,
      spellPair: readIds(searchParams.spellPair, 2).ids
    },
    issues
  };
}

function contextQuery(state: BuildLabState) {
  const query = new URLSearchParams({ role: state.role, section: state.section, mode: state.mode });
  if (state.opponentChampionId) query.set("opponentChampionId", String(state.opponentChampionId));
  if (state.patch) query.set("patch", state.patch);
  if (state.region && state.region !== "ALL") query.set("region", state.region);
  for (const id of state.itemPath) query.append("itemPath", String(id));
  return query;
}

/** The complete shareable configuration — every control round-trips through this. */
export function buildLabQuery(state: BuildLabState) {
  const query = contextQuery(state);
  if (state.keystone) query.set("keystone", String(state.keystone));
  for (const id of state.starter) query.append("starter", String(id));
  if (state.boots) query.set("boots", String(state.boots));
  for (const id of state.runePage) query.append("runePage", String(id));
  for (const id of state.spellPair) query.append("spellPair", String(id));
  return query;
}

/**
 * The backend read: only the choices that condition a decision are sent. Terminal picks change
 * nothing about what the next decision looks like, so they stay client-side.
 */
export function buildLabRequestQuery(state: BuildLabState) {
  const query = contextQuery(state);
  if (state.keystone) query.append("runeSelections", String(state.keystone));
  return query;
}

export function buildLabPermalink(championId: number, state: BuildLabState) {
  return `/lol/builds/${championId}?${buildLabQuery(state).toString()}`;
}

const REGION_LABELS: Record<string, string> = {
  ALL: "All regions",
  NA1: "North America",
  EUW1: "Europe West",
  EUN1: "Europe Nordic & East",
  KR: "Korea",
  BR1: "Brazil",
  JP1: "Japan",
  TR1: "Türkiye",
  LA1: "Latin America North",
  LA2: "Latin America South",
  OC1: "Oceania"
};

export function buildLabRegionLabel(region: string | null | undefined) {
  if (!region) return REGION_LABELS.ALL;
  const normalized = region.toUpperCase();
  return REGION_LABELS[normalized] ?? normalized;
}

/**
 * Region options come from the regions the counted matches actually come from. `selected` is kept
 * even when absent so the control still shows the state it is bound to.
 */
export function buildLabRegionOptions(includedRegions: readonly string[], selected?: string | null) {
  const codes = ["ALL"];
  for (const region of [...includedRegions, selected ?? "ALL"]) {
    const normalized = (region ?? "").trim().toUpperCase();
    if (!normalized || normalized === "GLOBAL" || codes.includes(normalized)) continue;
    codes.push(normalized);
  }
  return codes.map((code) => ({ value: code, label: buildLabRegionLabel(code) }));
}

/** Sign maps to the data semantics: green is a better outcome, the muted red a worse one. */
export function liftToneClass(value: number | null | undefined, lowSample = false) {
  if (lowSample || value == null || Math.abs(value) < 0.005) return "text-fg";
  return value > 0 ? "text-success" : "text-danger";
}

export function formatLift(value?: number | null) {
  if (value == null) return "—";
  const points = value * 100;
  return `${points > 0 ? "+" : ""}${points.toFixed(1)} pp`;
}

export function formatPercent(value?: number | null, decimals = 1) {
  if (value == null) return "—";
  return `${(value * 100).toFixed(decimals)}%`;
}

export function formatCompactCount(value: number) {
  return new Intl.NumberFormat("en", {
    notation: value >= 10_000 ? "compact" : "standard",
    maximumFractionDigits: value >= 10_000 ? 1 : 0
  }).format(value);
}
