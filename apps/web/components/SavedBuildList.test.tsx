import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { SavedBuildList } from "./SavedBuildList";
import type { SavedBuild, SavedBuildCompatibilityStatus } from "@/lib/buildLab";

const itemPayload = {
  items: {
    "3078": { name: "Trinity Force" },
    "3110": { name: "Frozen Heart" },
    "3111": { name: "Mercury's Treads" }
  }
};

function savedBuild(overrides: Partial<SavedBuild> = {}): SavedBuild {
  return {
    id: "build-1",
    name: "Ahri mid poke",
    championId: 103,
    role: "MIDDLE",
    opponentChampionId: null,
    patch: "26.14",
    region: "NA1",
    rankingMode: "SUPPORTED",
    itemPath: [3078, 3110],
    runeSelections: [8005],
    spell1Id: 4,
    spell2Id: 14,
    sourceGenerationId: "gen-1",
    currentGenerationId: "gen-1",
    analyticsChanged: false,
    compatibilityStatus: "CURRENT",
    unavailableItemIds: [],
    unavailableItems: [],
    shareId: null,
    createdAtUtc: "2026-07-20T12:00:00Z",
    updatedAtUtc: "2026-07-20T12:00:00Z",
    ...overrides
  };
}

function withStatus(id: string, name: string, status: SavedBuildCompatibilityStatus) {
  return savedBuild({ id, name, compatibilityStatus: status });
}

function applyButton(panel: HTMLElement, name: string) {
  return within(panel).getByRole("button", { name }) as HTMLButtonElement;
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" }
  });
}

describe("SavedBuildList", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      if (String(input).startsWith("/api/static/items")) return jsonResponse(itemPayload);
      return new Response(null, { status: 204 });
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("says what each compatibility status actually means", () => {
    render(
      <SavedBuildList
        authenticated
        initialBuilds={[
          withStatus("current", "Current build", "CURRENT"),
          withStatus("retired", "Retired items build", "ITEMS_RETIRED"),
          withStatus("patch", "Older patch build", "PATCH_CHANGED"),
          withStatus("nogen", "Pre-analytics build", "NO_SOURCE_GENERATION")
        ]}
      />
    );

    expect(screen.getByText("Items unavailable")).toBeTruthy();
    expect(screen.getByText("Patch changed")).toBeTruthy();
    expect(screen.getByText("Saved before analytics were published")).toBeTruthy();
    // One label for three different situations was the bug being guarded here.
    expect(screen.queryByText("Patch-incompatible")).toBeNull();
    expect(screen.queryByText("Needs review")).toBeNull();
  });

  it("keeps a pre-analytics build informational instead of dressing it as a failure", () => {
    render(
      <SavedBuildList
        authenticated
        initialBuilds={[
          withStatus("nogen", "Pre-analytics build", "NO_SOURCE_GENERATION"),
          withStatus("retired", "Retired items build", "ITEMS_RETIRED")
        ]}
      />
    );

    const informational = screen.getByText("Saved before analytics were published");
    expect(informational.className).not.toMatch(/danger|warning|primary/);
    // A genuinely broken build still encodes the bad outcome in the muted data red.
    expect(screen.getByText("Items unavailable").className).toContain("text-danger");
  });

  it("names each unavailable selection and its reason instead of showing a bare id", async () => {
    render(
      <SavedBuildList
        authenticated
        initialBuilds={[
          savedBuild({
            compatibilityStatus: "ITEMS_RETIRED",
            unavailableItemIds: [3078, 3110],
            unavailableItems: [
              { itemId: 3078, reason: "RETIRED" },
              { itemId: 3110, reason: "REMOVED_FROM_STORE" }
            ]
          })
        ]}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Repair 2 selections" }));
    const panel = await screen.findByRole("region", {
      name: "Repair unavailable selections in Ahri mid poke"
    });

    expect(await within(panel).findByText("Trinity Force")).toBeTruthy();
    expect(within(panel).getByText("Frozen Heart")).toBeTruthy();
    expect(within(panel).getByText(/Retired from the game/)).toBeTruthy();
    expect(within(panel).getByText(/No longer purchasable/)).toBeTruthy();
    expect(within(panel).queryByText("Item 3078")).toBeNull();
  });

  it("requires an explicit outcome per item and never substitutes a replacement itself", async () => {
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      if (String(input).startsWith("/api/static/items")) return jsonResponse(itemPayload);
      return jsonResponse(
        savedBuild({ itemPath: [3111, 3110], unavailableItemIds: [], unavailableItems: [] })
      );
    });
    render(
      <SavedBuildList
        authenticated
        initialBuilds={[
          savedBuild({
            compatibilityStatus: "ITEMS_RETIRED",
            unavailableItemIds: [3078],
            unavailableItems: [{ itemId: 3078, reason: "RETIRED" }]
          })
        ]}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Repair 1 selection" }));
    const panel = await screen.findByRole("region", {
      name: "Repair unavailable selections in Ahri mid poke"
    });
    expect(await within(panel).findByText("Trinity Force")).toBeTruthy();

    // Nothing is chosen for the user: neither outcome is pre-selected and applying is refused.
    const choice = within(panel).getByRole("group", {
      name: "Repair choice for Trinity Force"
    });
    for (const option of within(choice).getAllByRole("radio")) {
      expect(option.getAttribute("aria-checked")).toBe("false");
    }
    expect(applyButton(panel, "Apply repair").disabled).toBe(true);

    fireEvent.click(within(choice).getByRole("radio", { name: "Replace" }));
    expect(within(panel).getByText("Choose a replacement item to continue.")).toBeTruthy();
    expect(applyButton(panel, "Apply repair").disabled).toBe(true);

    fireEvent.change(within(panel).getByLabelText("Replacement item"), {
      target: { value: "mercury" }
    });
    fireEvent.click(within(panel).getByRole("button", { name: /Mercury's Treads/ }));

    const apply = applyButton(panel, "Apply 1 repair");
    expect(apply.disabled).toBe(false);
    fireEvent.click(apply);

    await waitFor(() =>
      expect(screen.getByRole("status").textContent).toContain("repaired with your explicit choices")
    );
    const repairCall = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith("/saved-builds/build-1/repair")
    );
    expect(repairCall).toBeTruthy();
    expect(JSON.parse(String(repairCall?.[1]?.body))).toEqual({
      choices: [{ itemId: 3078, action: "REPLACE", replacementItemId: 3111 }]
    });
  });

  it("asks for confirmation before deleting a saved build", async () => {
    render(<SavedBuildList authenticated initialBuilds={[savedBuild()]} />);

    fireEvent.click(screen.getByRole("button", { name: "Delete Ahri mid poke" }));

    expect(screen.getByText("Delete permanently?")).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Confirm delete of Ahri mid poke" }));

    await waitFor(() => expect(screen.getByText("No saved builds yet")).toBeTruthy());
    expect(fetchMock).toHaveBeenCalledWith("/api/trn/user/users/me/lol/saved-builds/build-1", {
      method: "DELETE"
    });
  });

  it("keeps the build when the confirmation is declined", () => {
    render(<SavedBuildList authenticated initialBuilds={[savedBuild()]} />);

    fireEvent.click(screen.getByRole("button", { name: "Delete Ahri mid poke" }));
    fireEvent.click(screen.getByRole("button", { name: "Keep" }));

    expect(screen.queryByText("Delete permanently?")).toBeNull();
    expect(screen.getByRole("heading", { name: "Ahri mid poke" })).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
