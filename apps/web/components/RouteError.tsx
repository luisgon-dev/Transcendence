"use client";

import { useEffect } from "react";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Button } from "@/components/ui/Button";

export type RouteErrorProps = {
  /** Heading for the error card (usually the surface or section name). */
  title?: string;
  /** Short, user-facing explanation. */
  message?: string;
  /** The error thrown during render, forwarded by the Next.js error boundary. */
  error: Error & { digest?: string };
  /** Re-render the segment to attempt recovery. */
  reset: () => void;
};

/**
 * Shared, branded fallback for route-level error boundaries. Reuses
 * {@link BackendErrorCard} and wires the retry action to `reset()`.
 */
export function RouteError({
  title = "Something went wrong",
  message = "An unexpected error interrupted this page.",
  error,
  reset
}: RouteErrorProps) {
  useEffect(() => {
    // Surface the error for client-side logging/telemetry without leaking it
    // into the rendered UI.
    console.error(error);
  }, [error]);

  return (
    <BackendErrorCard
      title={title}
      message={message}
      hint="You can retry, or reload the page if the problem persists."
      detail={error.digest ? `Error reference: ${error.digest}` : null}
    >
      <Button type="button" onClick={() => reset()}>
        Try again
      </Button>
    </BackendErrorCard>
  );
}
