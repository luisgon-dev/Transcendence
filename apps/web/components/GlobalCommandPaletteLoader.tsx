"use client";

import dynamic from "next/dynamic";
import { useCallback, useEffect, useRef, useState } from "react";
import { dispatchGlobalSearchOpen, GLOBAL_SEARCH_OPEN_EVENT, isGlobalSearchShortcut } from "@/lib/globalSearch";

// Keep the dialog and search libraries off the initial render path. Capture the
// first intent and replay it only after the palette installs its listeners.
const GlobalCommandPalette = dynamic(
  () =>
    import("@/components/GlobalCommandPalette").then(
      (mod) => mod.GlobalCommandPalette
    ),
  { ssr: false, loading: () => <div role="status" className="fixed left-1/2 top-24 z-50 -translate-x-1/2 rounded-card border border-border bg-surface px-5 py-3 text-sm text-fg shadow-overlay">Loading search…</div> }
);

// buildLabEnabled arrives as a prop from a Server Component: TRN_FEATURE_BUILD_LAB is unprefixed,
// so reading it here would be `undefined` in the browser and the flag permanently false.
export function GlobalCommandPaletteLoader({ buildLabEnabled = false }: { buildLabEnabled?: boolean }) {
  const [requested, setRequested] = useState(false);
  const ready = useRef(false);
  const pending = useRef(false);
  const onReady = useCallback(() => {
    if (!pending.current) return;
    ready.current = true;
    if (pending.current) { pending.current = false; dispatchGlobalSearchOpen(); }
  }, []);
  useEffect(() => {
    const open = () => {
      if (ready.current) return;
      pending.current = true; setRequested(true);
    };
    const keydown = (event: KeyboardEvent) => {
      if (ready.current) return;
      if (event.key === "Escape" && pending.current) { pending.current = false; setRequested(false); }
      else if (isGlobalSearchShortcut(event)) { event.preventDefault(); open(); }
    };
    window.addEventListener(GLOBAL_SEARCH_OPEN_EVENT, open);
    window.addEventListener("keydown", keydown);
    return () => {
      window.removeEventListener(GLOBAL_SEARCH_OPEN_EVENT, open);
      window.removeEventListener("keydown", keydown);
    };
  }, []);
  return requested ? <GlobalCommandPalette buildLabEnabled={buildLabEnabled} onReady={onReady} /> : null;
}
