import { describe, expect, it } from "vitest";

import { appendRsoResult, normalizeRsoMode, safeRsoReturnPath } from "./riotRso";

describe("Riot RSO routing helpers", () => {
  it("normalizes modes and rejects external or slash-confused return targets", () => {
    expect(normalizeRsoMode("link")).toBe("link");
    expect(normalizeRsoMode("anything")).toBe("login");
    expect(safeRsoReturnPath("/account/favorites", "link")).toBe("/account/favorites");
    expect(safeRsoReturnPath("//evil.example/path", "login")).toBe("/account/favorites");
    expect(safeRsoReturnPath("/\\evil.example", "login")).toBe("/account/favorites");
  });

  it("adds callback results without discarding existing query state", () => {
    expect(appendRsoResult("/account/favorites?view=compact", "riot", "linked"))
      .toBe("/account/favorites?view=compact&riot=linked");
  });
});
