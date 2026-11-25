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
      title="Page could not load"
      message="We couldn't load this page."
      error={error}
      reset={reset}
    />
  );
}
