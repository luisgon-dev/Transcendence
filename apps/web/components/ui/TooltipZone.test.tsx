import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { TooltipZone } from "./TooltipZone";

function Board() {
  return (
    <TooltipZone>
      <span data-tooltip="Observed 58.0% over 120 games">+3.6%</span>
      <span data-tooltip="Observed 47.1% over 900 games">-1.2%</span>
      <span>no hover story</span>
    </TooltipZone>
  );
}

describe("TooltipZone", () => {
  it("shows the story of whichever trigger the pointer is over", async () => {
    const user = userEvent.setup();
    render(<Board />);

    expect(screen.queryByRole("tooltip")).toBeNull();

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() =>
      expect(screen.getByRole("tooltip").textContent).toContain("Observed 58.0% over 120 games")
    );
  });

  // Reading down a column means crossing trigger after trigger. Re-serving the
  // open delay each time would make the board feel broken, so a hover inside
  // the skip window resolves synchronously — asserted here with no waitFor.
  it("swaps to the next trigger without re-serving the open delay", async () => {
    const user = userEvent.setup();
    render(<Board />);

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());

    await user.hover(screen.getByText("-1.2%"));
    expect(screen.getByRole("tooltip").textContent).toContain("Observed 47.1% over 900 games");
  });

  it("serves the open delay again once the group has gone cold", async () => {
    const user = userEvent.setup();
    render(<Board />);

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());
    await user.unhover(screen.getByText("+3.6%"));
    await new Promise((resolve) => setTimeout(resolve, 400));

    await user.hover(screen.getByText("-1.2%"));
    expect(screen.queryByRole("tooltip")).toBeNull();
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());
  });

  it("hides when the pointer leaves the trigger", async () => {
    const user = userEvent.setup();
    render(<Board />);

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());

    await user.unhover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.queryByRole("tooltip")).toBeNull());
  });

  it("ignores content that carries no story", async () => {
    const user = userEvent.setup();
    render(<Board />);

    await user.hover(screen.getByText("no hover story"));
    await new Promise((resolve) => setTimeout(resolve, 400));

    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("dismisses on Escape", async () => {
    const user = userEvent.setup();
    render(<Board />);

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());

    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("tooltip")).toBeNull());
  });

  it("follows its row on scroll rather than dismissing", async () => {
    const user = userEvent.setup();
    render(<Board />);

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());

    window.dispatchEvent(new Event("scroll"));
    await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
    expect(screen.getByRole("tooltip")).toBeTruthy();
  });

  it("gives up when the row it was anchored to leaves the page", async () => {
    const user = userEvent.setup();
    const { rerender } = render(<Board />);

    await user.hover(screen.getByText("+3.6%"));
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeTruthy());

    // What a filter or a sort does to the row under the pointer.
    rerender(
      <TooltipZone>
        <span data-tooltip="Observed 47.1% over 900 games">-1.2%</span>
      </TooltipZone>
    );
    window.dispatchEvent(new Event("scroll"));
    await waitFor(() => expect(screen.queryByRole("tooltip")).toBeNull());
  });
});
