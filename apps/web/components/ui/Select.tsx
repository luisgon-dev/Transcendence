"use client";

import { Select as RadixSelect } from "radix-ui";

import { cn } from "@/lib/cn";

export type SelectOption = { value: string; label: string };

// Styled, accessible single-select (Radix). Used for rank/region/patch filters
// in place of native <select>, with a consistent popover surface in both themes.
export function Select({
  value,
  onValueChange,
  options,
  ariaLabel,
  placeholder = "Select…",
  className
}: {
  value: string;
  onValueChange: (value: string) => void;
  options: SelectOption[];
  ariaLabel?: string;
  placeholder?: string;
  className?: string;
}) {
  return (
    <RadixSelect.Root value={value} onValueChange={onValueChange}>
      <RadixSelect.Trigger
        aria-label={ariaLabel}
        className={cn(
          "inline-flex h-11 min-w-0 items-center justify-between gap-2 rounded-control border border-border bg-surface px-3 text-sm text-fg transition-colors hover:border-border-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 sm:h-9",
          className
        )}
      >
        <RadixSelect.Value placeholder={placeholder} />
        <RadixSelect.Icon className="text-muted">
          <svg viewBox="0 0 12 12" className="size-3" aria-hidden="true">
            <path d="M3 4.5 6 7.5 9 4.5" stroke="currentColor" strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </RadixSelect.Icon>
      </RadixSelect.Trigger>
      <RadixSelect.Portal>
        <RadixSelect.Content
          position="popper"
          sideOffset={6}
          className="z-50 max-h-[var(--radix-select-content-available-height)] min-w-[var(--radix-select-trigger-width)] overflow-hidden rounded-card border border-border-strong bg-surface shadow-overlay"
        >
          <RadixSelect.Viewport className="p-1">
            {options.map((opt) => (
              <RadixSelect.Item
                key={opt.value}
                value={opt.value}
                className="flex cursor-pointer select-none items-center justify-between gap-2 rounded-[0.45rem] px-2.5 py-2 text-sm text-fg outline-none data-[highlighted]:bg-surface-2 data-[state=checked]:text-primary"
              >
                <RadixSelect.ItemText>{opt.label}</RadixSelect.ItemText>
                <RadixSelect.ItemIndicator>
                  <svg viewBox="0 0 12 12" className="size-3" aria-hidden="true">
                    <path d="M2.5 6.5 5 9l4.5-5.5" stroke="currentColor" strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </RadixSelect.ItemIndicator>
              </RadixSelect.Item>
            ))}
          </RadixSelect.Viewport>
        </RadixSelect.Content>
      </RadixSelect.Portal>
    </RadixSelect.Root>
  );
}
