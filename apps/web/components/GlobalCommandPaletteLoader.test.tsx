import { act, fireEvent, render, screen } from "@testing-library/react";
import { useEffect } from "react";
import { beforeEach, expect, it, vi } from "vitest";
import { GlobalCommandPaletteLoader } from "./GlobalCommandPaletteLoader";
import { dispatchGlobalSearchOpen, GLOBAL_SEARCH_OPEN_EVENT } from "@/lib/globalSearch";

const observed = vi.hoisted(() => ({ opens: 0, ready: undefined as undefined | (() => void) }));
vi.mock("next/dynamic", () => ({ default: () => function DelayedPalette({ onReady }: { onReady: () => void }) {
  useEffect(() => {
    const open = () => { observed.opens += 1; };
    window.addEventListener(GLOBAL_SEARCH_OPEN_EVENT, open);
    observed.ready = onReady;
    return () => window.removeEventListener(GLOBAL_SEARCH_OPEN_EVENT, open);
  }, [onReady]);
  return <div role="status">Search module loaded</div>;
} }));

beforeEach(() => { observed.opens = 0; observed.ready = undefined; });

it("defers mounting and delivers the first click exactly once after listeners are ready", () => {
  render(<GlobalCommandPaletteLoader />);
  expect(screen.queryByRole("status")).toBeNull();
  act(() => dispatchGlobalSearchOpen());
  expect(screen.getByRole("status")).toBeTruthy();
  expect(observed.opens).toBe(0);
  act(() => observed.ready?.());
  expect(observed.opens).toBe(1);
  act(() => dispatchGlobalSearchOpen());
  expect(observed.opens).toBe(2);
});

it("ignores typing targets and can cancel then reopen a pending keyboard request", () => {
  render(<><input aria-label="Editing" /><GlobalCommandPaletteLoader /></>);
  fireEvent.keyDown(screen.getByRole("textbox"), { key: "k", ctrlKey: true });
  expect(screen.queryByRole("status")).toBeNull();
  fireEvent.keyDown(window, { key: "k", metaKey: true });
  expect(screen.getByRole("status")).toBeTruthy();
  const cancelledReady = observed.ready;
  fireEvent.keyDown(window, { key: "Escape" });
  expect(screen.queryByRole("status")).toBeNull();
  act(() => cancelledReady?.());
  fireEvent.keyDown(window, { key: "k", ctrlKey: true });
  act(() => observed.ready?.());
  expect(observed.opens).toBe(1);
});
