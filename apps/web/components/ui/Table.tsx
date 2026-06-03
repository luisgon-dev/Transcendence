import * as React from "react";

import { cn } from "@/lib/cn";

// Presentational table building blocks with consistent density, sticky-head
// support, and a sort affordance. Sorting/state lives in the consuming client
// component; these stay dumb so server and client pages can both use them.

export function TableScroll({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("w-full overflow-x-auto", className)} {...props} />;
}

export function Table({ className, ...props }: React.TableHTMLAttributes<HTMLTableElement>) {
  return <table className={cn("w-full border-collapse text-left", className)} {...props} />;
}

export function Th({
  className,
  align = "left",
  ...props
}: React.ThHTMLAttributes<HTMLTableCellElement> & { align?: "left" | "right" | "center" }) {
  return (
    <th
      scope="col"
      className={cn(
        "type-overline whitespace-nowrap px-3 py-2.5 font-semibold text-muted",
        align === "right" && "text-right",
        align === "center" && "text-center",
        className
      )}
      {...props}
    />
  );
}

export function SortableTh({
  label,
  active,
  direction,
  onSort,
  align = "left",
  className
}: {
  label: React.ReactNode;
  active?: boolean;
  direction?: "asc" | "desc";
  onSort?: () => void;
  align?: "left" | "right" | "center";
  className?: string;
}) {
  return (
    <th
      scope="col"
      aria-sort={active ? (direction === "asc" ? "ascending" : "descending") : undefined}
      className={cn(
        "type-overline whitespace-nowrap px-3 py-2.5 font-semibold text-muted",
        align === "right" && "text-right",
        align === "center" && "text-center",
        className
      )}
    >
      <button
        type="button"
        onClick={onSort}
        className={cn(
          "inline-flex items-center gap-1 select-none transition-colors hover:text-fg",
          align === "right" && "flex-row-reverse",
          active && "text-fg"
        )}
      >
        {label}
        <span aria-hidden="true" className={cn("text-primary", !active && "opacity-0")}>
          {direction === "asc" ? "▲" : "▼"}
        </span>
      </button>
    </th>
  );
}

export function Td({
  className,
  align = "left",
  ...props
}: React.TdHTMLAttributes<HTMLTableCellElement> & { align?: "left" | "right" | "center" }) {
  return (
    <td
      className={cn(
        "px-3 py-2.5 align-middle",
        align === "right" && "text-right",
        align === "center" && "text-center",
        className
      )}
      {...props}
    />
  );
}
