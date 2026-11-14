"use client";

import { useEffect, useRef, useState } from "react";

/** One delegated listener avoids hydrating every icon in dense tables. Retry is user initiated. */
export function ImageRecovery() {
  const failed = useRef(new Set<HTMLImageElement>());
  const [count, setCount] = useState(0);
  useEffect(() => {
    const sync = () => { for (const image of failed.current) if (!image.isConnected) failed.current.delete(image); setCount(failed.current.size); };
    const error = (event: Event) => { if (event.target instanceof HTMLImageElement) { failed.current.add(event.target); sync(); } };
    const loaded = (event: Event) => { if (event.target instanceof HTMLImageElement) { failed.current.delete(event.target); sync(); } };
    for (const image of document.images) if (image.complete && image.naturalWidth === 0) failed.current.add(image);
    sync();
    document.addEventListener("error", error, true);
    document.addEventListener("load", loaded, true);
    return () => { document.removeEventListener("error", error, true); document.removeEventListener("load", loaded, true); };
  }, []);
  if (!count) return null;
  return <aside role="status" className="fixed bottom-4 left-4 right-4 z-50 mx-auto flex max-w-lg items-center gap-4 rounded-card border border-border-strong bg-surface p-4 text-sm text-fg shadow-overlay">
    <span>Some images could not load.</span>
    <button className="ml-auto min-h-11 shrink-0 rounded-control px-3 font-semibold text-primary focus-visible:outline-2 focus-visible:outline-primary" onClick={() => {
      for (const image of failed.current) {
        if (!image.isConnected) { failed.current.delete(image); continue; }
        const source = new URL(image.currentSrc || image.src, location.href);
        source.searchParams.set("assetRetry", String(Date.now()));
        image.removeAttribute("srcset"); image.setAttribute("src", source.toString());
      }
      setCount(failed.current.size);
    }}>Retry images</button>
  </aside>;
}
