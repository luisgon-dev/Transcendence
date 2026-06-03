"use client";

import * as React from "react";
import Link from "next/link";
import { ToggleGroup } from "radix-ui";

import { cn } from "@/lib/cn";

export type SegmentOption<T extends string = string> = {
  value: T;
  label: React.ReactNode;
  "aria-label"?: string;
};

const groupClass =
  "inline-flex items-center gap-0.5 rounded-control border border-border bg-surface-2/50 p-0.5";

const itemBase =
  "inline-flex min-h-9 items-center justify-center gap-1.5 rounded-[0.45rem] px-3 text-sm font-medium text-muted transition-colors duration-150 ease-[cubic-bezier(0.25,1,0.5,1)] hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40";

const itemActive =
  "bg-surface text-fg shadow-soft data-[state=on]:bg-surface data-[state=on]:text-fg";

// Stateful segmented control (Radix ToggleGroup, single select) — replaces
// hand-rolled pill/tab clusters with a keyboard-accessible roving tabindex.
export function SegmentedControl<T extends string>({
  options,
  value,
  onValueChange,
  ariaLabel,
  className
}: {
  options: SegmentOption<T>[];
  value: T;
  onValueChange: (value: T) => void;
  ariaLabel?: string;
  className?: string;
}) {
  return (
    <ToggleGroup.Root
      type="single"
      value={value}
      onValueChange={(v) => {
        if (v) onValueChange(v as T);
      }}
      aria-label={ariaLabel}
      className={cn(groupClass, className)}
    >
      {options.map((opt) => (
        <ToggleGroup.Item
          key={opt.value}
          value={opt.value}
          aria-label={opt["aria-label"]}
          className={cn(itemBase, itemActive)}
        >
          {opt.label}
        </ToggleGroup.Item>
      ))}
    </ToggleGroup.Root>
  );
}

// Link variant for URL-state filters (role/lane navigation). Same visual
// language; uses aria-current for the active segment.
export function SegmentedLinks({
  options,
  activeValue,
  hrefFor,
  ariaLabel,
  className,
  scroll = false
}: {
  options: SegmentOption[];
  activeValue: string;
  hrefFor: (value: string) => string;
  ariaLabel?: string;
  className?: string;
  scroll?: boolean;
}) {
  return (
    <div role="group" aria-label={ariaLabel} className={cn(groupClass, className)}>
      {options.map((opt) => {
        const active = opt.value === activeValue;
        return (
          <Link
            key={opt.value}
            href={hrefFor(opt.value)}
            scroll={scroll}
            aria-current={active ? "page" : undefined}
            aria-label={opt["aria-label"]}
            className={cn(itemBase, active && "bg-surface text-fg shadow-soft")}
          >
            {opt.label}
          </Link>
        );
      })}
    </div>
  );
}
