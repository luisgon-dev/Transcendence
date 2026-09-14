"use client";

import * as React from "react";
import { createPortal } from "react-dom";

import { cn } from "@/lib/cn";

// Matches RadixTooltip.Provider's delayDuration/skipDelayDuration in
// <TooltipProvider>, so a delegated tooltip and a regular <Tooltip> feel
// identical: the first one waits, and moving straight on to another trigger
// while the group is still "warm" opens it at once.
const OPEN_DELAY_MS = 250;
const SKIP_DELAY_MS = 300;
const SIDE_OFFSET = 6;
const VIEWPORT_MARGIN = 8;

type Anchor = { text: string; trigger: HTMLElement; rect: DOMRect };

/**
 * One tooltip for a whole region, driven by `data-tooltip` on any descendant.
 *
 * `<Tooltip>` mounts a Radix root per instance. That is the right trade for a
 * handful of them and the wrong one for a table: on `/lol/tierlist` the 173
 * per-row Strength tooltips were the largest single per-row cost on the page —
 * removing them alone was −138ms TBT and −222ms of script evaluation, against
 * −68ms for next/image and −78ms for the win-rate bars. This keeps the same
 * hover affordance, the same delay and the same visuals with one listener and
 * one positioned node for the whole table, and the per-row cost drops to a
 * string attribute.
 *
 * Pointer-only, exactly like the per-row tooltips it replaces: these triggers
 * restate a value that is already rendered in the row, so they deliberately
 * stay out of the tab order rather than adding a stop per row (WCAG 2.4.3 —
 * the same call `Confidence` documents).
 */
export function TooltipZone({
  children,
  className
}: {
  children: React.ReactNode;
  className?: string;
}) {
  const [anchor, setAnchor] = React.useState<Anchor | null>(null);
  const [mounted, setMounted] = React.useState(false);
  const timerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const openRef = React.useRef(false);
  // Until when a newly hovered trigger still counts as part of the warm group.
  const skipDelayUntilRef = React.useRef(0);

  React.useEffect(() => setMounted(true), []);

  const clearTimer = React.useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const hide = React.useCallback(() => {
    clearTimer();
    if (openRef.current) skipDelayUntilRef.current = Date.now() + SKIP_DELAY_MS;
    openRef.current = false;
    setAnchor(null);
  }, [clearTimer]);

  React.useEffect(() => () => clearTimer(), [clearTimer]);

  // The bubble is fixed-positioned, so it has to be told when its row moves.
  // Re-measure rather than dismiss: a tooltip that vanishes the moment the
  // page settles (a tier-rail jump, a sticky header resize) reads as a bug.
  // Capture phase so a scrollable ancestor still reaches us; one rAF-throttled
  // rect read per frame, and only while a tooltip is open.
  const activeTrigger = anchor?.trigger ?? null;
  React.useEffect(() => {
    if (!activeTrigger) return;

    let frame = 0;
    const reposition = () => {
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        if (!activeTrigger.isConnected) {
          hide();
          return;
        }
        const rect = activeTrigger.getBoundingClientRect();
        setAnchor((current) =>
          current && current.trigger === activeTrigger ? { ...current, rect } : current
        );
      });
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") hide();
    };

    window.addEventListener("scroll", reposition, true);
    window.addEventListener("resize", reposition);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      if (frame) cancelAnimationFrame(frame);
      window.removeEventListener("scroll", reposition, true);
      window.removeEventListener("resize", reposition);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [activeTrigger, hide]);

  function handlePointerOver(event: React.PointerEvent<HTMLDivElement>) {
    const trigger = (event.target as Element | null)?.closest<HTMLElement>("[data-tooltip]");
    if (!trigger) return;

    const text = trigger.getAttribute("data-tooltip");
    if (!text) return;

    const show = () => {
      timerRef.current = null;
      openRef.current = true;
      setAnchor({ text, trigger, rect: trigger.getBoundingClientRect() });
    };

    clearTimer();
    // Already open, or open again within the skip window: move to the new
    // trigger at once. Re-waiting 250ms per cell would make reading down a
    // column of rows feel broken.
    if (openRef.current || Date.now() < skipDelayUntilRef.current) show();
    else timerRef.current = setTimeout(show, OPEN_DELAY_MS);
  }

  function handlePointerOut(event: React.PointerEvent<HTMLDivElement>) {
    const trigger = (event.target as Element | null)?.closest<HTMLElement>("[data-tooltip]");
    if (!trigger) return;
    // Ignore moves between children of the same trigger.
    const next = event.relatedTarget as Node | null;
    if (next && trigger.contains(next)) return;
    hide();
  }

  return (
    <div className={className} onPointerOver={handlePointerOver} onPointerOut={handlePointerOut}>
      {children}
      {mounted && anchor
        ? createPortal(<TooltipBubble anchor={anchor} />, document.body)
        : null}
    </div>
  );
}

const ARROW_PX = 8;

function TooltipBubble({ anchor }: { anchor: Anchor }) {
  const nodeRef = React.useRef<HTMLDivElement | null>(null);
  const [placement, setPlacement] = React.useState<{ left: number; arrowLeft: number } | null>(null);

  // Centre on the trigger, then pull back inside the viewport once the real
  // width is known, keeping the arrow under the trigger when the bubble is
  // pushed off-centre. Hidden for that one frame so it never flashes in the
  // wrong place.
  React.useLayoutEffect(() => {
    const node = nodeRef.current;
    if (!node) return;

    const width = node.getBoundingClientRect().width;
    const anchorCentre = anchor.rect.left + anchor.rect.width / 2;
    const maxLeft = Math.max(VIEWPORT_MARGIN, window.innerWidth - width - VIEWPORT_MARGIN);
    const left = Math.round(Math.min(Math.max(anchorCentre - width / 2, VIEWPORT_MARGIN), maxLeft));
    const maxArrow = Math.max(ARROW_PX, width - ARROW_PX * 2);
    const arrowLeft = Math.round(
      Math.min(Math.max(anchorCentre - left - ARROW_PX / 2, ARROW_PX), maxArrow)
    );

    setPlacement({ left, arrowLeft });
  }, [anchor]);

  // Flip below the trigger when there is no room above it.
  const above = anchor.rect.top > 64;
  const top = above ? anchor.rect.top - SIDE_OFFSET : anchor.rect.bottom + SIDE_OFFSET;

  return (
    <div
      ref={nodeRef}
      role="tooltip"
      className={cn(
        "pointer-events-none fixed z-50 max-w-xs rounded-lg border border-border-strong bg-surface px-2.5 py-1.5 type-caption text-fg shadow-overlay",
        placement === null && "invisible"
      )}
      style={{
        left: placement?.left ?? 0,
        top,
        transform: above ? "translateY(-100%)" : undefined
      }}
    >
      {anchor.text}
      <span
        aria-hidden="true"
        className="absolute size-2 rotate-45 border-border-strong bg-surface"
        style={{
          left: placement?.arrowLeft ?? 0,
          ...(above
            ? { bottom: -ARROW_PX / 2, borderRightWidth: 1, borderBottomWidth: 1 }
            : { top: -ARROW_PX / 2, borderLeftWidth: 1, borderTopWidth: 1 })
        }}
      />
    </div>
  );
}
