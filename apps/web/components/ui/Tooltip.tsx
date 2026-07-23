"use client";

import * as React from "react";
import { Tooltip as RadixTooltip } from "radix-ui";

import { cn } from "@/lib/cn";

export function TooltipProvider({ children }: { children: React.ReactNode }) {
  return <RadixTooltip.Provider delayDuration={250}>{children}</RadixTooltip.Provider>;
}

// Lightweight tooltip wrapper. A single provider is mounted in the root layout;
// each tooltip contributes only its Root, Trigger, Portal, and Content.
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
  );
}
