import {
    analyticsRegionLabel,
    GLOBAL_ANALYTICS_REGION,
    type AnalyticsRegionOption,
} from "@/lib/analyticsRegionShared";

export async function fetchWithGlobalAnalyticsRegionFallback<
    TResult extends { ok: boolean },
>(
    activeRegion: string,
    fetcher: () => Promise<TResult>,
): Promise<{
    result: TResult;
    usedGlobalFallback: boolean;
}> {
    const initial = await fetcher(activeRegion);
    if (initial.ok || activeRegion === GLOBAL_ANALYTICS_REGION) {
        return { result: initial, usedGlobalFallback: false };
    }

    const fallback = await fetcher(GLOBAL_ANALYTICS_REGION);
    if (!fallback.ok) {
        return { result: initial, usedGlobalFallback: false };
    }

    return { result: fallback, usedGlobalFallback: true };
}

export function resolveAnalyticsRegionPresentation(
    activeRegion: string,
    activeRegionLabel: string,
    options: readonly AnalyticsRegionOption[],
    usedGlobalFallback: boolean,
): {
    effectiveRegion: string;
    effectiveRegionLabel: string;
    fallbackMessage: string | null;
} {
    if (!usedGlobalFallback) {
        return {
            effectiveRegion: activeRegion,
            effectiveRegionLabel: activeRegionLabel,
            fallbackMessage: null,
        };
    }

    return {
        effectiveRegion: GLOBAL_ANALYTICS_REGION,
        effectiveRegionLabel: analyticsRegionLabel(
            GLOBAL_ANALYTICS_REGION,
            options,
        ),
        fallbackMessage: `${activeRegionLabel} data is unavailable right now. Showing Global instead.`,
    };
}
