import { notFound } from "next/navigation";

import { TFT_ROUTES_ENABLED } from "@/lib/featureFlags";

export default function TftLayout({ children }: { children: React.ReactNode }) {
  if (!TFT_ROUTES_ENABLED) {
    notFound();
  }

  return children;
}
