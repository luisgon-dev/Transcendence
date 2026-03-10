import { describe, expect, it } from "vitest";

import {
  buildLolPublicSummonerByIdPath,
  buildLolPublicSummonerByRiotIdPath,
  buildLolPublicSummonerSearchPath
} from "@/lib/lolPublicApi";

describe("lolPublicApi", () => {
  it("builds riot-id paths with the lol prefix", () => {
    expect(buildLolPublicSummonerByRiotIdPath("kr", "Hide on bush", "KR1")).toBe(
      "/api/trn/public/lol/summoners/kr/Hide%20on%20bush/KR1"
    );
  });

  it("builds summoner-id paths with the lol prefix", () => {
    expect(buildLolPublicSummonerByIdPath("0f0e0d0c-0000-1111-2222-333344445555")).toBe(
      "/api/trn/public/lol/summoners/0f0e0d0c-0000-1111-2222-333344445555"
    );
  });

  it("builds search paths with the lol prefix", () => {
    expect(buildLolPublicSummonerSearchPath("na", "Faker", 8)).toBe(
      "/api/trn/public/lol/summoners/search?region=na&q=Faker&limit=8"
    );
  });
});
