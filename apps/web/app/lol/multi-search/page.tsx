import type { Metadata } from "next";

import { MultiSearchClient } from "@/components/MultiSearchClient";
import { socialImageUrl } from "@/lib/seo";

const title = "Champ Select Multi-Search";
const description = "Paste up to five Riot IDs to compare ranks, main roles, tracked form, champion pools, and autofill risk.";
const image = socialImageUrl(title, "Team scout", "Ranks, role coverage, champion pools, and autofill signals");

export const metadata: Metadata = {
  title,
  description,
  alternates: { canonical: "/lol/multi-search" },
  openGraph: {
    type: "website",
    title,
    description,
    url: "/lol/multi-search",
    images: [{ url: image, width: 1200, height: 630, alt: title }]
  },
  twitter: { card: "summary_large_image", title, description, images: [image] }
};

export default function MultiSearchPage() {
  return <MultiSearchClient />;
}
