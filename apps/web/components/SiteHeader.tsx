import { Suspense } from "react";

import { AccountNav } from "@/components/AccountNav";
import { SiteHeaderClient } from "@/components/SiteHeaderClient";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";

export async function SiteHeader() {
  let patch: string | null = null;
  try {
    patch = (await fetchLolAnalyticsStatus())?.patch ?? null;
  } catch {
    // ignore – header still renders without patch badge
  }

  return (
    <SiteHeaderClient patch={patch}>
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
  );
}
