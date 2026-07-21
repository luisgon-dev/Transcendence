import { act, render, screen } from "@testing-library/react";
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
    const fetchMock = vi.fn(
      () =>
        new Promise<Response>((resolve) => {
          resolveRequest = resolve;
        })
    );
    vi.stubGlobal("fetch", fetchMock);

    render(<LiveGameCard region="na" gameName="Kronic" tagLine="NA1" />);

    expect(screen.getByLabelText("Checking live game")).toBeTruthy();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/trn/app/summoners/na/Kronic/NA1/live-game",
      expect.objectContaining({ cache: "no-store" })
    );

    await act(async () => {
      resolveRequest?.(jsonResponse({ state: "NOT_IN_PROGRESS", participants: [] }));
    });

    expect(await screen.findByText("Not currently in a game.")).toBeTruthy();
    expect(screen.getByText(/^Checked /)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Re-check" })).toBeTruthy();
  });

  it("refreshes detected games on the light one-minute cadence", async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn(async () =>
      jsonResponse({ state: "IN_PROGRESS", participants: [], gameLengthSeconds: 120 })
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
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
