import { describe, expect, it } from "vitest";

import {
  buildTftPublicSummonerByIdPath,
  buildTftPublicSummonerByRiotIdPath,
  buildTftPublicSummonerSearchPath
} from "@/lib/tftPublicApi";

describe("tftPublicApi", () => {
  it("builds a TFT summoner path from riot id", () => {
    expect(buildTftPublicSummonerByRiotIdPath("na", "Kronic", "NA1")).toBe(
      "/api/trn/public/tft/summoners/na/Kronic/NA1"
    );
  });

  it("builds a TFT summoner path from id", () => {
    expect(buildTftPublicSummonerByIdPath("abc-123")).toBe(
      "/api/trn/public/tft/summoners/abc-123"
    );
  });

  it("builds a TFT summoner search path", () => {
    expect(buildTftPublicSummonerSearchPath("na", "Kronic", 8)).toBe(
      "/api/trn/public/tft/summoners/search?region=na&q=Kronic&limit=8"
    );
  });
});
