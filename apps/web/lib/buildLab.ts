export const BUILD_LAB_ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;
export const BUILD_LAB_SECTIONS = ["items", "runes", "spells"] as const;
export const BUILD_LAB_MODES = ["supported", "impact", "common"] as const;

export type BuildLabRole = (typeof BUILD_LAB_ROLES)[number];
export type BuildLabSection = (typeof BUILD_LAB_SECTIONS)[number];
export type BuildLabMode = (typeof BUILD_LAB_MODES)[number];

/**
 * Mirrors `BuildLabService.MaximumItemPath`: a starter set plus boots plus six legendary slots,
 * with room for multi-piece starters. The backend REJECTS an overlong path instead of truncating
 * it, so a selection that would overflow is refused with a message — never trimmed from the tail.
 */
export const BUILD_LAB_MAX_ITEM_PATH = 12;
export const BUILD_LAB_MAX_RUNE_SELECTIONS = 12;
export const BUILD_LAB_MAX_SPELL_PAIR = 2;

/**
 * Families whose candidate already IS the complete selection. The modeler only ever writes them
 * against an empty path prefix, so feeding one back as a conditioning prefix hashes to a prefix
 * that was never modeled. They are recorded as terminal choices instead of extending the path.
 */
export const BUILD_LAB_TERMINAL_FAMILIES: readonly string[] = ["RUNE_PAGE", "SPELL"];

export function isTerminalBuildLabFamily(family: string) {
  return BUILD_LAB_TERMINAL_FAMILIES.includes(family);
}

export type BuildLabProvenance = {
  generationId?: string | null;
  datasetVersion: string;
  modelVersion: string;
  staticDataVersion: string;
  sourceCutoffUtc?: string | null;
  generatedAtUtc?: string | null;
  matchCount: number;
  rankScope: string;
  includedPatches: string[];
  includedRegions: string[];
};

export type BuildLabContext = {
  championId: number;
  role: string;
  opponentChampionId?: number | null;
  requestedPatch: string;
  effectivePatch: string;
  requestedRegion: string;
  effectiveRegion: string;
  section: string;
  mode: string;
};

export type EvidenceTier = "NUMERIC" | "BUCKETED" | "DESCRIPTIVE";
export type EvidenceBucket = "ABOVE_AVERAGE" | "TYPICAL" | "BELOW_AVERAGE";

export const EVIDENCE_BUCKET_LABEL: Record<EvidenceBucket, string> = {
  ABOVE_AVERAGE: "Above average",
  TYPICAL: "Typical",
  BELOW_AVERAGE: "Below average"
};

export type AdjustedActionEstimate = {
  actionKey: string;
  actionIds: number[];
  adjustedWpa?: number | null;
  confidenceLow?: number | null;
  confidenceHigh?: number | null;
  /** Withheld (null) for a cell that failed the publication gates. */
  rawWinRate?: number | null;
  /** Withheld (null) for a cell that failed the publication gates. */
  pickRate?: number | null;
  observedCount: number;
  effectiveSampleSize: number;
  averageTimingMinutes?: number | null;
  evidenceQuality: string;
  /** NONE when the cell is in the requested region, GLOBAL_FALLBACK when it substituted. */
  fallbackScope: string;
  /** The region the cell was actually estimated in (GLOBAL for the pooled baseline). */
  regionScope: string;
  /** The comparison set the lift is measured against — disclosed, never implied. */
  baselineDefinition: string;
  /**
   * How much of the estimate may be shown. A fortnightly patch rarely earns a <=3pp interval in
   * time, so a cell that cannot support a number can still support a direction.
   */
  evidenceTier: EvidenceTier;
  /** Only meaningful at the BUCKETED tier. */
  evidenceBucket?: EvidenceBucket | null;
  isPublishable: boolean;
  unavailableReason?: string | null;
};

export type BuildLabStage = {
  family: string;
  stage: number;
  label: string;
  candidates: AdjustedActionEstimate[];
};

export type BuildLabPathEstimate = {
  itemPath: number[];
  estimatedWinProbability?: number | null;
  adjustedLift?: number | null;
  confidenceLow?: number | null;
  confidenceHigh?: number | null;
  observedCount: number;
  effectiveSampleSize: number;
  isPublishable: boolean;
  unavailableReason?: string | null;
};

export type BuildLabResponse = {
  available: boolean;
  context: BuildLabContext;
  provenance: BuildLabProvenance;
  selectedPath: number[];
  pathEstimate?: BuildLabPathEstimate | null;
  stages: BuildLabStage[];
  unavailableReason?: string | null;
};

export const SAVED_BUILD_COMPATIBILITY_STATUSES = [
  "CURRENT",
  "PATCH_CHANGED",
  "ITEMS_RETIRED",
  "NO_SOURCE_GENERATION"
] as const;

export type SavedBuildCompatibilityStatus =
  (typeof SAVED_BUILD_COMPATIBILITY_STATUSES)[number];

/** RETIRED: absent from the active patch. REMOVED_FROM_STORE: present but unbuyable. */
export type SavedBuildUnavailableItem = {
  itemId: number;
  reason: "RETIRED" | "REMOVED_FROM_STORE";
};

export type SavedBuild = {
  id: string;
  name: string;
  championId: number;
  role: string;
  opponentChampionId?: number | null;
  patch: string;
  region: string;
  rankingMode: string;
  itemPath: number[];
  runeSelections: number[];
  spell1Id?: number | null;
  spell2Id?: number | null;
  sourceGenerationId?: string | null;
  currentGenerationId?: string | null;
  analyticsChanged: boolean;
  compatibilityStatus: SavedBuildCompatibilityStatus;
  unavailableItemIds: number[];
  unavailableItems: SavedBuildUnavailableItem[];
  shareId?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type SavedBuildList = {
  items: SavedBuild[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
};

/** REPLACE requires a replacementItemId that is valid on the active patch. */
export type SavedBuildRepairChoice = {
  itemId: number;
  action: "DROP" | "REPLACE";
  replacementItemId?: number;
};

export type PublicSavedBuild = Omit<SavedBuild, "id" | "shareId" | "createdAtUtc">;

export type BuildLabState = {
  role: BuildLabRole;
  opponentChampionId?: number;
  patch?: string;
  region?: string;
  section: BuildLabSection;
  mode: BuildLabMode;
  itemPath: number[];
  /**
   * Size of each locked item group, in order. A STARTER lock is one group of several ids, so undo
   * has to pop a whole group — popping a single id leaves a prefix the modeler never hashed.
   * Absent means "every id was locked on its own".
   */
  itemLocks?: number[];
  runeSelections: number[];
  /** A complete RUNE_PAGE choice: a terminal selection, never a conditioning prefix. */
  runePage?: number[];
  spellPair: number[];
};

function readIds(
  value: string | string[] | undefined,
  maximum: number
): { ids: number[]; overflow: boolean } {
  const raw = Array.isArray(value) ? value : value ? value.split(",") : [];
  const parsed = raw
    .flatMap((part) => String(part).split(","))
    .map(Number)
    .filter((id) => Number.isInteger(id) && id > 0);
  return { ids: parsed.slice(0, maximum), overflow: parsed.length > maximum };
}

// Mirrors BuildLabService.NormalizeToken so a hostile patch/region degrades to the default here
// instead of provoking a 400 the page would have to render as an error.
function readToken(value: string | string[] | undefined, maximumLength: number) {
  if (typeof value !== "string") return undefined;
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > maximumLength) return undefined;
  return /^[A-Za-z0-9._-]+$/.test(trimmed) ? trimmed : undefined;
}

function singletonLocks(ids: number[]) {
  return ids.map(() => 1);
}

/** Splits the ordered item path back into the groups the user actually locked. */
export function itemLockGroups(state: BuildLabState): number[][] {
  const sizes =
    state.itemLocks && state.itemLocks.length > 0
      ? state.itemLocks
      : singletonLocks(state.itemPath);
  const groups: number[][] = [];
  let cursor = 0;
  for (const size of sizes) {
    if (cursor >= state.itemPath.length) break;
    groups.push(state.itemPath.slice(cursor, cursor + size));
    cursor += size;
  }
  // A truncated or mismatched lock map must not hide selected ids.
  if (cursor < state.itemPath.length) {
    for (const id of state.itemPath.slice(cursor)) groups.push([id]);
  }
  return groups;
}

/** The current section's selection, grouped the way it was locked. */
export function buildLabSelectionGroups(state: BuildLabState): number[][] {
  if (state.section === "items") return itemLockGroups(state);
  if (state.section === "runes") {
    return state.runePage && state.runePage.length > 0
      ? [state.runePage]
      : state.runeSelections.map((id) => [id]);
  }
  return state.spellPair.length > 0 ? [state.spellPair] : [];
}

export type BuildLabSelectionResult = { state: BuildLabState; error?: string };

/**
 * Applies a candidate to the lab state. Terminal families replace the section's selection instead
 * of extending the conditioning prefix; an overflowing selection is refused, never trimmed.
 */
export function selectBuildLabCandidate(
  state: BuildLabState,
  family: string,
  actionIds: number[]
): BuildLabSelectionResult {
  const ids = actionIds.filter((id) => Number.isInteger(id) && id > 0);
  if (ids.length === 0) return { state };

  if (family === "RUNE_PAGE") {
    return { state: { ...state, runePage: ids, runeSelections: [] } };
  }
  if (family === "SPELL") {
    return { state: { ...state, spellPair: ids.slice(0, BUILD_LAB_MAX_SPELL_PAIR) } };
  }

  if (state.section === "items") {
    if (state.itemPath.length + ids.length > BUILD_LAB_MAX_ITEM_PATH) {
      return {
        state,
        error: `A conditioned path holds at most ${BUILD_LAB_MAX_ITEM_PATH} items. Undo a locked step before locking this one — nothing was discarded.`
      };
    }
    return {
      state: {
        ...state,
        itemPath: [...state.itemPath, ...ids],
        itemLocks: [...itemLockGroups(state).map((group) => group.length), ids.length]
      }
    };
  }

  if (state.section === "runes") {
    if (state.runeSelections.length + ids.length > BUILD_LAB_MAX_RUNE_SELECTIONS) {
      return {
        state,
        error: `A rune prefix holds at most ${BUILD_LAB_MAX_RUNE_SELECTIONS} selections. Undo a step before locking this one — nothing was discarded.`
      };
    }
    return {
      state: { ...state, runeSelections: [...state.runeSelections, ...ids], runePage: [] }
    };
  }

  return { state: { ...state, spellPair: ids.slice(0, BUILD_LAB_MAX_SPELL_PAIR) } };
}

/** Undoes the last whole selection (a composite lock leaves together, not one id at a time). */
export function undoLastBuildLabSelection(state: BuildLabState): BuildLabState {
  if (state.section === "items") {
    const groups = itemLockGroups(state);
    if (groups.length === 0) return state;
    const kept = groups.slice(0, -1);
    return {
      ...state,
      itemPath: kept.flat(),
      itemLocks: kept.map((group) => group.length)
    };
  }
  if (state.section === "runes") {
    if (state.runePage && state.runePage.length > 0) return { ...state, runePage: [] };
    return { ...state, runeSelections: state.runeSelections.slice(0, -1) };
  }
  return { ...state, spellPair: [] };
}

/** Clears the current section's selection only; the other sections keep their configuration. */
export function clearBuildLabSelection(state: BuildLabState): BuildLabState {
  if (state.section === "items") return { ...state, itemPath: [], itemLocks: [] };
  if (state.section === "runes") return { ...state, runeSelections: [], runePage: [] };
  return { ...state, spellPair: [] };
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
  const opponent = Number(searchParams.opponentChampionId);

  const itemPath = readIds(searchParams.itemPath, BUILD_LAB_MAX_ITEM_PATH);
  const runeSelections = readIds(searchParams.runeSelections, BUILD_LAB_MAX_RUNE_SELECTIONS);
  const runePage = readIds(searchParams.runePage, BUILD_LAB_MAX_RUNE_SELECTIONS);
  const spellPair = readIds(searchParams.spellPair, BUILD_LAB_MAX_SPELL_PAIR);
  const lockSizes = readIds(searchParams.itemLocks, BUILD_LAB_MAX_ITEM_PATH).ids;
  const locksMatch =
    lockSizes.length > 0 &&
    lockSizes.reduce((total, size) => total + size, 0) === itemPath.ids.length;

  const issues: string[] = [];
  if (itemPath.overflow) {
    issues.push(
      `The link carried more than ${BUILD_LAB_MAX_ITEM_PATH} item selections, which the model cannot condition on. The extra selections were not applied.`
    );
  }
  if (runeSelections.overflow || runePage.overflow) {
    issues.push(
      `The link carried more than ${BUILD_LAB_MAX_RUNE_SELECTIONS} rune selections. The extra selections were not applied.`
    );
  }
  if (spellPair.overflow) {
    issues.push("The link carried more than two summoner spells. Only the first two were applied.");
  }

  return {
    state: {
      role,
      section,
      mode,
      opponentChampionId: Number.isInteger(opponent) && opponent > 0 ? opponent : undefined,
      patch: readToken(searchParams.patch, 32),
      region: readToken(searchParams.region, 16)?.toUpperCase(),
      itemPath: itemPath.ids,
      itemLocks: locksMatch ? lockSizes : singletonLocks(itemPath.ids),
      runeSelections: runeSelections.ids,
      runePage: runePage.ids,
      spellPair: spellPair.ids
    },
    issues
  };
}

/** The complete shareable configuration — every control round-trips through this. */
export function buildLabQuery(state: BuildLabState) {
  const query = new URLSearchParams({
    role: state.role,
    section: state.section,
    mode: state.mode
  });
  if (state.opponentChampionId) {
    query.set("opponentChampionId", String(state.opponentChampionId));
  }
  if (state.patch) query.set("patch", state.patch);
  if (state.region && state.region !== "GLOBAL") query.set("region", state.region);
  for (const id of state.itemPath) query.append("itemPath", String(id));
  const groups = itemLockGroups(state);
  // Only composite locks need the map; single-id locks are the default reading.
  if (groups.some((group) => group.length > 1)) {
    for (const group of groups) query.append("itemLocks", String(group.length));
  }
  for (const id of state.runeSelections) query.append("runeSelections", String(id));
  for (const id of state.runePage ?? []) query.append("runePage", String(id));
  for (const id of state.spellPair) query.append("spellPair", String(id));
  return query;
}

/**
 * The conditioning read. A complete rune page and a summoner-spell pair are terminal choices the
 * modeler wrote against an empty prefix, so they must not be sent as a path prefix — doing so
 * hashes to a prefix that does not exist and empties the board.
 */
export function buildLabRequestQuery(state: BuildLabState) {
  const query = new URLSearchParams({
    role: state.role,
    section: state.section,
    mode: state.mode
  });
  if (state.opponentChampionId) {
    query.set("opponentChampionId", String(state.opponentChampionId));
  }
  if (state.patch) query.set("patch", state.patch);
  if (state.region && state.region !== "GLOBAL") query.set("region", state.region);
  for (const id of state.itemPath) query.append("itemPath", String(id));
  for (const id of state.runeSelections) query.append("runeSelections", String(id));
  return query;
}

export function buildLabPermalink(championId: number, state: BuildLabState) {
  return `/lol/builds/${championId}?${buildLabQuery(state).toString()}`;
}

/** The rune ids a save should persist: a terminal page if one was chosen, else the staged prefix. */
export function savedRuneSelections(state: BuildLabState) {
  return state.runePage && state.runePage.length > 0 ? state.runePage : state.runeSelections;
}

// Build Lab names its pooled scope GLOBAL (the analytics filters call theirs ALL), so the labels
// live here rather than borrowing the region-filter map.
const REGION_LABELS: Record<string, string> = {
  GLOBAL: "Global baseline",
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
  if (!region) return REGION_LABELS.GLOBAL;
  const normalized = region.toUpperCase();
  return REGION_LABELS[normalized] ?? normalized;
}

/**
 * Region options come from the promoted generation's own `includedRegions` — a hardcoded list
 * would offer scopes the generation never modeled. `selected` is kept even when the generation
 * does not list it so the control still shows the state it is bound to.
 */
export function buildLabRegionOptions(
  includedRegions: readonly string[],
  selected?: string | null
) {
  const codes = ["GLOBAL"];
  for (const region of [...includedRegions, selected ?? "GLOBAL"]) {
    const normalized = (region ?? "").trim().toUpperCase();
    if (!normalized || normalized === "ALL" || codes.includes(normalized)) continue;
    codes.push(normalized);
  }
  return codes.map((code) => ({ value: code, label: buildLabRegionLabel(code) }));
}

/** Sign maps to the data semantics: green is a better outcome, the muted red a worse one. */
export function wpaToneClass(value?: number | null) {
  if (value == null || value === 0) return "text-fg";
  return value > 0 ? "text-success" : "text-danger";
}

/** Same win/loss semantics as a numeric lift, so a direction reads the same way a number would. */
export function bucketToneClass(bucket?: EvidenceBucket | null) {
  if (bucket === "ABOVE_AVERAGE") return "text-success";
  if (bucket === "BELOW_AVERAGE") return "text-danger";
  return "text-fg";
}

export function bucketLabel(bucket?: EvidenceBucket | null) {
  return bucket ? EVIDENCE_BUCKET_LABEL[bucket] : "Insufficient evidence";
}

export function humanizeToken(value: string | null | undefined) {
  if (!value) return "—";
  return value.toLowerCase().replaceAll("_", " ");
}

export function formatWpa(value?: number | null) {
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
    maximumFractionDigits: 1
  }).format(value);
}
