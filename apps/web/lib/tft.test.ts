import { describe, expect, it } from "vitest";

import {
  buildTftEntityMap,
  formatTftDescription,
  buildTftLabelMap,
  getTftCompositionEntities,
  formatTftEntityName,
  formatTftCompName,
  formatTftLabel,
  formatTftUnitLabel,
  isTftCraftableItem
} from "@/lib/tft";

describe("tft formatting", () => {
  it("builds lookup maps from static entities", () => {
    const labelMap = buildTftLabelMap([
      { apiName: "TFT16_Blacksmith", name: "Blacksmith" }
    ]);

    expect(labelMap.TFT16_Blacksmith).toBe("Blacksmith");
    expect(labelMap.Blacksmith).toBe("Blacksmith");
  });

  it("formats raw TFT identifiers when no lookup exists", () => {
    expect(formatTftLabel("TFT16_Teamup_JarvanShyvana")).toBe("Team-Up Jarvan Shyvana");
    expect(formatTftLabel("TFT16_SylasTrait")).toBe("Sylas");
  });

  it("formats comp names and drops empty legacy segments", () => {
    const traitLabels = buildTftLabelMap([
      { apiName: "TFT16_Glutton", name: "Glutton" },
      { apiName: "TFT16_Blacksmith", name: "Blacksmith" }
    ]);

    expect(
      formatTftCompName("TFT16_Glutton / TFT16_Blacksmith / /", { traitLabels })
    ).toBe("Glutton / Blacksmith");
  });

  it("formats unit labels from unit ids when display names are missing", () => {
    const unitLabels = buildTftLabelMap([
      { apiName: "TFT16_ShyvanaUnique", name: "Shyvana" }
    ]);

    expect(
      formatTftUnitLabel(
        { characterId: "TFT16_ShyvanaUnique", name: null },
        unitLabels
      )
    ).toBe("Shyvana");
  });

  it("falls back to api names when static entity names are blank or templated", () => {
    expect(
      formatTftEntityName({
        apiName: "TFT16_Item_Bilgewater_ADTier1",
        name: "+@BonusAD*100@% Attack Damage"
      })
    ).toBe("Bilgewater AD Tier 1");

    expect(
      formatTftEntityName({
        apiName: "TFT_Item_EmptyBag",
        name: ""
      })
    ).toBe("Empty Bag");
  });

  it("sanitizes unresolved TFT description markup", () => {
    expect(
      formatTftDescription(
        "Bilgewater champions gain @BonusAD*100@% Attack Damage and Ability Power.<br><br><expandRow>(@MinUnits@) @AllyAP@% for Arcanists</expandRow>"
      )
    ).toBe("Bilgewater champions gain Attack Damage and Ability Power. for Arcanists");
  });

  it("detects craftable items from community dragon composition data", () => {
    expect(
      isTftCraftableItem({
        composition: ["TFT_Item_RecurveBow", "TFT_Item_RecurveBow"]
      })
    ).toBe(true);

    expect(
      isTftCraftableItem({
        composition: []
      })
    ).toBe(false);
  });

  it("resolves item recipe entities from composition ids", () => {
    const entityMap = buildTftEntityMap([
      {
        apiName: "TFT_Item_RapidFireCannon",
        name: "Red Buff",
        composition: ["TFT_Item_RecurveBow", "TFT_Item_RecurveBow"]
      },
      { apiName: "TFT_Item_RecurveBow", name: "Recurve Bow" }
    ]);

    expect(
      getTftCompositionEntities(
        {
          composition: ["TFT_Item_RecurveBow", "TFT_Item_RecurveBow"]
        },
        entityMap
      ).map((entity) => entity.apiName)
    ).toEqual(["TFT_Item_RecurveBow", "TFT_Item_RecurveBow"]);
  });
});
