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
      title="Admin"
      message="We couldn't render this admin page."
      error={error}
      reset={reset}
    />
  );
}
