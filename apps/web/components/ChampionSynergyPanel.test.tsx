import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";

import { ChampionSynergyPanel } from "./ChampionSynergyPanel";

describe("ChampionSynergyPanel", () => {
  it("renders confidence-ranked partner context and preserves filters", () => {
    const html = renderToStaticMarkup(
      <ChampionSynergyPanel
        championName="Aatrox"
        version="16.14.1"
        champions={{ "64": { id: "LeeSin", name: "Lee Sin" } }}
        linkQuery="region=KR&patch=16.14"
        synergies={{
          championId: 266,
          role: "TOP",
          rankTier: "EMERALD_PLUS",
          region: "KR",
          patch: "16.14",
          queueFamily: "RANKED_SOLO_DUO",
          totalGames: 100,
          totalWins: 51,
          baselineWinRate: 0.51,
          bestPartners: [
            {
              partnerChampionId: 64,
              partnerRole: "JUNGLE",
              games: 40,
              wins: 23,
              winRate: 0.575,
              pickRate: 0.4,
              winRateDelta: 0.065,
              confidenceScore: 0.01
            }
          ]
        }}
      />
    );

    expect(html).toContain("Best jungle partners");
    expect(html).toContain("Lee Sin");
    expect(html).toContain("+6.5%");
    expect(html).toContain("region=KR&amp;patch=16.14&amp;role=JUNGLE");
    expect(html).toContain("Ranked by confidence-adjusted lift");
  });
});
