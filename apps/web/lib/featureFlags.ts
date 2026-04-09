export type TftReleaseStage = "off" | "internal" | "beta" | "public";

export function parseTftReleaseStage(raw: string | null | undefined): TftReleaseStage {
  const normalized = raw?.trim().toLowerCase();
  if (normalized === "internal") return "internal";
  if (normalized === "beta") return "beta";
  if (normalized === "public") return "public";
  return "off";
}

export const TFT_RELEASE_STAGE = parseTftReleaseStage(
  process.env.NEXT_PUBLIC_TRN_TFT_RELEASE_STAGE
);

export const TFT_ROUTES_ENABLED = TFT_RELEASE_STAGE !== "off";
export const TFT_PUBLIC_ENABLED = TFT_RELEASE_STAGE === "public";

// Backward-compatible alias for call sites that only care about public visibility.
export const TFT_FRONTEND_ENABLED = TFT_PUBLIC_ENABLED;
