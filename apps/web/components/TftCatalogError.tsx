import { BackendErrorCard } from "@/components/BackendErrorCard";
import type { BackendJsonResult } from "@/lib/backendCall";
import { getErrorVerbosity } from "@/lib/env";

/**
 * Fail-closed error card for the TFT catalog pages (units/items/traits/augments).
 * Mirrors the LoL surface: a failed backend fetch renders an explicit error state
 * rather than silently degrading to an empty grid.
 */
export function TftCatalogError({
  title,
  noun,
  result
}: {
  title: string;
  noun: string;
  result: BackendJsonResult<unknown>;
}) {
  const verbosity = getErrorVerbosity();
  return (
    <BackendErrorCard
      title={title}
      message={
        result.errorKind === "timeout"
          ? `The ${noun} catalog is taking too long to load.`
          : result.errorKind === "unreachable"
            ? "We couldn't reach the TFT service right now."
            : `We couldn't load the TFT ${noun} catalog.`
      }
      hint="Try again in a moment."
      requestId={result.requestId}
      detail={
        verbosity === "verbose"
          ? JSON.stringify({ status: result.status, errorKind: result.errorKind }, null, 2)
          : null
      }
    />
  );
}
