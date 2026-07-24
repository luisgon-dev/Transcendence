import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { LeaderboardFilters } from "./LeaderboardFilters";

const router = vi.hoisted(() => ({
  replace: vi.fn()
}));

vi.mock("next/navigation", () => ({
  usePathname: () => "/lol/leaderboards",
  useRouter: () => router
}));

vi.mock("@/components/ui/Select", () => ({
  Select: ({
    value,
    onValueChange,
    ariaLabel
  }: {
    value: string;
    onValueChange: (value: string) => void;
    ariaLabel: string;
  }) => (
    <button
      type="button"
      aria-label={ariaLabel}
      onClick={() => onValueChange(ariaLabel === "Champion leaderboard filter" ? "145" : "MIDDLE")}
    >
      {value}
    </button>
  )
}));

describe("LeaderboardFilters", () => {
  it("keeps the selected champion when a role is chosen before navigation settles", async () => {
    const user = userEvent.setup();
    router.replace.mockReset();
    render(
      <LeaderboardFilters
        filters={{ region: "na", queue: "solo", championId: null, role: null }}
        champions={{
          "145": { id: "Kaisa", name: "Kai'Sa", title: "the Daughter of the Void" }
        }}
      />
    );

    await user.click(screen.getByRole("button", { name: "Champion leaderboard filter" }));
    await user.click(await screen.findByRole("button", { name: "Champion role" }));

    expect(router.replace).toHaveBeenLastCalledWith(
      "/lol/leaderboards?region=na&queue=solo&championId=145&role=MIDDLE",
      { scroll: false }
    );
  });
});
