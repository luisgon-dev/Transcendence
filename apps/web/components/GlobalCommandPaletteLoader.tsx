"use client";

import dynamic from "next/dynamic";

// The command palette drags framer-motion + cmdk into whatever bundle imports
// it. Loading it via next/dynamic with { ssr: false } keeps those libraries out
// of the shared layout chunk so they are only fetched on the client, after
// hydration. The open trigger is decoupled from render: the palette attaches a
// window `keydown` (Cmd/Ctrl+K) listener and listens for the
// `trn:open-command-palette` CustomEvent (dispatched by GlobalSearchLauncher),
// both wired up in its own useEffect on mount — so lazy-mounting it here does
// not break opening it.
//
// This wrapper is a Client Component on purpose: `ssr: false` is not allowed
// with next/dynamic inside a Server Component (app/layout.tsx), so the dynamic
// import lives here instead.
const GlobalCommandPalette = dynamic(
  () =>
    import("@/components/GlobalCommandPalette").then(
      (mod) => mod.GlobalCommandPalette
    ),
  { ssr: false }
);

// buildLabEnabled arrives as a prop from a Server Component: TRN_FEATURE_BUILD_LAB is unprefixed,
// so reading it here would be `undefined` in the browser and the flag permanently false.
export function GlobalCommandPaletteLoader({ buildLabEnabled = false }: { buildLabEnabled?: boolean }) {
  return <GlobalCommandPalette buildLabEnabled={buildLabEnabled} />;
}
