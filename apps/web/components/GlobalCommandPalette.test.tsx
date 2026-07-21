import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { GlobalCommandPalette } from "./GlobalCommandPalette";

const router = vi.hoisted(() => ({
  push: vi.fn(),
  prefetch: vi.fn()
}));

vi.mock("next/navigation", () => ({
  useRouter: () => router
}));

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "content-type": "application/json" }
  });
}

describe("GlobalCommandPalette", () => {
  beforeEach(() => {
    router.push.mockReset();
    router.prefetch.mockReset();
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: string | URL | Request) => {
        const url = String(input);
        if (url === "/api/static/champions") {
          return jsonResponse({
            version: "16.14.1",
            champions: {
              "103": { id: "Ahri", name: "Ahri" },
              "86": { id: "Garen", name: "Garen" }
            }
          });
        }

        return jsonResponse({ items: [] });
      })
    );
  });

  it("opens as a focused modal and returns focus when dismissed", async () => {
    const user = userEvent.setup();
    render(
      <>
        <button type="button">Search launcher</button>
        <GlobalCommandPalette />
      </>
    );
    const launcher = screen.getByRole("button", { name: "Search launcher" });
    launcher.focus();

    fireEvent.keyDown(window, { key: "k", ctrlKey: true });

    const dialog = await screen.findByRole("dialog", { name: "Global search" });
    expect(dialog.getAttribute("aria-modal")).toBe("true");
    const input = screen.getByRole("combobox", { name: "Global search input" });
    await waitFor(() => expect(document.activeElement).toBe(input));

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Global search" })).toBeNull());
    expect(document.activeElement).toBe(launcher);
  });

  it("routes a complete Riot ID directly from the keyboard", async () => {
    const user = userEvent.setup();
    render(<GlobalCommandPalette />);
    fireEvent.keyDown(window, { key: "k", metaKey: true });

    const input = await screen.findByRole("combobox", { name: "Global search input" });
    await user.type(input, "Kronic#NA1");
    expect(await screen.findByText("Kronic#NA1 in NA")).toBeTruthy();

    await user.keyboard("{Enter}");
    await waitFor(() => expect(router.push).toHaveBeenCalledWith("/lol/summoners/na/Kronic-NA1"));
  });
});
