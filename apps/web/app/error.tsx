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
      title="Something went wrong"
      message="An unexpected error interrupted this page."
      error={error}
      reset={reset}
    />
  );
}
