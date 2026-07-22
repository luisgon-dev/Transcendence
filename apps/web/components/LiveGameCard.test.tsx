import { act, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { LiveGameCard } from "./LiveGameCard";

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "content-type": "application/json" }
  });
}

describe("LiveGameCard", () => {
  afterEach(() => vi.useRealTimers());

  it("checks automatically, shows a loading skeleton, and stamps freshness", async () => {
    let resolveRequest: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ status: "queued", retryAfterSeconds: 0 }))
      .mockImplementationOnce(
        () =>
        new Promise<Response>((resolve) => {
          resolveRequest = resolve;
        })
      );
    vi.stubGlobal("fetch", fetchMock);

    render(<LiveGameCard region="na" gameName="Kronic" tagLine="NA1" />);

    expect(screen.getByLabelText("Checking live game")).toBeTruthy();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/trn/app/lol/summoners/na/Kronic/NA1/live-game/probe",
      expect.objectContaining({ cache: "no-store", method: "POST" })
    );

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    await act(async () => {
      resolveRequest?.(jsonResponse({
        state: "NOT_IN_PROGRESS",
        participants: [],
        lastUpdatedUtc: new Date().toISOString()
      }));
    });

    expect(await screen.findByText("Not currently in a game.")).toBeTruthy();
    expect(screen.getByText(/^Checked /)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Re-check" })).toBeTruthy();
  });

  it("refreshes detected games on the light one-minute cadence", async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn(async (input: RequestInfo | URL) =>
      String(input).endsWith("/probe")
        ? jsonResponse({ status: "queued", retryAfterSeconds: 0 })
        : jsonResponse({ state: "IN_PROGRESS", participants: [], gameLengthSeconds: 120, lastUpdatedUtc: new Date().toISOString() })
    );
    vi.stubGlobal("fetch", fetchMock);

    render(<LiveGameCard region="na" gameName="Kronic" tagLine="NA1" />);
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(screen.getByText("Live")).toBeTruthy();
    expect(screen.getByText(/Auto-refreshes every 60 sec/)).toBeTruthy();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(60_000);
    });
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it("shows loadout, streak, KDA, and recent champion pool on the detailed scout", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === "/api/static/spells") {
        return jsonResponse({
          version: "16.14.1",
          spells: {
            "4": { id: "SummonerFlash", name: "Flash" },
            "12": { id: "SummonerTeleport", name: "Teleport" }
          }
        });
      }
      if (url === "/api/static/runes") {
        return jsonResponse({
          runeById: { "8005": { name: "Press the Attack", icon: "perk-images/Styles/Precision/PressTheAttack/PressTheAttack.png" } }
        });
      }
      if (url.endsWith("/probe")) {
        return jsonResponse({ status: "queued", retryAfterSeconds: 0 });
      }
      return jsonResponse({
        state: "in_game",
        lastUpdatedUtc: new Date().toISOString(),
        dataAgeSeconds: 14,
        participants: [
          {
            puuid: "player-1",
            riotId: "Top Laner#NA1",
            teamId: 100,
            championId: 24,
            spell1Id: 4,
            spell2Id: 12,
            perkIds: [8005],
            profileIconId: 1
          }
        ],
        analysis: {
          participants: [
            {
              puuid: "player-1",
              teamId: 100,
              championId: 24,
              recentWinRate: 0.6,
              recentKda: 3.25,
              currentStreak: 3,
              championPool: [{ championId: 24, games: 8, winRate: 0.625 }]
            }
          ],
          teams: []
        }
      });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<LiveGameCard region="na" gameName="Top Laner" tagLine="NA1" detailed />);

    expect(await screen.findByText("3 win streak")).toBeTruthy();
    expect(screen.getByText("3.25 KDA")).toBeTruthy();
    expect(screen.getByLabelText("Recent champion pool")).toBeTruthy();
    expect(await screen.findByAltText("Flash")).toBeTruthy();
    expect(await screen.findByAltText("Press the Attack")).toBeTruthy();
    expect(screen.getByText("Worker snapshot 14 sec old")).toBeTruthy();
  });
});
