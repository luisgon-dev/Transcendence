import { describe, expect, it } from "vitest";

import { parseLobbyText } from "@/lib/multiSearch";

describe("parseLobbyText", () => {
  it("parses plain Riot IDs and lobby join messages", () => {
    expect(
      parseLobbyText("Kronic#NA1\nHide on bush#KR1 joined the lobby\nThird Player#EUW")
        .summoners
    ).toEqual([
      { gameName: "Kronic", tagLine: "NA1" },
      { gameName: "Hide on bush", tagLine: "KR1" },
      { gameName: "Third Player", tagLine: "EUW" }
    ]);
  });

  it("deduplicates case-insensitively and reports invalid lines", () => {
    const result = parseLobbyText("Kronic#NA1\nkronic#na1\nTeam 1\nMissingTag");

    expect(result.summoners).toEqual([{ gameName: "Kronic", tagLine: "NA1" }]);
    expect(result.rejected).toEqual(["Team 1", "MissingTag"]);
  });

  it("caps the request at five players", () => {
    const result = parseLobbyText(
      "One#1,Two#2,Three#3,Four#4,Five#5,Six#6"
    );

    expect(result.summoners).toHaveLength(5);
    expect(result.truncated).toBe(true);
  });
});
