import type { components } from "@transcendence/api-client";
import Link from "next/link";
import { RefreshAnalyticsButton } from "./RefreshAnalyticsButton";

type PatchSelection = components["schemas"]["AnalyticsPatchSelection"];

export function AnalyticsPatchNotice({
  selection,
  latestHref
}: {
  selection?: PatchSelection | null;
  latestHref?: string;
}) {
  if (!selection) return null;
  const { state, activePatch, effectivePatch, requestedPatch } = selection;
  const waitingPatch = requestedPatch ?? activePatch;
  let message: string | null = null;

  if (state === "Preparing") {
    message = waitingPatch
      ? `Stats for patch ${waitingPatch} are being calculated.`
      : "Stats are being calculated.";
  } else if (state === "Unavailable") {
    message = requestedPatch ? `Stats for patch ${requestedPatch} are unavailable.` : "Patch stats are unavailable.";
  } else if (!requestedPatch && effectivePatch && activePatch && effectivePatch !== activePatch) {
    message = `Showing patch ${effectivePatch} while ${activePatch} stats are calculated.`;
  }

  if (!message) return null;
  return (
    <div role="status" className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 rounded-lg border border-border bg-surface-2/65 px-3 py-2 text-sm text-fg">
      <p>{message}</p>
      {state !== "Ready" ? (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          {state === "Preparing" ? <RefreshAnalyticsButton /> : null}
          {latestHref ? (
            <Link href={latestHref} className="font-semibold text-primary hover:underline">
              View latest published stats
            </Link>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
