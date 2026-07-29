import { Suspense } from "react";

import { AccountNav } from "@/components/AccountNav";
import { GlobalCommandPaletteLoader } from "@/components/GlobalCommandPaletteLoader";
import { SiteHeaderClient } from "@/components/SiteHeaderClient";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";

export async function SiteHeader() {
  // The palette is mounted from here rather than from the root layout: the feature flags are read
  // per request, which only the layout's Suspense boundary around this component permits. The
  // palette portals to the body, so its position in this subtree has no layout effect.
  const flags = await analyticsFeatureFlags();

  let patch: string | null = null;
  try {
    patch = (await fetchLolAnalyticsStatus())?.patch ?? null;
  } catch {
    // ignore – header still renders without patch badge
  }

  return (
    <>
      <SiteHeaderClient patch={patch} buildLabEnabled={flags.buildLab}>
        <Suspense
          fallback={
            <div
              aria-hidden
              className="h-11 w-20 rounded-full bg-surface-2/40"
            />
          }
        >
          <AccountNav />
        </Suspense>
      </SiteHeaderClient>
      <GlobalCommandPaletteLoader buildLabEnabled={flags.buildLab} />
    </>
  );
}
