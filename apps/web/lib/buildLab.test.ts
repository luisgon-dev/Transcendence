import { describe, expect, it } from "vitest";

import {
  BUILD_LAB_MAX_ITEM_PATH,
  buildLabPermalink,
  buildLabQuery,
  buildLabRegionOptions,
  buildLabRequestQuery,
  buildLabSelectionGroups,
  clearBuildLabSelection,
  itemLockGroups,
  normalizeBuildLabState,
  savedRuneSelections,
  selectBuildLabCandidate,
  undoLastBuildLabSelection,
  wpaToneClass,
  type BuildLabState
} from "@/lib/buildLab";

const completeState: BuildLabState = {
  role: "JUNGLE",
  opponentChampionId: 64,
  patch: "26.14",
  region: "NA1",
  section: "runes",
  mode: "impact",
  itemPath: [1101, 2003, 3006, 6672],
  itemLocks: [2, 1, 1],
  runeSelections: [8005, 9111, 9104, 8014],
  runePage: [],
  spellPair: [4, 11]
};

function reparse(query: URLSearchParams) {
  return normalizeBuildLabState(
    Object.fromEntries(
      [...new Set(query.keys())].map((key) => {
        const values = query.getAll(key);
        return [key, values.length === 1 ? values[0] : values];
      })
    )
  ).state;
}

describe("Build Lab URL state", () => {
  it("round-trips the complete shareable context, including composite lock granularity", () => {
    const state = reparse(buildLabQuery(completeState));

    expect(state).toEqual(completeState);
    expect(itemLockGroups(state)).toEqual([[1101, 2003], [3006], [6672]]);
    expect(buildLabPermalink(64, completeState)).toContain("/lol/builds/64?");
  });

  it("round-trips a terminal rune page separately from a staged rune prefix", () => {
    const state = reparse(
      buildLabQuery({ ...completeState, runeSelections: [], runePage: [8005, 9111, 9104] })
    );

    expect(state.runePage).toEqual([8005, 9111, 9104]);
    expect(state.runeSelections).toEqual([]);
    expect(savedRuneSelections(state)).toEqual([8005, 9111, 9104]);
  });

  it("never sends a terminal selection to the backend as a conditioning prefix", () => {
    const query = buildLabRequestQuery({
      ...completeState,
      section: "spells",
      runeSelections: [],
      runePage: [8005, 9111],
      spellPair: [4, 11]
    });

    expect(query.getAll("spellPair")).toEqual([]);
    expect(query.getAll("runeSelections")).toEqual([]);
    expect(query.has("runePage")).toBe(false);
    expect(query.has("itemLocks")).toBe(false);
    expect(query.getAll("itemPath")).toEqual(["1101", "2003", "3006", "6672"]);
  });

  it("uses safe defaults and drops invalid identifiers", () => {
    const { state } = normalizeBuildLabState({
      role: "carry",
      section: "unknown",
      mode: "causal",
      opponentChampionId: "-1",
      itemPath: ["6672", "oops", "0", "3006"],
      spellPair: "4,not-an-id,12"
    });

    expect(state).toMatchObject({
      role: "MIDDLE",
      section: "items",
      mode: "supported",
      itemPath: [6672, 3006],
      spellPair: [4, 12]
    });
    expect(state.opponentChampionId).toBeUndefined();
  });

  it("degrades hostile patch, region, and repeated scalar params instead of forwarding them", () => {
    const { state } = normalizeBuildLabState({
      role: ["MIDDLE", "TOP"],
      patch: "26.14'; DROP TABLE",
      region: "na1<script>",
      opponentChampionId: ["7", "8"]
    });

    expect(state.role).toBe("MIDDLE");
    expect(state.patch).toBeUndefined();
    expect(state.region).toBeUndefined();
    expect(state.opponentChampionId).toBeUndefined();
  });

  it("rejects an over-long patch token the analytics columns could not store", () => {
    const { state } = normalizeBuildLabState({ role: "TOP", patch: "1".repeat(33) });

    expect(state.patch).toBeUndefined();
  });

  it("reports, rather than silently trims, a link carrying more selections than the model accepts", () => {
    const overflowing = Array.from({ length: BUILD_LAB_MAX_ITEM_PATH + 3 }, (_, index) =>
      String(1000 + index)
    );
    const { state, issues } = normalizeBuildLabState({ role: "TOP", itemPath: overflowing });

    expect(state.itemPath).toHaveLength(BUILD_LAB_MAX_ITEM_PATH);
    expect(issues).toHaveLength(1);
    expect(issues[0]).toContain(String(BUILD_LAB_MAX_ITEM_PATH));
  });

  it("falls back to single-id groups when the lock map does not describe the path", () => {
    const { state } = normalizeBuildLabState({
      role: "TOP",
      itemPath: ["1101", "2003", "3006"],
      itemLocks: ["9"]
    });

    expect(itemLockGroups(state)).toEqual([[1101], [2003], [3006]]);
  });

  it("does not encode the pooled global region as an unnecessary override", () => {
    const query = buildLabQuery({ ...completeState, region: "GLOBAL" });

    expect(query.has("region")).toBe(false);
  });

  it("round-trips every control through the permalink a user would actually paste", () => {
    const url = new URL(buildLabPermalink(103, completeState), "https://transcend.kronic.one");

    expect(url.pathname).toBe("/lol/builds/103");
    expect(url.searchParams.get("role")).toBe("JUNGLE");
    expect(url.searchParams.get("opponentChampionId")).toBe("64");
    expect(url.searchParams.get("patch")).toBe("26.14");
    expect(url.searchParams.get("region")).toBe("NA1");
    expect(url.searchParams.get("section")).toBe("runes");
    expect(url.searchParams.get("mode")).toBe("impact");
    expect(reparse(url.searchParams)).toEqual(completeState);
  });

  it("preserves item-path order, because the path is a prefix and not a set", () => {
    const ordered: BuildLabState = { ...completeState, itemPath: [3006, 1101, 3006, 6672] };
    const query = buildLabQuery(ordered);

    expect(query.getAll("itemPath")).toEqual(["3006", "1101", "3006", "6672"]);
    // A repeated component and a descending id order both survive: sorting or de-duplicating
    // would hash to a prefix the modeler never wrote.
    expect(reparse(query).itemPath).toEqual([3006, 1101, 3006, 6672]);
  });

  it("reads a comma-packed and a repeated item param into one ordered path", () => {
    const { state } = normalizeBuildLabState({
      role: "TOP",
      itemPath: ["1101,2003", "3006", "6672"]
    });

    expect(state.itemPath).toEqual([1101, 2003, 3006, 6672]);
  });

  it("keeps a hostile selection payload from being read as identifiers", () => {
    const { state } = normalizeBuildLabState({
      role: "TOP",
      itemPath: ["3006<script>", "-3006", "0", "NaN", " 6672 ", "3006"],
      runeSelections: "8005'); DROP TABLE,9111",
      spellPair: ["../../etc/passwd"]
    });

    expect(state.itemPath).toEqual([6672, 3006]);
    expect(state.runeSelections).toEqual([9111]);
    expect(state.spellPair).toEqual([]);
  });

  it("survives an absurdly long link without throwing", () => {
    const { state, issues } = normalizeBuildLabState({
      role: "TOP",
      itemPath: Array.from({ length: 5_000 }, (_, index) => String(1000 + index)),
      runeSelections: Array.from({ length: 5_000 }, () => "8005"),
      spellPair: Array.from({ length: 5_000 }, () => "4")
    });

    expect(state.itemPath).toHaveLength(BUILD_LAB_MAX_ITEM_PATH);
    expect(state.spellPair).toEqual([4, 4]);
    expect(issues).toHaveLength(3);
  });
});

describe("Build Lab selection", () => {
  const itemsState: BuildLabState = {
    role: "MIDDLE",
    section: "items",
    mode: "supported",
    itemPath: [],
    itemLocks: [],
    runeSelections: [],
    runePage: [],
    spellPair: []
  };

  it("undoes a whole composite lock so the remaining prefix is one the model hashed", () => {
    const starter = selectBuildLabCandidate(itemsState, "STARTER", [1055, 2003]);
    const boots = selectBuildLabCandidate(starter.state, "BOOTS", [3006]);

    expect(boots.state.itemPath).toEqual([1055, 2003, 3006]);
    expect(undoLastBuildLabSelection(boots.state).itemPath).toEqual([1055, 2003]);
    // The starter set leaves together: popping one id would strand a prefix with no stored hash.
    expect(undoLastBuildLabSelection(undoLastBuildLabSelection(boots.state)).itemPath).toEqual([]);
  });

  it("refuses a selection that would overflow the item path instead of discarding ids", () => {
    const full: BuildLabState = {
      ...itemsState,
      itemPath: Array.from({ length: BUILD_LAB_MAX_ITEM_PATH - 1 }, (_, index) => 1000 + index)
    };
    const result = selectBuildLabCandidate(full, "ITEM", [6672, 3153]);

    expect(result.error).toContain(String(BUILD_LAB_MAX_ITEM_PATH));
    expect(result.state.itemPath).toEqual(full.itemPath);
  });

  it("accepts a selection that exactly fills the path and refuses the one after it", () => {
    expect(BUILD_LAB_MAX_ITEM_PATH).toBe(12);
    const nearlyFull: BuildLabState = {
      ...itemsState,
      itemPath: Array.from({ length: BUILD_LAB_MAX_ITEM_PATH - 2 }, (_, index) => 1000 + index)
    };

    const filled = selectBuildLabCandidate(nearlyFull, "STARTER", [1055, 2003]);
    expect(filled.error).toBeUndefined();
    expect(filled.state.itemPath).toHaveLength(BUILD_LAB_MAX_ITEM_PATH);

    const refused = selectBuildLabCandidate(filled.state, "ITEM", [3006]);
    // Refused whole, not tail-trimmed: the previous selection is returned untouched.
    expect(refused.state).toBe(filled.state);
    expect(refused.error).toContain(String(BUILD_LAB_MAX_ITEM_PATH));
  });

  it("records a terminal rune page as a complete selection and clears the staged prefix", () => {
    const staged = selectBuildLabCandidate(
      { ...itemsState, section: "runes" },
      "RUNE",
      [8005]
    ).state;
    const page = selectBuildLabCandidate(staged, "RUNE_PAGE", [8005, 9111, 9104]).state;

    expect(page.runeSelections).toEqual([]);
    expect(page.runePage).toEqual([8005, 9111, 9104]);
    expect(buildLabSelectionGroups(page)).toEqual([[8005, 9111, 9104]]);
    expect(undoLastBuildLabSelection(page).runePage).toEqual([]);
  });

  it("replaces the spell pair rather than appending to it", () => {
    const first = selectBuildLabCandidate({ ...itemsState, section: "spells" }, "SPELL", [4, 11]);
    const second = selectBuildLabCandidate(first.state, "SPELL", [4, 14]);

    expect(second.state.spellPair).toEqual([4, 14]);
    expect(clearBuildLabSelection(second.state).spellPair).toEqual([]);
  });

  it("clears only the section being edited", () => {
    const cleared = clearBuildLabSelection({ ...completeState, section: "items" });

    expect(cleared.itemPath).toEqual([]);
    expect(cleared.runeSelections).toEqual(completeState.runeSelections);
    expect(cleared.spellPair).toEqual(completeState.spellPair);
  });
});

describe("Build Lab presentation", () => {
  it("drives region options from the generation's own included regions", () => {
    const options = buildLabRegionOptions(["NA1", "KR", "GLOBAL"], "EUW1");

    expect(options.map((option) => option.value)).toEqual(["GLOBAL", "NA1", "KR", "EUW1"]);
    expect(options[0].label).toBe("Global baseline");
  });

  it("maps the sign of an estimate to the matching data semantics", () => {
    expect(wpaToneClass(0.012)).toBe("text-success");
    expect(wpaToneClass(-0.012)).toBe("text-danger");
    expect(wpaToneClass(null)).toBe("text-fg");
  });
});
