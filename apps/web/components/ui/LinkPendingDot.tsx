"use client";

import { useLinkStatus } from "next/link";

import { Spinner } from "@/components/ui/Spinner";

/**
 * Inline pending indicator for a Next.js <Link>. Renders nothing until the
 * parent Link's navigation is in flight, then shows a small spinner so a
 * clicked tab visibly "loads" even when the server response is slow. Must be
 * rendered as a descendant of the <Link> it reports on (Next 16 useLinkStatus).
 */
export function LinkPendingDot({ className }: { className?: string }) {
  const { pending } = useLinkStatus();
  if (!pending) return null;
  return <Spinner className={className} />;
}
