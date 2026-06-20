"use client";

import { RouteError } from "@/components/RouteError";

export default function Error({
  error,
  reset
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <RouteError
      title="Teamfight Tactics"
      message="We couldn't render this Teamfight Tactics page."
      error={error}
      reset={reset}
    />
  );
}
