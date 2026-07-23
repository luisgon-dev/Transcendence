import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { FavoriteListClient, type FavoriteSummonerDto } from "./FavoriteListClient";

const favorites: FavoriteSummonerDto[] = [
  {
    id: "offline-id",
    summonerPuuid: "offline-puuid",
    platformRegion: "NA1",
    displayName: "Offline Player#NA1",
    createdAtUtc: "2026-07-20T12:00:00Z",
    isLive: false,
    liveState: "offline",
    liveObservedAtUtc: "2026-07-21T20:00:00Z"
  },
  {
    id: "live-id",
    summonerPuuid: "live-puuid",
    platformRegion: "KR",
    displayName: "Live Player#KR1",
    createdAtUtc: "2026-07-19T12:00:00Z",
    isLive: true,
    liveState: "in_game",
    liveGameId: "game-1",
    liveObservedAtUtc: "2026-07-21T20:01:00Z"
  }
];

describe("FavoriteListClient", () => {
  it("prioritizes live favorites and links directly into the live scout", () => {
    render(
      <FavoriteListClient initialItems={favorites} initialError={null} authenticated />
    );

    const section = screen.getByRole("region", { name: "Saved players" });
    const cards = within(section).getAllByText(/Player#/, { selector: "a" });
    expect(cards[0].textContent).toBe("Live Player#KR1");
    expect(screen.getByText("Live now")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Scout live game" }).getAttribute("href")).toBe(
      "/lol/live?region=kr&riotId=Live%20Player%23KR1"
    );
  });

  it("removes a favorite without refetching the whole page", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    render(
      <FavoriteListClient initialItems={[favorites[0]]} initialError={null} authenticated />
    );

    fireEvent.click(screen.getByRole("button", { name: "Remove" }));

    await waitFor(() => expect(screen.getByText("No saved players yet")).toBeTruthy());
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/trn/user/users/me/favorites/offline-id",
      { method: "DELETE" }
    );
  });
});
