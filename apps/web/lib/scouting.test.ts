import { expect, it } from "vitest";
import { scoutingLinks } from "./scouting";

it("preserves region, encoded identity and queue without accepting arbitrary URLs", () => {
  const links = scoutingLinks({ region: "euw", gameName: "A B/玩家", tagLine: "EUW", queue: "family:RANKED_SOLO_DUO" });
  expect(links.profile).toContain("/lol/summoners/euw/");
  expect(links.profile).toContain("queue=family%3ARANKED_SOLO_DUO");
  const live = new URL(links.live, "https://local.invalid");
  expect(live.searchParams.get("riotId")).toBe("A B/玩家#EUW");
  expect(live.searchParams.get("region")).toBe("euw");
  expect(live.searchParams.get("queue")).toBe("family:RANKED_SOLO_DUO");
});
