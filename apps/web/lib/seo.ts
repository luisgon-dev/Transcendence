import "server-only";

import { getPublicSiteOrigin } from "@/lib/env";

export const SITE_NAME = "Transcendence";

export function getMetadataBase(): URL {
  return new URL(getPublicSiteOrigin());
}

export function socialImageUrl(title: string, eyebrow: string, detail?: string): string {
  const url = new URL("/api/og", getPublicSiteOrigin());
  url.searchParams.set("title", title);
  url.searchParams.set("eyebrow", eyebrow);
  if (detail) url.searchParams.set("detail", detail);
  return url.toString();
}
