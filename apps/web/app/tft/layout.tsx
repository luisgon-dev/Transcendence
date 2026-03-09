import { notFound } from "next/navigation";

import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";

export default function TftLayout({ children }: { children: React.ReactNode }) {
  if (!TFT_FRONTEND_ENABLED) {
    notFound();
  }

  return children;
}
