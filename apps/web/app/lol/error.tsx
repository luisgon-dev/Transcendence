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
      title="League of Legends"
      message="We couldn't render this League of Legends page."
      error={error}
      reset={reset}
    />
  );
}
