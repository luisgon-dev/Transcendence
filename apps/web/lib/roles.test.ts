import { describe, expect, it } from "vitest";

import { roleDisplayLabel } from "@/lib/roles";

describe("roleDisplayLabel", () => {
  it("returns title-case labels for known role tokens", () => {
    expect(roleDisplayLabel("ALL")).toBe("All");
    expect(roleDisplayLabel("TOP")).toBe("Top");
    expect(roleDisplayLabel("JUNGLE")).toBe("Jungle");
    expect(roleDisplayLabel("MIDDLE")).toBe("Middle");
    expect(roleDisplayLabel("BOTTOM")).toBe("Bottom");
    expect(roleDisplayLabel("UTILITY")).toBe("Support");
    expect(roleDisplayLabel("support")).toBe("Support");
  });

  it("handles unknown/empty roles safely", () => {
    expect(roleDisplayLabel("")).toBe("Unknown");
    expect(roleDisplayLabel(undefined)).toBe("Unknown");
    expect(roleDisplayLabel("foo")).toBe("Unknown");
  });
});
