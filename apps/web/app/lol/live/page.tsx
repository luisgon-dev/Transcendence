import type { Metadata } from "next";

import { LiveScoutClient } from "@/components/LiveScoutClient";
import { socialImageUrl } from "@/lib/seo";

const title = "Live Game";
const description =
  "Scout an active match by Riot ID. See both teams’ ranks, recent form, spells, and runes.";
const image = socialImageUrl(title, "Live matchup", "Ranks, streaks, champion pools, spells, and runes");

export const metadata: Metadata = {
  title,
  description,
  alternates: { canonical: "/lol/live" },
  openGraph: {
    type: "website",
    title,
    description,
    url: "/lol/live",
    images: [{ url: image, width: 1200, height: 630, alt: title }]
  },
  twitter: { card: "summary_large_image", title, description, images: [image] }
};

export default async function LiveScoutPage({
  searchParams
}: {
  searchParams?: Promise<{ region?: string; riotId?: string; queue?: string }>;
}) {
  const resolved = searchParams ? await searchParams : undefined;
  return <LiveScoutClient initialRegion={resolved?.region} initialRiotId={resolved?.riotId} initialQueue={resolved?.queue?.slice(0, 64)} />;
}
