"use client";

import * as React from "react";
import { Tooltip as RadixTooltip } from "radix-ui";

import { cn } from "@/lib/cn";

// Lightweight tooltip wrapper. Wrap the app once is unnecessary — Radix's
// Provider is included here per-instance with a sensible delay.
export function Tooltip({
  content,
  children,
  side = "top",
  className
}: {
  content: React.ReactNode;
  children: React.ReactNode;
  side?: "top" | "right" | "bottom" | "left";
  className?: string;
}) {
  return (
    <RadixTooltip.Provider delayDuration={250}>
      <RadixTooltip.Root>
        <RadixTooltip.Trigger asChild>{children}</RadixTooltip.Trigger>
        <RadixTooltip.Portal>
          <RadixTooltip.Content
            side={side}
            sideOffset={6}
            className={cn(
              "z-50 max-w-xs rounded-lg border border-border-strong bg-surface px-2.5 py-1.5 type-caption text-fg shadow-overlay",
              className
            )}
          >
            {content}
            <RadixTooltip.Arrow className="fill-surface" />
          </RadixTooltip.Content>
        </RadixTooltip.Portal>
      </RadixTooltip.Root>
    </RadixTooltip.Provider>
  );
}
