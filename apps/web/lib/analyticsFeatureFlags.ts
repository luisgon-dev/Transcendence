import "server-only";

import { connection } from "next/server";

export type AnalyticsFeatureFlags = {
  buildLab: boolean;
  championRecommendations: boolean;
  buildReferenceLinks: boolean;
};

function enabled(value: string | undefined) {
  return value?.trim().toLowerCase() === "true";
}

/**
 * TRN_FEATURE_* are runtime container env, not build-time constants: prod flips a flag on the
 * compose service and restarts it, without rebuilding the image. `cacheComponents` prerenders
 * anything it can, so a bare `process.env` read here would be resolved once at image-build time
 * and frozen into the static shell — the flag could never be turned on afterwards. `connection()`
 * moves the read into the request, so every caller must be inside a Suspense boundary.
 *
 * Server-only on purpose: an unprefixed env var is `undefined` in the browser, so a flag a Client
 * Component needs is resolved here and passed down as a prop.
 */
export async function analyticsFeatureFlags(): Promise<AnalyticsFeatureFlags> {
  await connection();
  return {
    buildLab: enabled(process.env.TRN_FEATURE_BUILD_LAB),
    championRecommendations: enabled(process.env.TRN_FEATURE_CHAMPION_RECOMMENDATIONS),
    buildReferenceLinks: enabled(process.env.TRN_FEATURE_BUILD_REFERENCE_LINKS)
  };
}
