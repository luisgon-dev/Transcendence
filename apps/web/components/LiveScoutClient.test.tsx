import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { LiveScoutClient } from "./LiveScoutClient";

describe("LiveScoutClient", () => {
  it("validates Riot IDs and starts a scout from the first-class entry form", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("/api/static/")) {
        return new Response(JSON.stringify(url.endsWith("spells") ? { version: "16.14.1", spells: {} } : { runeById: {} }), {
          status: 200,
          headers: { "content-type": "application/json" }
        });
      }
      if (url.endsWith("/probe")) {
        return new Response(JSON.stringify({ status: "queued", retryAfterSeconds: 0 }), {
          status: 200,
          headers: { "content-type": "application/json" }
        });
      }
      return new Response(JSON.stringify({ state: "offline", participants: [], dataAgeSeconds: 8, lastUpdatedUtc: new Date().toISOString() }), {
        status: 200,
        headers: { "content-type": "application/json" }
      });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<LiveScoutClient />);
    fireEvent.change(screen.getByLabelText("Riot ID"), { target: { value: "Kronic" } });
    fireEvent.click(screen.getByRole("button", { name: "Scout game" }));
    expect(screen.getByRole("alert").textContent).toContain("GameName#TAG");

    fireEvent.change(screen.getByLabelText("Riot ID"), { target: { value: "Kronic#NA1" } });
    fireEvent.click(screen.getByRole("button", { name: "Scout game" }));

    expect(await screen.findByText("Not currently in a game.")).toBeTruthy();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/trn/app/lol/summoners/na/Kronic/NA1/live-game",
      expect.objectContaining({ cache: "no-store" })
    );
  });

  it("opens a favorite's live game directly from URL-provided inputs", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("/api/static/")) {
        return new Response(JSON.stringify(url.endsWith("spells") ? { version: "16.14.1", spells: {} } : { runeById: {} }), {
          status: 200,
          headers: { "content-type": "application/json" }
        });
      }
      if (url.endsWith("/probe")) {
        return new Response(JSON.stringify({ status: "queued", retryAfterSeconds: 0 }), {
          status: 200,
          headers: { "content-type": "application/json" }
        });
      }
      return new Response(JSON.stringify({ state: "offline", participants: [], dataAgeSeconds: 8, lastUpdatedUtc: new Date().toISOString() }), {
        status: 200,
        headers: { "content-type": "application/json" }
      });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<LiveScoutClient initialRegion="kr" initialRiotId="Hide on bush#KR1" />);

    expect(await screen.findByText("Not currently in a game.")).toBeTruthy();
    expect((screen.getByLabelText("Riot ID") as HTMLInputElement).value).toBe("Hide on bush#KR1");
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/trn/app/lol/summoners/kr/Hide%20on%20bush/KR1/live-game",
      expect.objectContaining({ cache: "no-store" })
    );
  });
});
