import { describe, expect, it } from "vitest";

import {
  BUILD_LAB_MAX_ITEM_PATH,
  buildLabPermalink,
  buildLabQuery,
  buildLabRegionOptions,
  buildLabRequestQuery,
  clearBuildLabSelection,
  formatLift,
  hasBuildLabSelection,
  liftToneClass,
  normalizeBuildLabState,
  selectBuildLabOption,
  undoLastBuildLabSelection,
  type BuildLabState
} from "@/lib/buildLab";

const completeState: BuildLabState = {
  role: "JUNGLE",
  opponentChampionId: 64,
  patch: "26.14",
  region: "NA1",
  section: "items",
  mode: "impact",
  itemPath: [6672, 3031],
  keystone: 8005,
  starter: [1101, 2003],
  boots: 3006,
  runePage: [8005, 9111, 9104],
  spellPair: [4, 11]
};

const emptyState: BuildLabState = {
  role: "MIDDLE",
  section: "items",
  mode: "supported",
  itemPath: [],
  starter: [],
  runePage: [],
  spellPair: []
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
  it("round-trips the complete shareable context", () => {
    expect(reparse(buildLabQuery(completeState))).toEqual(completeState);
    expect(buildLabPermalink(64, completeState)).toContain("/lol/builds/64?");
  });

  it("sends only the choices that condition a decision to the backend", () => {
    const query = buildLabRequestQuery(completeState);

    expect(query.getAll("itemPath")).toEqual(["6672", "3031"]);
    expect(query.getAll("runeSelections")).toEqual(["8005"]);
    for (const terminal of ["starter", "boots", "runePage", "spellPair", "keystone"]) {
      expect(query.has(terminal)).toBe(false);
    }
  });

  it("drops the default region from links", () => {
    expect(buildLabQuery({ ...emptyState, region: "ALL" }).has("region")).toBe(false);
  });

  it("refuses an overlong item path from a link and says so", () => {
    const { state, issues } = normalizeBuildLabState({
      itemPath: ["1", "2", "3", "4", "5", "6", "7"]
    });

    expect(state.itemPath).toHaveLength(BUILD_LAB_MAX_ITEM_PATH);
    expect(issues).toHaveLength(1);
  });

  it("degrades hostile tokens to defaults instead of forwarding them", () => {
    const { state } = normalizeBuildLabState({
      role: "ADC",
      section: "skins",
      mode: "vibes",
      patch: "16.19; DROP TABLE",
      region: "x".repeat(40),
      opponentChampionId: "-3"
    });

    expect(state).toMatchObject({ role: "MIDDLE", section: "items", mode: "supported" });
    expect(state.patch).toBeUndefined();
    expect(state.region).toBeUndefined();
    expect(state.opponentChampionId).toBeUndefined();
  });
});

describe("Build Lab selection", () => {
  it("locks a legendary onto the end of the path", () => {
    const { state } = selectBuildLabOption({ ...emptyState, itemPath: [6672] }, "ITEM", 2, [3031]);

    expect(state.itemPath).toEqual([6672, 3031]);
  });

  it("refuses a lock past the deepest followed item without discarding anything", () => {
    const full = { ...emptyState, itemPath: [1, 2, 3, 4, 5] };
    const result = selectBuildLabOption(full, "ITEM", 6, [6]);

    expect(result.error).toBeTruthy();
    expect(result.state).toBe(full);
  });

  it("records terminal picks without touching the conditioning path", () => {
    let state = selectBuildLabOption(emptyState, "STARTER", 0, [1055, 2003]).state;
    state = selectBuildLabOption(state, "BOOTS", 1, [3006]).state;
    state = selectBuildLabOption(state, "SPELLS", 0, [4, 14]).state;

    expect(state).toMatchObject({ starter: [1055, 2003], boots: 3006, spellPair: [4, 14], itemPath: [] });
  });

  it("conditions runes on the keystone only", () => {
    const keystone = selectBuildLabOption({ ...emptyState, section: "runes" }, "RUNE", 1, [8112]).state;
    const laterSlot = selectBuildLabOption(keystone, "RUNE", 3, [8143]).state;
    const page = selectBuildLabOption(emptyState, "RUNE_PAGE", 0, [8010, 9111, 9104]).state;

    expect(keystone.keystone).toBe(8112);
    expect(laterSlot).toBe(keystone);
    expect(page).toMatchObject({ runePage: [8010, 9111, 9104], keystone: 8010 });
  });

  it("undo and clear act on the current section only", () => {
    const items = { ...completeState, section: "items" as const };

    expect(undoLastBuildLabSelection(items).itemPath).toEqual([6672]);
    expect(clearBuildLabSelection(items)).toMatchObject({
      itemPath: [],
      starter: [],
      boots: undefined,
      runePage: completeState.runePage,
      spellPair: completeState.spellPair
    });
    expect(clearBuildLabSelection({ ...completeState, section: "runes" })).toMatchObject({
      keystone: undefined,
      runePage: [],
      itemPath: completeState.itemPath
    });
  });

  it("knows whether the section has anything to undo", () => {
    expect(hasBuildLabSelection(emptyState)).toBe(false);
    expect(hasBuildLabSelection({ ...emptyState, boots: 3006 })).toBe(true);
    expect(hasBuildLabSelection({ ...emptyState, section: "spells" })).toBe(false);
  });
});

describe("Build Lab formatting", () => {
  it("offers the counted regions after the all-regions default", () => {
    expect(buildLabRegionOptions(["NA1", "KR"], "EUW1").map((option) => option.value)).toEqual([
      "ALL",
      "NA1",
      "KR",
      "EUW1"
    ]);
  });

  it("colours lift by sign, except when the sample is too thin to mean it", () => {
    expect(liftToneClass(0.02)).toBe("text-success");
    expect(liftToneClass(-0.02)).toBe("text-danger");
    expect(liftToneClass(0.002)).toBe("text-fg");
    expect(liftToneClass(0.05, true)).toBe("text-fg");
    expect(formatLift(0.0123)).toBe("+1.2 pp");
    expect(formatLift(-0.004)).toBe("-0.4 pp");
  });
});
