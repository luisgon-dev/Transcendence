import { describe, expect, it } from "vitest";

import {
  decodeRiotIdPath,
  encodeRiotIdPath,
  parseRiotIdInput
} from "@/lib/riotid";

describe("riotid", () => {
  it("parses input gameName#tagLine", () => {
    expect(parseRiotIdInput("Faker#KR1")).toEqual({
      gameName: "Faker",
      tagLine: "KR1"
    });
  });

  it("roundtrips encode/decode for path form", () => {
    const riotId = { gameName: "My Name", tagLine: "NA1" };
    const path = encodeRiotIdPath(riotId);
    expect(decodeRiotIdPath(path)).toEqual(riotId);
  });
});

