import { describe, expect, it } from "vitest";

import { parseItemDescription } from "@/lib/itemDescription";

describe("parseItemDescription", () => {
  it("splits stat lines and named effects out of Data Dragon markup", () => {
    const markup =
      "<mainText><stats><attention>105</attention> Ability Power<br><attention>50</attention> Armor</stats><br><br>" +
      "<passive>Quicken</passive><br>Attacking grants <speed>20 Move Speed</speed>.<br> <br>" +
      "<active>Time Stop</active><br>Enter <keyword>Stasis</keyword> for 2.5 seconds.<rules>Small print.</rules></mainText>";

    expect(parseItemDescription(markup)).toEqual({
      stats: ["105 Ability Power", "50 Armor"],
      effects: [
        { kind: "passive", name: "Quicken", text: "Attacking grants 20 Move Speed." },
        { kind: "active", name: "Time Stop", text: "Enter Stasis for 2.5 seconds." }
      ]
    });
  });

  it("returns nothing for a missing or stat-less description", () => {
    expect(parseItemDescription(undefined)).toEqual({ stats: [], effects: [] });
    expect(parseItemDescription("<mainText></mainText>")).toEqual({ stats: [], effects: [] });
  });
});
