"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

import {
  ANALYTICS_REGION_COOKIE,
  ANALYTICS_REGION_STORAGE_KEY,
  GLOBAL_ANALYTICS_REGION,
  type AnalyticsRegionOption
} from "@/lib/analyticsRegionShared";

function persistRegion(region: string) {
  if (typeof window !== "undefined") {
    window.localStorage.setItem(ANALYTICS_REGION_STORAGE_KEY, region);
  }

  document.cookie = `${ANALYTICS_REGION_COOKIE}=${encodeURIComponent(region)}; path=/; max-age=31536000; samesite=lax`;
}

async function syncPreferredRegion(region: string) {
  try {
    const current = await fetch("/api/trn/user/users/me/preferences", {
      cache: "no-store",
      credentials: "same-origin"
    });

    if (!current.ok) return;

    const preferences = (await current.json()) as {
      preferredRegion?: string | null;
      preferredRankTier?: string | null;
      livePollingEnabled?: boolean;
    };

    await fetch("/api/trn/user/users/me/preferences", {
      method: "PUT",
      headers: {
        "content-type": "application/json"
      },
      credentials: "same-origin",
      body: JSON.stringify({
        preferredRegion: region === GLOBAL_ANALYTICS_REGION ? null : region,
        preferredRankTier: preferences.preferredRankTier ?? null,
        livePollingEnabled: preferences.livePollingEnabled ?? true
      })
    });
  } catch {
    // Best-effort preference sync.
  }
}

export function AnalyticsRegionFilter({
  options,
  activeRegion,
  className
}: {
  options: readonly AnalyticsRegionOption[];
  activeRegion: string;
  className?: string;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  function applyRegion(region: string) {
    const nextParams = new URLSearchParams(searchParams.toString());
    if (region === GLOBAL_ANALYTICS_REGION) {
      nextParams.delete("region");
    } else {
      nextParams.set("region", region);
    }

    persistRegion(region);
    void syncPreferredRegion(region);

    const nextUrl = nextParams.toString() ? `${pathname}?${nextParams.toString()}` : pathname;
    router.push(nextUrl);
  }

  return (
    <div className={className ?? "flex flex-wrap items-center gap-x-4 gap-y-2"}>
      {options.map((option) => {
        const active = option.code === activeRegion;
        return (
          <button
            key={option.code}
            type="button"
            onClick={() => applyRegion(option.code)}
            className="control-tab type-ui min-h-11 px-3 py-2"
            data-active={active}
            aria-pressed={active}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
